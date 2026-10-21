using System;
using System.Collections.Generic;
using System.Linq;
using LynhCad.Manhole.Core.Geometry;
using LynhCad.Manhole.Core.Model;

namespace LynhCad.Manhole.Core.Rebar
{
    /// <summary>Một lớp thép trong bản (đáy hoặc nắp), chạy theo X hoặc Y.</summary>
    /// <summary>
    /// Khoá nổi bật (Prim.Key) của các primitive thép trong preview hộp thoại = tên thuộc tính ManholeParams của ô nhập
    /// tương ứng (trimmer dùng chung một khoá cho cả đường kính và số thanh).
    /// </summary>
    public static class RebarKeys
    {
        public const string WallVertical = "WallVertical";
        public const string WallHorizontal = "WallHorizontal";
        public const string Trimmers = "Trimmers";

        /// <summary>Khoá của một lưới theo tiền tố bộ thép (BarSets.BaseBot...).</summary>
        public static string ForMat(string matPrefix)
        {
            switch (matPrefix)
            {
                case BarSets.BaseBot: return "BaseBottom";
                case BarSets.BaseTop: return "BaseTop";
                case BarSets.RoofBot: return "RoofBottom";
                case BarSets.RoofTop: return "RoofTop";
                default: return null;
            }
        }
    }

    public class MatLayer
    {
        public string Key;
        /// <summary>Tiền tố bộ thép của lưới (BarSets.BaseBot...), chung cho hai lớp X / Y.</summary>
        public string Mat;
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
        /// <summary>Vùng nhấn xuống dưới rãnh khung nắp (chỉ lưới trên bản nắp khi có rãnh). Null nếu không.</summary>
        public DropZone Drop;
        /// <summary>Cog chuẩn ở hai đầu thanh (0 = không có), hướng (+1 lên / −1 xuống) và cao độ tim lớp đối diện mà cog không được vượt.</summary>
        public double CogStd, ZCogLimit;
        public int CogDir;
        /// <summary>Bán kính tim thanh khi uốn (vẽ cung tại cog, chỗ gãy).</summary>
        public double BendRadius;

        /// <summary>Chiều dài chân cog tại đầu thanh ở cao độ z: cog chuẩn nhưng không vượt quá lớp đối diện.</summary>
        public double CogAt(double z)
        {
            if (CogStd <= 0) return 0;
            return Math.Max(0, Math.Min(CogStd, Math.Abs(ZCogLimit - z)));
        }

        /// <summary>Trắc dọc để vẽ: Profile + chân cog đứng ở hai đầu (nếu có).</summary>
        public List<Vec2> DrawProfile(double pos, Interval piece)
        {
            var pr = Profile(pos, piece);
            if (CogStd <= 0) return pr;
            var first = pr[0]; var last = pr[pr.Count - 1];
            pr.Insert(0, new Vec2(first.X, first.Y + CogDir * CogAt(first.Y)));
            pr.Add(new Vec2(last.X, last.Y + CogDir * CogAt(last.Y)));
            return pr;
        }

        /// <summary>Các đoạn thực tế của thanh tại vị trí pos (thanh bị cắt bởi lỗ thăm thì tách đôi).</summary>
        public List<Interval> Pieces(double pos)
        {
            if (CutAlong == null || CutPerp == null || !CutPerp.Value.Contains(pos))
                return new List<Interval> { Run };
            return Run.Subtract(new[] { CutAlong.Value }).Where(i => i.Length >= RebarDesigner.MinPiece).ToList();
        }

        /// <summary>Trắc dọc (along, z) của một đoạn thanh tại vị trí pos: thẳng ở Z, hoặc nhấn xuống trong vùng rãnh.</summary>
        public List<Vec2> Profile(double pos, Interval piece)
        {
            return Drop != null && Drop.Applies(pos) ? Drop.Profile(pos, piece, Z) : DropZone.Flat(piece, Z);
        }

        /// <summary>Cao độ tim thanh tại vị trí pos, toạ độ dọc thanh along.</summary>
        public double ZAt(double pos, double along)
        {
            return Drop != null && Drop.Applies(pos) ? Drop.ZAt(pos, along, Z) : Z;
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
        /// <summary>Vùng nhấn xuống (thanh gia cường lớp trên nằm trong rãnh khung nắp). Null nếu không.</summary>
        public DropZone Drop;

        public List<Vec2> Profile()
        {
            return Drop != null && Drop.Applies(Pos) ? Drop.Profile(Pos, Run, Z) : DropZone.Flat(Run, Z);
        }

        public double ZAt(double along)
        {
            return Drop != null && Drop.Applies(Pos) ? Drop.ZAt(Pos, along, Z) : Z;
        }
    }

    /// <summary>Các giá trị bố trí thép dùng chung cho bảng thống kê và bản vẽ (để hai bên luôn khớp nhau).</summary>
    public class RebarLayout
    {
        public double Cover;
        // Tường (chỉ hố chữ nhật)
        public int DV, DH;
        public double SV, SH;
        /// <summary>Khoảng từ mặt tường tới tim thép ngang / thép đứng khi đặt 2 lớp (lớp nào ngoài tuỳ HorizInside).</summary>
        public double HorizOffset, VertOffset;
        /// <summary>Thép ngang nằm phía trong thép đứng (true) hay là lớp ngoài sát mặt bê tông (false).</summary>
        public bool HorizInside;
        /// <summary>Số lớp thép tường: 2 = hai mặt (EF), 1 = một lớp giữa tường (CENTRAL).</summary>
        public int WallLayers = 2;
        /// <summary>Khoảng từ MẶT TRONG tường tới tim từng lớp thép đứng / thép ngang (mặt trong trước, mặt ngoài sau).</summary>
        public readonly List<double> VertFromInner = new List<double>(), HorizFromInner = new List<double>();
        public List<double> HorizLevels = new List<double>();
        public double VertZBot, VertZTop, VertLeg;
        /// <summary>Cog ở đầu trên thép đứng móc vào bản nắp (0 = không), bán kính tim uốn thép đứng / thép ngang.</summary>
        public double VertCog, VertBendRadius, HorizBendRadius;
        /// <summary>Thanh U thép ngang thứ hai của mỗi vòng đặt lệch về tim tường (= d + 10, hai thanh cách nhau 10 mm) để thấy rõ nối chồng; cùng mác.</summary>
        public double HorizLapOffset;
        /// <summary>Cao độ chân / đỉnh của thép đứng từng mặt (cùng thứ tự VertFromInner): mặt ngoài lùi WallBarInset ở hai đầu để chân L / cog không trùng mặt trong.</summary>
        public readonly List<double> VertZBotFaces = new List<double>(), VertZTopFaces = new List<double>();
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
        /// <summary>Độ dốc đoạn xiên của thanh nhấn xuống dưới rãnh khung nắp: chiều ngang = CrankSlope × độ nhấn (1:2).</summary>
        public const double CrankSlope = 2;
        /// <summary>Lưới trên có cog lùi đầu thanh vào so với lưới dưới (mm) để cog lớp trên (móc xuống) lồng trong cog lớp dưới (móc lên), không trùng chỗ.</summary>
        public const double TopMatInset = 10;
        /// <summary>Thép đứng mặt ngoài lùi vào (mm) ở chân L và cog so với mặt trong (hai chân / hai cog cùng cao độ sẽ đè lên nhau).</summary>
        public const double WallBarInset = 10;
        /// <summary>Khe hở giữa thanh vuông góc và tim cog của lớp kia (mm, cộng thêm 1,5d) để chấm thép nằm trong vùng cog.</summary>
        public const double CogClear = 10;

        public static RebarDesign Design(ManholeGeometry g)
        {
            var P = g.P;
            var L = new RebarLayout { Cover = P.Cover, TrimmerDia = P.TrimmerDiameter };
            var items = new List<BarItem>();
            var sets = new List<BarSetInfo>();
            double c = P.Cover;

            // ---------------- Bản đáy ----------------
            AddMat(g, L.BaseLayers, items, sets, BarSets.BaseBot, P.BaseBottom, false, 'B', c,
                   g.BaseHalfX, g.BaseHalfY, -g.Tb, g.Tb, null, null, null, null);
            if (P.BaseTopMat)
                AddMat(g, L.BaseLayers, items, sets, BarSets.BaseTop, P.BaseTop, true, 'B', c,
                       g.BaseHalfX, g.BaseHalfY, 0, g.Tb, null, null, null, null);

            // ---------------- Bản nắp ----------------
            var o = g.OpeningCenter;
            var cutX = new Interval(o.X - g.OpeningHalfX - c, o.X + g.OpeningHalfX + c);
            var cutY = new Interval(o.Y - g.OpeningHalfY - c, o.Y + g.OpeningHalfY + c);
            AddMat(g, L.RoofLayers, items, sets, BarSets.RoofBot, P.RoofBottom, false, 'R', c,
                   g.RoofHalfX, g.RoofHalfY, g.H, g.Ts, cutX, cutY, null, null);
            if (P.RoofTopMat)
            {
                // có rãnh khung nắp: lưới trên nhấn xuống RebateDepth trong phạm vi rãnh (hố tròn: hình vuông bao rãnh)
                DropZone dropX = null, dropY = null;
                if (g.HasRebate)
                {
                    var rx = new Interval(o.X - g.RebateHalfX, o.X + g.RebateHalfX);
                    var ry = new Interval(o.Y - g.RebateHalfY, o.Y + g.RebateHalfY);
                    double run = g.RebateDepth * CrankSlope;
                    // thanh trong rãnh hạ hết; thanh nằm dưới đoạn xiên của lớp kia (Perp ± run) hạ dần theo (DropZone.DepthAt)
                    dropX = new DropZone { Along = rx, Perp = ry, Depth = g.RebateDepth, Run = run };
                    dropY = new DropZone { Along = ry, Perp = rx, Depth = g.RebateDepth, Run = run };
                }
                AddMat(g, L.RoofLayers, items, sets, BarSets.RoofTop, P.RoofTop, true, 'R', c,
                       g.RoofHalfX, g.RoofHalfY, g.H + g.Ts, g.Ts, cutX, cutY, dropX, dropY);
            }

            AddRoofTrimmers(g, L, items, sets);

            // ---------------- Tường đổ tại chỗ (chữ nhật hoặc tròn) ----------------
            if (g.HasCastWalls) AddWalls(g, L, items, sets);

            return new RebarDesign { Layout = L, Schedule = RebarSchedule.Build(items, sets) };
        }

        /// <summary>Thêm một lưới 2 lớp (X ở ngoài cùng, Y ở trong).</summary>
        private static void AddMat(ManholeGeometry g, List<MatLayer> layers, List<BarItem> items, List<BarSetInfo> sets,
            string keyPrefix, BarSpec spec, bool isTop, char group, double c,
            double hx, double hy, double zFace, double thickness, Interval? cutX, Interval? cutY, DropZone dropX, DropZone dropY)
        {
            // thickness = chiều dày bản (giới hạn chân cog: lớp dưới móc lên tới lớp ngoài cùng của lưới trên và ngược lại)
            var P = g.P;
            int d = spec.Diameter;
            double s = spec.Spacing;
            string suffix = isTop ? "TOP" : "BOT";
            double zOpp = isTop ? zFace - thickness + c + d / 2.0 : zFace + thickness - c - d / 2.0;
            double bendDia = P.BendFactor * d;
            // lưới trên có cog: đầu thanh (mép bản và mép lỗ thăm) lùi vào TopMatInset để cog không trùng cog lưới dưới
            double inset = isTop && P.Cogs ? TopMatInset : 0;
            // Lớp 1 (ngoài cùng) chạy theo X, lớp 2 chạy theo Y
            double z1 = isTop ? zFace - c - d / 2.0 : zFace + c + d / 2.0;
            double z2 = isTop ? z1 - d : z1 + d;

            for (int k = 0; k < 2; k++)
            {
                bool alongX = k == 0;
                var layer = new MatLayer
                {
                    Key = alongX ? BarSets.X(keyPrefix) : BarSets.Y(keyPrefix), Mat = keyPrefix,
                    Dia = d, Spacing = s, AlongX = alongX, IsTop = isTop, Z = alongX ? z1 : z2,
                };
                double hRun = alongX ? hx : hy, hPerp = alongX ? hy : hx;
                layer.Run = new Interval(-hRun + c + inset, hRun - c - inset);
                layer.Positions = GeomUtil.Distribute(-hPerp + c + d / 2.0, hPerp - c - d / 2.0, s);
                if (cutX != null && cutY != null)
                {
                    var ca = alongX ? cutX.Value : cutY.Value;
                    layer.CutAlong = new Interval(ca.Min - inset, ca.Max + inset);
                    layer.CutPerp = alongX ? cutY : cutX;
                }
                if (P.Cogs)
                {
                    // thanh nằm sát đường cog của lớp kia (mép bản, mép lỗ thăm) dịch vào trong vùng cog: cách tim cog ≥ 1,5d + CogClear
                    double clear = 1.5 * d + CogClear;
                    var lines = new List<KeyValuePair<double, int>>
                    {
                        new KeyValuePair<double, int>(-hPerp + c + inset, +1),
                        new KeyValuePair<double, int>(hPerp - c - inset, -1),
                    };
                    if (layer.CutPerp != null)
                    {
                        var cp = layer.CutPerp.Value;
                        lines.Add(new KeyValuePair<double, int>(cp.Min - inset, -1));
                        lines.Add(new KeyValuePair<double, int>(cp.Max + inset, +1));
                    }
                    for (int i = 0; i < layer.Positions.Count; i++)
                        foreach (var ln in lines)
                            if (Math.Abs(layer.Positions[i] - ln.Key) < clear - 1e-9)
                                layer.Positions[i] = ln.Key + ln.Value * clear;
                    layer.Positions = layer.Positions.Distinct().OrderBy(v => v).ToList();
                }
                layer.Drop = alongX ? dropX : dropY;
                layer.BendRadius = RebarCatalog.BendRadius(d, P.BendFactor);
                if (P.Cogs)
                {
                    layer.CogStd = RebarCatalog.StandardCog(d, P.BendFactor);
                    layer.CogDir = isTop ? -1 : 1;
                    layer.ZCogLimit = zOpp;
                }
                layers.Add(layer);

                sets.Add(new BarSetInfo
                {
                    Key = layer.Key, Dia = d, Spacing = s,
                    Suffix = suffix + (alongX ? " (E-W)" : " (N-S)"),
                });
                foreach (var pos in layer.Positions)
                    foreach (var piece in layer.Pieces(pos))
                    {
                        var pr = layer.Profile(pos, piece);
                        var it = DropZone.Item(pr, layer.Key, group, d);
                        it.BendDia = bendDia;
                        it.CogStart = layer.CogAt(pr[0].Y);
                        it.CogEnd = layer.CogAt(pr[pr.Count - 1].Y);
                        items.Add(it);
                    }
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
                        // thanh gia cường lớp trên nằm trong dải rãnh khung nắp cũng nhấn xuống theo lưới
                        var t = new TrimmerBar { Key = key, AlongX = ax, Pos = pos, Run = run, Z = layer.Z, Dia = dt, Drop = layer.Drop };
                        L.RoofTrimmers.Add(t);
                        var ti = DropZone.Item(t.Profile(), key, 'T', dt);
                        ti.BendDia = P.BendFactor * dt;
                        items.Add(ti);
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
            L.HorizInside = P.WallHorizontalInside;
            if (L.HorizInside)
            {
                // thép đứng là lớp ngoài sát mặt bê tông, thép ngang buộc phía trong
                L.VertOffset = c + L.DV / 2.0;
                L.HorizOffset = c + L.DV + L.DH / 2.0;
            }
            else
            {
                L.HorizOffset = c + L.DH / 2.0;
                L.VertOffset = c + L.DH + L.DV / 2.0;
            }
            L.Lap = g.LapLength(L.DH);
            L.WallLayers = P.WallSingleLayer ? 1 : 2;
            L.VertFromInner.Clear(); L.HorizFromInner.Clear();
            if (L.WallLayers == 1)
            {
                // một lớp giữa tường: thép đứng đúng tim tường, thép ngang buộc phía trong (về lòng hố) hoặc phía ngoài thép đứng
                L.VertFromInner.Add(g.Tw / 2);
                L.HorizFromInner.Add(g.Tw / 2 + (L.HorizInside ? -1 : 1) * (L.DV + L.DH) / 2.0);
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
            L.VertCog = P.Cogs ? RebarCatalog.StandardCog(L.DV, P.BendFactor) : 0;
            L.VertBendRadius = RebarCatalog.BendRadius(L.DV, P.BendFactor);
            L.HorizBendRadius = RebarCatalog.BendRadius(L.DH, P.BendFactor);
            L.HorizLapOffset = L.DH + CogClear;
            // mặt ngoài (lớp thứ 2) lùi WallBarInset ở chân và ở cog để không đè lên chân / cog mặt trong
            L.VertZBotFaces.Clear(); L.VertZTopFaces.Clear();
            for (int k = 0; k < L.VertFromInner.Count; k++)
            {
                double ins = k == 1 ? WallBarInset : 0;
                L.VertZBotFaces.Add(L.VertZBot + ins);
                L.VertZTopFaces.Add(L.VertZTop - (L.VertCog > 0 ? ins : 0));
            }

            var counts = VerticalCountsPerFace(g, L);
            sets.Add(new BarSetInfo { Key = BarSets.WallVert, Dia = L.DV, Spacing = L.SV, Suffix = faces + " VERT" });
            for (int k = 0; k < counts.Count; k++)
            {
                double vertA = L.VertZTopFaces[k] - L.VertZBotFaces[k];
                // chân L vào bản đáy + (nếu có cog) móc vào bản nắp → thanh chữ U (A = chân, B = đoạn đứng, C = cog)
                if (L.VertCog > 0)
                    items.Add(new BarItem { SetKey = BarSets.WallVert, Group = 'W', Dia = L.DV, Shape = BarShape.U, A = L.VertLeg, B = vertA, C = L.VertCog, Count = counts[k], BendDia = P.BendFactor * L.DV });
                else
                    items.Add(new BarItem { SetKey = BarSets.WallVert, Group = 'W', Dia = L.DV, Shape = BarShape.L, A = vertA, B = L.VertLeg, Count = counts[k], BendDia = P.BendFactor * L.DV });
            }

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
                    items.Add(new BarItem { SetKey = BarSets.WallHoriz, Group = 'W', Dia = L.DH, Shape = BarShape.U, A = leg, B = 2 * bx, C = leg, Count = 2 * nLevels, BendDia = P.BendFactor * L.DH });
                }
            }
            else
            {
                // mỗi cao độ, mỗi mặt = 1 vòng kín, nối chồng một chỗ
                foreach (var r in HorizontalRingRadii(g, L))
                    items.Add(new BarItem { SetKey = BarSets.WallHoriz, Group = 'W', Dia = L.DH, Shape = BarShape.Ring, A = 2 * r, B = L.Lap, Count = nLevels, BendDia = P.BendFactor * L.DH });
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
                    items.Add(new BarItem { SetKey = BarSets.PipeTrim, Group = 'T', Dia = dt, Shape = BarShape.Straight, A = vLen, Count = L.WallLayers * 2 * n, BendDia = P.BendFactor * dt });
                    items.Add(new BarItem { SetKey = BarSets.PipeTrim, Group = 'T', Dia = dt, Shape = BarShape.Straight, A = hLen, Count = L.WallLayers * 2 * n, BendDia = P.BendFactor * dt });
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

        /// <summary>Số thanh đứng của từng mặt (mặt trong trước), cùng cách rải như VerticalPositions.</summary>
        public static List<int> VerticalCountsPerFace(ManholeGeometry g, RebarLayout L)
        {
            var counts = new List<int>();
            var pts = VerticalPositions(g, L);
            if (g.IsRect)
            {
                var loops = VerticalLoops(g, L);
                if (loops.Count == 0) return counts;
                int inner = pts.Count(q => Math.Abs(q.X) <= loops[0].X + 1e-6 && Math.Abs(q.Y) <= loops[0].Y + 1e-6);
                counts.Add(inner);
                if (loops.Count > 1) counts.Add(pts.Count - inner);
            }
            else
            {
                var radii = VerticalRingRadii(g, L);
                if (radii.Count == 0) return counts;
                int n = pts.Count / radii.Count;
                foreach (var r in radii) counts.Add(n);
            }
            return counts;
        }

        private static void AddUnique(List<Vec2> pts, Vec2 p)
        {
            foreach (var q in pts) if ((q - p).Length < 1e-6) return;
            pts.Add(p);
        }
    }
}
