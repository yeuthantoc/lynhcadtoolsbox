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
        /// <summary>Khoảng từ mặt tường tới tim thép ngang (lớp ngoài) / thép đứng (lớp trong) khi đặt 2 lớp.</summary>
        public double HorizOffset, VertOffset;
        /// <summary>Số lớp thép tường: 2 = hai mặt (EF), 1 = một lớp giữa tường (CENTRAL).</summary>
        public int WallLayers = 2;
        /// <summary>Khoảng từ MẶT TRONG tường tới tim từng lớp thép đứng / thép ngang (mặt trong trước, mặt ngoài sau).</summary>
        public readonly List<double> VertFromInner = new List<double>(), HorizFromInner = new List<double>();
        public List<double> HorizLevels = new List<double>();
        public double VertZBot, VertZTop, VertLeg;
        public double Lap;

        public readonly List<MatLayer> BaseLayers = new List<MatLayer>();
        public readonly List<MatLayer> RoofLayers = new List<MatLayer>();
        public readonly List<TrimmerBar> RoofTrimmers = new List<TrimmerBar>();
        public int TrimmerDia;

        public IEnumerable<MatLayer> AllLayers { get { return BaseLayers.Concat(RoofLayers); } }
    }

    /// <summary>Vòng thép chữ nhật quanh tâm hố, ghi bằng nửa kích thước theo X và Y.</summary>
    public struct HalfSize
    {
        public readonly double X, Y;
        public HalfSize(double x, double y) { X = x; Y = y; }
    }

    public class RebarDesign
    {
        public RebarLayout Layout;
        public RebarSchedule Schedule;
    }

    /// <summary>
    /// Bố trí thép theo nguyên tắc thông dụng (đơn giản hoá) cho hố ga BTCT:
    ///  - Tường đổ tại chỗ: thép đứng chân L neo vào đáy, hai mặt (EF). Thép ngang: hố chữ nhật 2 thanh U mỗi lớp,
    ///    hố tròn 1 vòng kín có nối chồng mỗi lớp.
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
            AddMat(L.BaseLayers, items, sets, BarSets.BaseBot, P.BaseBottom, false, 'B', c,
                   g.BaseHalfX, g.BaseHalfY, -g.Tb, null, null);
            if (P.BaseTopMat)
                AddMat(L.BaseLayers, items, sets, BarSets.BaseTop, P.BaseTop, true, 'B', c,
                       g.BaseHalfX, g.BaseHalfY, 0, null, null);

            // ---------------- Bản nắp ----------------
            var o = g.OpeningCenter;
            var cutX = new Interval(o.X - g.OpeningHalfX - c, o.X + g.OpeningHalfX + c);
            var cutY = new Interval(o.Y - g.OpeningHalfY - c, o.Y + g.OpeningHalfY + c);
            AddMat(L.RoofLayers, items, sets, BarSets.RoofBot, P.RoofBottom, false, 'R', c,
                   g.RoofHalfX, g.RoofHalfY, g.H, cutX, cutY);
            if (P.RoofTopMat)
                AddMat(L.RoofLayers, items, sets, BarSets.RoofTop, P.RoofTop, true, 'R', c,
                       g.RoofHalfX, g.RoofHalfY, g.H + g.Ts, cutX, cutY);

            AddRoofTrimmers(g, L, items, sets);

            // ---------------- Tường đổ tại chỗ (chữ nhật hoặc tròn) ----------------
            if (g.HasCastWalls) AddWalls(g, L, items, sets);

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
                    Key = alongX ? BarSets.X(keyPrefix) : BarSets.Y(keyPrefix),
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
                string key = BarSets.RoofTrim;
                for (int side = -1; side <= 1; side += 2)
                    for (int k = 0; k < n; k++)
                    {
                        double pos = oPerp + side * (hPerp + c + dt / 2.0 + k * TrimmerPitch);
                        L.RoofTrimmers.Add(new TrimmerBar { Key = key, AlongX = ax, Pos = pos, Run = run, Z = layer.Z, Dia = dt });
                        items.Add(new BarItem { SetKey = key, Group = 'T', Dia = dt, Shape = BarShape.Straight, A = run.Length, Count = 1 });
                    }
            }
            sets.Add(new BarSetInfo { Key = BarSets.RoofTrim, Dia = dt, FixedCount = n, Suffix = "TRIMMERS EACH SIDE OF OPENING" + (P.RoofTopMat ? " T&B" : "") });
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
            L.WallLayers = P.WallSingleLayer ? 1 : 2;
            L.VertFromInner.Clear(); L.HorizFromInner.Clear();
            if (L.WallLayers == 1)
            {
                // một lớp giữa tường: thép đứng đúng tim tường, thép ngang buộc phía ngoài thép đứng
                L.VertFromInner.Add(g.Tw / 2);
                L.HorizFromInner.Add(g.Tw / 2 + (L.DV + L.DH) / 2.0);
            }
            else
            {
                L.VertFromInner.Add(L.VertOffset); L.VertFromInner.Add(g.Tw - L.VertOffset);
                L.HorizFromInner.Add(L.HorizOffset); L.HorizFromInner.Add(g.Tw - L.HorizOffset);
            }
            string faces = L.WallLayers == 1 ? "CENTRAL" : "EF";

            // ---- Thép đứng: chân L neo vào đáy, đặt lên lưới dưới ----
            double baseBotDepth = 0, roofTopDepth = 0;
            foreach (var ly in L.BaseLayers.Where(x => !x.IsTop)) baseBotDepth += ly.Dia;
            foreach (var ly in L.RoofLayers.Where(x => x.IsTop)) roofTopDepth += ly.Dia;
            L.VertZBot = -g.Tb + c + baseBotDepth;
            L.VertZTop = g.H + g.Ts - c - roofTopDepth;
            L.VertLeg = P.VerticalBarLeg;
            double vertA = L.VertZTop - L.VertZBot;

            int vertCount = VerticalPositions(g, L).Count;
            sets.Add(new BarSetInfo { Key = BarSets.WallVert, Dia = L.DV, Spacing = L.SV, Suffix = faces + " VERT" });
            items.Add(new BarItem { SetKey = BarSets.WallVert, Group = 'W', Dia = L.DV, Shape = BarShape.L, A = vertA, B = L.VertLeg, Count = vertCount });

            // ---- Thép ngang ----
            L.HorizLevels = GeomUtil.Distribute(50, g.H - 50, L.SH);
            int nLevels = L.HorizLevels.Count;
            sets.Add(new BarSetInfo { Key = BarSets.WallHoriz, Dia = L.DH, Spacing = L.SH, Suffix = faces + " HORIZ" });
            if (g.IsRect)
            {
                // mỗi cao độ, mỗi mặt = 2 thanh U nối chồng ở giữa cạnh ngắn
                foreach (var loop in HorizontalLoops(g, L))
                {
                    double bx = loop.X, by = loop.Y;
                    double leg = Math.Min(2 * by, by + L.Lap / 2);
                    items.Add(new BarItem { SetKey = BarSets.WallHoriz, Group = 'W', Dia = L.DH, Shape = BarShape.U, A = leg, B = 2 * bx, C = leg, Count = 2 * nLevels });
                }
            }
            else
            {
                // mỗi cao độ, mỗi mặt = 1 vòng kín, nối chồng một chỗ
                foreach (var r in HorizontalRingRadii(g, L))
                    items.Add(new BarItem { SetKey = BarSets.WallHoriz, Group = 'W', Dia = L.DH, Shape = BarShape.Ring, A = 2 * r, B = L.Lap, Count = nLevels });
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
                    // số lớp × (n mỗi bên × 2 bên)
                    items.Add(new BarItem { SetKey = BarSets.PipeTrim, Group = 'T', Dia = dt, Shape = BarShape.Straight, A = vLen, Count = L.WallLayers * 2 * n });
                    items.Add(new BarItem { SetKey = BarSets.PipeTrim, Group = 'T', Dia = dt, Shape = BarShape.Straight, A = hLen, Count = L.WallLayers * 2 * n });
                }
                sets.Add(new BarSetInfo { Key = BarSets.PipeTrim, Dia = dt, FixedCount = n, Suffix = "TRIMMERS EACH SIDE OF PENETRATION" + (L.WallLayers == 2 ? " EF" : "") });
            }
        }

        /// <summary>Các vòng thép đứng (mặt trong trước, mặt ngoài sau; 1 vòng nếu thép 1 lớp) dạng nửa kích thước (ax, ay).</summary>
        public static List<HalfSize> VerticalLoops(ManholeGeometry g, RebarLayout L)
        {
            return L.VertFromInner.Select(v => new HalfSize(g.Li / 2 + v, g.Wi / 2 + v)).ToList();
        }

        /// <summary>Các vòng thép ngang (mặt trong trước, mặt ngoài sau; 1 vòng nếu thép 1 lớp) dạng nửa kích thước (bx, by).</summary>
        public static List<HalfSize> HorizontalLoops(ManholeGeometry g, RebarLayout L)
        {
            return L.HorizFromInner.Select(v => new HalfSize(g.Li / 2 + v, g.Wi / 2 + v)).ToList();
        }

        /// <summary>Hố tròn: bán kính tim các vòng thép đứng (mặt trong trước, mặt ngoài sau).</summary>
        public static List<double> VerticalRingRadii(ManholeGeometry g, RebarLayout L)
        {
            return L.VertFromInner.Select(v => g.Ri + v).ToList();
        }

        /// <summary>Hố tròn: bán kính tim các vòng thép ngang (mặt trong trước, mặt ngoài sau).</summary>
        public static List<double> HorizontalRingRadii(ManholeGeometry g, RebarLayout L)
        {
            return L.HorizFromInner.Select(v => g.Ro - (g.Tw - v)).ToList();
        }

        /// <summary>Số thanh đứng trên vòng tròn bán kính r để khoảng cách theo cung ≤ s (tối thiểu 4).</summary>
        public static int RingCount(double r, double s)
        {
            return Math.Max(4, (int)Math.Ceiling(2 * Math.PI * r / s - 1e-9));
        }

        /// <summary>
        /// Vị trí mọi thanh đứng của tường (mặt bằng), các mặt THẲNG HÀNG với nhau: thanh ở mặt ngoài nằm đúng
        /// vị trí dọc tường (hoặc đúng góc, hố tròn) của thanh mặt trong. Hố chữ nhật: vị trí rải theo vòng trong,
        /// vòng ngoài thêm 4 thanh góc; hố tròn: cùng số thanh, cùng góc trên mọi vòng (số thanh tính theo vòng lớn nhất).
        /// </summary>
        public static List<Vec2> VerticalPositions(ManholeGeometry g, RebarLayout L)
        {
            var pts = new List<Vec2>();
            if (g.IsRect)
            {
                var loops = VerticalLoops(g, L);
                if (loops.Count == 0) return pts;
                var xs = GeomUtil.Distribute(-loops[0].X, loops[0].X, L.SV);
                var ys = GeomUtil.Distribute(-loops[0].Y, loops[0].Y, L.SV);
                foreach (var loop in loops)
                {
                    foreach (var x in xs) foreach (int sy in new[] { -1, 1 }) AddUnique(pts, new Vec2(x, sy * loop.Y));
                    foreach (var y in ys) foreach (int sx in new[] { -1, 1 }) AddUnique(pts, new Vec2(sx * loop.X, y));
                    foreach (int sx in new[] { -1, 1 }) foreach (int sy in new[] { -1, 1 }) AddUnique(pts, new Vec2(sx * loop.X, sy * loop.Y));
                }
            }
            else
            {
                var radii = VerticalRingRadii(g, L);
                if (radii.Count == 0) return pts;
                int n = RingCount(radii.Max(), L.SV);
                foreach (var r in radii)
                    for (int k = 0; k < n; k++) pts.Add(Vec2.FromAngle(2 * Math.PI * k / n) * r);
            }
            return pts;
        }

        private static void AddUnique(List<Vec2> pts, Vec2 p)
        {
            foreach (var q in pts) if ((q - p).Length < 1e-6) return;
            pts.Add(p);
        }
    }
}
