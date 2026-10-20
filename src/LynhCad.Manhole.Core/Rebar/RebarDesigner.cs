using System;
using System.Collections.Generic;
using System.Linq;
using LynhCad.Manhole.Core.Geometry;
using LynhCad.Manhole.Core.Model;

namespace LynhCad.Manhole.Core.Rebar
{
    /// <summary>Một lớp thép trong bản (đáy hoặc nắp), chạy theo X hoặc Y.</summary>
    public class MatLayer
    {
        public string Key;
        public int Dia;
        public double Spacing;
        public bool AlongX;
        public bool IsTop;
        /// <summary>Cao độ tim thanh (mm so với mặt bản đáy).</summary>
        public double Z;
        /// <summary>Khoảng chạy của thanh dọc theo phương của nó.</summary>
        public Interval Run;
        /// <summary>Toạ độ vuông góc của từng thanh.</summary>
        public List<double> Positions = new List<double>();
        /// <summary>Vùng bị lỗ thăm cắt (đã cộng lớp bảo vệ). Null nếu bản không có lỗ.</summary>
        public Interval? CutAlong, CutPerp;

        /// <summary>Các đoạn thực tế của thanh tại vị trí pos (thanh bị cắt bởi lỗ thăm thì tách đôi).</summary>
        public List<Interval> Pieces(double pos)
        {
            if (CutAlong == null || CutPerp == null || !CutPerp.Value.Contains(pos))
                return new List<Interval> { Run };
            return Run.Subtract(new[] { CutAlong.Value }).Where(i => i.Length >= RebarDesigner.MinPiece).ToList();
        }
    }

    /// <summary>Thanh gia cường quanh lỗ thăm bản nắp.</summary>
    public class TrimmerBar
    {
        public string Key;
        public bool AlongX;
        public double Pos;      // toạ độ vuông góc
        public Interval Run;
        public double Z;
        public int Dia;
    }

    /// <summary>Các giá trị bố trí thép dùng chung cho bảng thống kê và bản vẽ (để hai bên luôn khớp nhau).</summary>
    public class RebarLayout
    {
        public double Cover;
        // Tường (chỉ hố chữ nhật)
        public int DV, DH;
        public double SV, SH;
        /// <summary>Khoảng từ mặt tường tới tim thép ngang (lớp ngoài) / thép đứng (lớp trong).</summary>
        public double HorizOffset, VertOffset;
        public List<double> HorizLevels = new List<double>();
        public double VertZBot, VertZTop, VertLeg;
        public double Lap;

        public readonly List<MatLayer> BaseLayers = new List<MatLayer>();
        public readonly List<MatLayer> RoofLayers = new List<MatLayer>();
        public readonly List<TrimmerBar> RoofTrimmers = new List<TrimmerBar>();
        public int TrimmerDia;

        public IEnumerable<MatLayer> AllLayers { get { return BaseLayers.Concat(RoofLayers); } }
    }

    public class RebarDesign
    {
        public RebarLayout Layout;
        public RebarSchedule Schedule;
    }

    /// <summary>
    /// Bố trí thép theo nguyên tắc thông dụng (đơn giản hoá) cho hố ga BTCT:
    ///  - Tường: thép đứng chân L neo vào đáy, thép ngang dạng 2 thanh U mỗi lớp, hai mặt (EF).
    ///  - Bản đáy: lưới dưới (+ lưới trên tuỳ chọn), hai phương.
    ///  - Bản nắp: lưới dưới (+ lưới trên tuỳ chọn), thanh bị lỗ thăm cắt được tách và thống kê riêng,
    ///    có thanh gia cường (trimmer) mỗi cạnh lỗ.
    ///  - Lỗ ống xuyên tường: thép cắt tại chỗ + trimmer quanh lỗ, hai mặt.
    /// KẾT QUẢ CẦN KỸ SƯ KẾT CẤU KIỂM TRA — tool không tính toán chịu lực.
    /// </summary>
    public static class RebarDesigner
    {
        public const double MinPiece = 300;
        public const double TrimmerPitch = 75;

        public static RebarDesign Design(ManholeGeometry g)
        {
            var P = g.P;
            var L = new RebarLayout { Cover = P.Cover, TrimmerDia = P.TrimmerDiameter };
            var items = new List<BarItem>();
            var sets = new List<BarSetInfo>();
            double c = P.Cover;

            // ---------------- Bản đáy ----------------
            AddMat(L.BaseLayers, items, sets, "BaseBot", P.BaseBottom, false, 'B', c,
                   g.BaseHalfX, g.BaseHalfY, -g.Tb, null, null);
            if (P.BaseTopMat)
                AddMat(L.BaseLayers, items, sets, "BaseTop", P.BaseTop, true, 'B', c,
                       g.BaseHalfX, g.BaseHalfY, 0, null, null);

            // ---------------- Bản nắp ----------------
            var o = g.OpeningCenter;
            var cutX = new Interval(o.X - g.OpeningHalfX - c, o.X + g.OpeningHalfX + c);
            var cutY = new Interval(o.Y - g.OpeningHalfY - c, o.Y + g.OpeningHalfY + c);
            AddMat(L.RoofLayers, items, sets, "RoofBot", P.RoofBottom, false, 'R', c,
                   g.RoofHalfX, g.RoofHalfY, g.H, cutX, cutY);
            if (P.RoofTopMat)
                AddMat(L.RoofLayers, items, sets, "RoofTop", P.RoofTop, true, 'R', c,
                       g.RoofHalfX, g.RoofHalfY, g.H + g.Ts, cutX, cutY);

            AddRoofTrimmers(g, L, items, sets);

            // ---------------- Tường (hố chữ nhật) ----------------
            if (g.IsRect) AddWalls(g, L, items, sets);

            return new RebarDesign { Layout = L, Schedule = RebarSchedule.Build(items, sets) };
        }

        /// <summary>Thêm một lưới 2 lớp (X ở ngoài cùng, Y ở trong).</summary>
        private static void AddMat(List<MatLayer> layers, List<BarItem> items, List<BarSetInfo> sets,
            string keyPrefix, BarSpec spec, bool isTop, char group, double c,
            double hx, double hy, double zFace, Interval? cutX, Interval? cutY)
        {
            int d = spec.Diameter;
            double s = spec.Spacing;
            string suffix = isTop ? "TOP" : "BOT";
            // Lớp 1 (ngoài cùng) chạy theo X, lớp 2 chạy theo Y
            double z1 = isTop ? zFace - c - d / 2.0 : zFace + c + d / 2.0;
            double z2 = isTop ? z1 - d : z1 + d;

            for (int k = 0; k < 2; k++)
            {
                bool alongX = k == 0;
                var layer = new MatLayer
                {
                    Key = keyPrefix + (alongX ? "X" : "Y"),
                    Dia = d, Spacing = s, AlongX = alongX, IsTop = isTop, Z = alongX ? z1 : z2,
                };
                double hRun = alongX ? hx : hy, hPerp = alongX ? hy : hx;
                layer.Run = new Interval(-hRun + c, hRun - c);
                layer.Positions = GeomUtil.Distribute(-hPerp + c + d / 2.0, hPerp - c - d / 2.0, s);
                if (cutX != null && cutY != null)
                {
                    layer.CutAlong = alongX ? cutX : cutY;
                    layer.CutPerp = alongX ? cutY : cutX;
                }
                layers.Add(layer);

                sets.Add(new BarSetInfo
                {
                    Key = layer.Key, Dia = d, Spacing = s,
                    Suffix = suffix + (alongX ? " (E-W)" : " (N-S)"),
                });
                foreach (var pos in layer.Positions)
                    foreach (var piece in layer.Pieces(pos))
                        items.Add(new BarItem { SetKey = layer.Key, Group = group, Dia = d, Shape = BarShape.Straight, A = piece.Length, Count = 1 });
            }
        }

        private static void AddRoofTrimmers(ManholeGeometry g, RebarLayout L, List<BarItem> items, List<BarSetInfo> sets)
        {
            var P = g.P;
            int n = Math.Max(0, P.TrimmerCount);
            if (n == 0) return;
            int dt = P.TrimmerDiameter;
            double c = P.Cover, lap = g.LapLength(dt);
            var o = g.OpeningCenter;

            foreach (var layer in L.RoofLayers)
            {
                bool ax = layer.AlongX;
                double oAlong = ax ? o.X : o.Y, oPerp = ax ? o.Y : o.X;
                double hAlong = ax ? g.OpeningHalfX : g.OpeningHalfY, hPerp = ax ? g.OpeningHalfY : g.OpeningHalfX;
                var run = new Interval(Math.Max(layer.Run.Min, oAlong - hAlong - lap), Math.Min(layer.Run.Max, oAlong + hAlong + lap));
                string key = "RoofTrim";
                for (int side = -1; side <= 1; side += 2)
                    for (int k = 0; k < n; k++)
                    {
                        double pos = oPerp + side * (hPerp + c + dt / 2.0 + k * TrimmerPitch);
                        L.RoofTrimmers.Add(new TrimmerBar { Key = key, AlongX = ax, Pos = pos, Run = run, Z = layer.Z, Dia = dt });
                        items.Add(new BarItem { SetKey = key, Group = 'T', Dia = dt, Shape = BarShape.Straight, A = run.Length, Count = 1 });
                    }
            }
            sets.Add(new BarSetInfo { Key = "RoofTrim", Dia = dt, FixedCount = n, Suffix = "TRIMMERS EACH SIDE OF OPENING" + (P.RoofTopMat ? " T&B" : "") });
        }

        private static void AddWalls(ManholeGeometry g, RebarLayout L, List<BarItem> items, List<BarSetInfo> sets)
        {
            var P = g.P;
            double c = P.Cover;
            L.DV = P.WallVertical.Diameter; L.SV = P.WallVertical.Spacing;
            L.DH = P.WallHorizontal.Diameter; L.SH = P.WallHorizontal.Spacing;
            L.HorizOffset = c + L.DH / 2.0;
            L.VertOffset = c + L.DH + L.DV / 2.0;
            L.Lap = g.LapLength(L.DH);

            // ---- Thép đứng: chân L neo vào đáy, đặt lên lưới dưới ----
            double baseBotDepth = 0, roofTopDepth = 0;
            foreach (var ly in L.BaseLayers.Where(x => !x.IsTop)) baseBotDepth += ly.Dia;
            foreach (var ly in L.RoofLayers.Where(x => x.IsTop)) roofTopDepth += ly.Dia;
            L.VertZBot = -g.Tb + c + baseBotDepth;
            L.VertZTop = g.H + g.Ts - c - roofTopDepth;
            L.VertLeg = P.VerticalBarLeg;
            double vertA = L.VertZTop - L.VertZBot;

            int vertCount = 0;
            foreach (var loop in VerticalLoops(g, L))
                vertCount += LoopPositions(loop.Item1, loop.Item2, L.SV).Count;
            sets.Add(new BarSetInfo { Key = "WallVert", Dia = L.DV, Spacing = L.SV, Suffix = "EF VERT" });
            items.Add(new BarItem { SetKey = "WallVert", Group = 'W', Dia = L.DV, Shape = BarShape.L, A = vertA, B = L.VertLeg, Count = vertCount });

            // ---- Thép ngang: mỗi cao độ, mỗi mặt = 2 thanh U nối chồng ở giữa cạnh ngắn ----
            L.HorizLevels = GeomUtil.Distribute(50, g.H - 50, L.SH);
            int nLevels = L.HorizLevels.Count;
            sets.Add(new BarSetInfo { Key = "WallHoriz", Dia = L.DH, Spacing = L.SH, Suffix = "EF HORIZ" });
            foreach (var loop in HorizontalLoops(g, L))
            {
                double bx = loop.Item1, by = loop.Item2;
                double leg = Math.Min(2 * by, by + L.Lap / 2);
                items.Add(new BarItem { SetKey = "WallHoriz", Group = 'W', Dia = L.DH, Shape = BarShape.U, A = leg, B = 2 * bx, C = leg, Count = 2 * nLevels });
            }

            // ---- Trimmer quanh lỗ ống xuyên tường ----
            int n = Math.Max(0, P.TrimmerCount);
            if (n > 0 && g.Pipes.Count > 0)
            {
                int dt = P.TrimmerDiameter;
                double lap = g.LapLength(dt);
                double maxVert = g.H + g.Tb + g.Ts - 2 * c;
                foreach (var f in g.Pipes)
                {
                    double vLen = Math.Min(maxVert, f.GapZ.Length + 2 * lap);
                    double hLen = 2 * f.HalfGap + 2 * lap;
                    // 2 mặt × (n mỗi bên × 2 bên)
                    items.Add(new BarItem { SetKey = "PipeTrim", Group = 'T', Dia = dt, Shape = BarShape.Straight, A = vLen, Count = 2 * 2 * n });
                    items.Add(new BarItem { SetKey = "PipeTrim", Group = 'T', Dia = dt, Shape = BarShape.Straight, A = hLen, Count = 2 * 2 * n });
                }
                sets.Add(new BarSetInfo { Key = "PipeTrim", Dia = dt, FixedCount = n, Suffix = "TRIMMERS EACH SIDE OF PENETRATION EF" });
            }
        }

        /// <summary>Hai vòng thép đứng (mặt trong, mặt ngoài) dạng nửa kích thước (ax, ay).</summary>
        public static List<Tuple<double, double>> VerticalLoops(ManholeGeometry g, RebarLayout L)
        {
            return new List<Tuple<double, double>>
            {
                Tuple.Create(g.Li / 2 + L.VertOffset, g.Wi / 2 + L.VertOffset),
                Tuple.Create(g.Lo / 2 - L.VertOffset, g.Wo / 2 - L.VertOffset),
            };
        }

        /// <summary>Hai vòng thép ngang (mặt trong, mặt ngoài) dạng nửa kích thước (bx, by).</summary>
        public static List<Tuple<double, double>> HorizontalLoops(ManholeGeometry g, RebarLayout L)
        {
            return new List<Tuple<double, double>>
            {
                Tuple.Create(g.Li / 2 + L.HorizOffset, g.Wi / 2 + L.HorizOffset),
                Tuple.Create(g.Lo / 2 - L.HorizOffset, g.Wo / 2 - L.HorizOffset),
            };
        }

        /// <summary>Vị trí thanh đứng rải quanh vòng chữ nhật (ax, ay), mỗi góc một thanh, không trùng.</summary>
        public static List<Vec2> LoopPositions(double ax, double ay, double s)
        {
            var corners = new[] { new Vec2(-ax, -ay), new Vec2(ax, -ay), new Vec2(ax, ay), new Vec2(-ax, ay) };
            var pts = new List<Vec2>();
            for (int i = 0; i < 4; i++)
            {
                var a = corners[i]; var b = corners[(i + 1) % 4];
                double len = (b - a).Length;
                var ts = GeomUtil.Distribute(0, len, s);
                for (int k = 0; k < ts.Count - 1; k++) // bỏ điểm cuối (là góc kế tiếp)
                    pts.Add(a + (b - a) * (ts[k] / len));
            }
            return pts;
        }
    }
}
