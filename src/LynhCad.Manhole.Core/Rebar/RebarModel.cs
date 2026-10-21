using System;
using System.Collections.Generic;
using System.Linq;
using LynhCad.Manhole.Core.Geometry;

namespace LynhCad.Manhole.Core.Rebar
{
    /// <summary>
    /// Hình dạng thanh. Ring = vòng tròn kín (A = đường kính tim, B = chiều dài nối chồng).
    /// Cranked = gãy một chỗ (A = đoạn thẳng thứ nhất, B = độ nhấn đứng, C = đoạn thẳng thứ hai; đoạn xiên dốc 1:CrankSlope).
    /// Dropped = nhấn xuống ở giữa, gãy hai chỗ (A = đoạn đầu, B = độ nhấn, C = đoạn giữa đã nhấn, D = đoạn cuối).
    /// </summary>
    public enum BarShape { Straight, L, U, Ring, Cranked, Dropped }

    /// <summary>
    /// Tên các bộ thép logic: khoá nối RebarDesigner (bảng thống kê) với các builder (ký hiệu trên bản vẽ).
    /// Luôn dùng hằng số ở đây, không gõ chuỗi tay.
    /// </summary>
    public static class BarSets
    {
        public const string WallVert = "WallVert";
        public const string WallHoriz = "WallHoriz";
        public const string PipeTrim = "PipeTrim";
        public const string RoofTrim = "RoofTrim";

        // Lưới bản đáy / bản nắp: mỗi lưới gồm 2 bộ, X (lớp ngoài cùng) và Y.
        public const string BaseBot = "BaseBot";
        public const string BaseTop = "BaseTop";
        public const string RoofBot = "RoofBot";
        public const string RoofTop = "RoofTop";

        public static string X(string mat) { return mat + "X"; }
        public static string Y(string mat) { return mat + "Y"; }
    }

    /// <summary>Bảng khối lượng thép thanh gân D500N theo AS/NZS 4671 (kg/m = 0.00785 × diện tích danh định).</summary>
    public static class RebarCatalog
    {
        private static readonly Dictionary<int, double> MassPerMetre = new Dictionary<int, double>
        {
            { 10, 0.617 }, { 12, 0.888 }, { 16, 1.58 }, { 20, 2.47 },
            { 24, 3.55 }, { 28, 4.83 }, { 32, 6.31 }, { 36, 7.99 },
        };

        public static readonly int[] Sizes = { 10, 12, 16, 20, 24, 28, 32, 36 };

        /// <summary>Bán kính tim thanh khi uốn với đường kính trong = bendFactor × d: (bendFactor + 1)·d / 2.</summary>
        public static double BendRadius(int dia, double bendFactor) { return (bendFactor + 1) * dia / 2.0; }

        /// <summary>
        /// Cog chuẩn AS 3600 13.1.2.7: uốn 90° (đường kính trong bendFactor·d) có tổng chiều dài bằng móc 180° chuẩn
        /// (nửa vòng + đoạn thẳng max(4d, 70)). Trả về chiều dài chân cog đo từ đỉnh giao hai tim (tim thanh), làm tròn lên 10.
        /// </summary>
        public static double StandardCog(int dia, double bendFactor)
        {
            double r = BendRadius(dia, bendFactor);
            double ext = Math.PI * r + Math.Max(4.0 * dia, 70) - Math.PI * r / 2; // đoạn thẳng của cog
            return GeomUtil.RoundUp(ext + r, 10);
        }

        public static bool IsStandard(int dia) { return MassPerMetre.ContainsKey(dia); }

        public static double KgPerMetre(int dia)
        {
            double m;
            if (MassPerMetre.TryGetValue(dia, out m)) return m;
            return 0.00785 * Math.PI * dia * dia / 4.0; // kích thước lạ: tính theo diện tích
        }
    }

    /// <summary>
    /// Vùng thép lớp trên bản nắp bị nhấn xuống dưới rãnh khung nắp: thanh nằm trong dải Perp được hạ Depth
    /// trong khoảng Along, chuyển cao độ bằng đoạn xiên dài Run (= Depth × RebarDesigner.CrankSlope) ở hai mép.
    /// </summary>
    public class DropZone
    {
        /// <summary>Along: khoảng dọc thanh bị hạ hết Depth (hai mép có đoạn xiên dài Run). Perp: dải thanh nằm trong rãnh (hạ hết).</summary>
        public Interval Along, Perp;
        public double Depth, Run;

        /// <summary>
        /// Độ nhấn của thanh ở vị trí vuông góc pos: hết Depth trong dải Perp, giảm dần về 0 trong dải đoạn xiên
        /// (Perp ± Run) để thanh nằm dưới đoạn xiên của lớp kia cũng hạ theo đúng độ cao chỗ đó, ngoài ra = 0.
        /// </summary>
        public double DepthAt(double pos)
        {
            if (Perp.Contains(pos)) return Depth;
            double dist = pos < Perp.Min ? Perp.Min - pos : pos - Perp.Max;
            return dist >= Run ? 0 : Depth * (1 - dist / Run);
        }

        public bool Applies(double pos) { return DepthAt(pos) > 1e-9; }

        /// <summary>Cao độ thanh ở vị trí pos (so với cao độ bình thường zHigh) tại toạ độ dọc thanh.</summary>
        public double ZAt(double pos, double along, double zHigh)
        {
            double depth = DepthAt(pos);
            if (depth <= 1e-9) return zHigh;
            double run = depth * RebarDesigner.CrankSlope;
            if (along <= Along.Min - run || along >= Along.Max + run) return zHigh;
            if (along >= Along.Min && along <= Along.Max) return zHigh - depth;
            double t = along < Along.Min ? (Along.Min - along) / run : (along - Along.Max) / run;
            return zHigh - depth * (1 - t);
        }

        /// <summary>
        /// Trắc dọc của một đoạn thanh ở vị trí pos: các đỉnh (along, z) gồm hai đầu và các điểm gãy nằm trong đoạn.
        /// Thanh thẳng (không chạm vùng nhấn) → 2 đỉnh. Đoạn xiên luôn dốc 1:CrankSlope.
        /// </summary>
        public List<Vec2> Profile(double pos, Interval piece, double zHigh)
        {
            double depth = DepthAt(pos);
            if (depth <= 1e-9) return Flat(piece, zHigh);
            double run = depth * RebarDesigner.CrankSlope;
            var xs = new List<double> { piece.Min };
            foreach (var x in new[] { Along.Min - run, Along.Min, Along.Max, Along.Max + run })
                if (x > piece.Min + 1e-6 && x < piece.Max - 1e-6) xs.Add(x);
            xs.Add(piece.Max);
            var pts = new List<Vec2>();
            foreach (var x in xs)
            {
                var v = new Vec2(x, ZAt(pos, x, zHigh));
                // bỏ đỉnh thừa nằm trên cùng một đường thẳng với hai đỉnh trước
                if (pts.Count >= 2)
                {
                    var a = pts[pts.Count - 2]; var b = pts[pts.Count - 1];
                    if (Math.Abs((b - a).Cross(v - a)) < 1e-6 * Math.Max(1, (v - a).Length)) { pts[pts.Count - 1] = v; continue; }
                }
                pts.Add(v);
            }
            return pts;
        }

        /// <summary>Trắc dọc thẳng (không có vùng nhấn).</summary>
        public static List<Vec2> Flat(Interval piece, double z)
        {
            return new List<Vec2> { new Vec2(piece.Min, z), new Vec2(piece.Max, z) };
        }

        /// <summary>
        /// Lập BarItem từ trắc dọc: 0 đoạn xiên → thẳng; 1 → Cranked (A = đoạn dài hơn); 2 → Dropped (A = đoạn đầu dài hơn D).
        /// Trắc dọc lấy từ Profile nên đoạn xiên luôn dốc 1:CrankSlope và chiều dài của nó tính theo B.
        /// </summary>
        public static BarItem Item(List<Vec2> profile, string setKey, char group, int dia)
        {
            // đoạn thẳng theo thứ tự dọc thanh, đoạn xiên nằm giữa chúng; đoạn xiên ở đầu / cuối thanh → đoạn thẳng dài 0
            var legs = new List<double>();
            double drop = 0;
            int ramps = 0;
            bool prevRamp = true;
            for (int i = 0; i + 1 < profile.Count; i++)
            {
                var a = profile[i]; var b = profile[i + 1];
                bool ramp = Math.Abs(b.Y - a.Y) >= 1e-9;
                if (ramp)
                {
                    ramps++;
                    drop = Math.Max(drop, Math.Abs(b.Y - a.Y));
                    if (prevRamp) legs.Add(0);
                }
                else legs.Add(Math.Abs(b.X - a.X));
                prevRamp = ramp;
            }
            if (prevRamp) legs.Add(0);

            var it = new BarItem { SetKey = setKey, Group = group, Dia = dia, Count = 1 };
            if (ramps == 0)
            {
                it.Shape = BarShape.Straight; it.A = legs.Sum();
            }
            else if (ramps == 1)
            {
                it.Shape = BarShape.Cranked; it.B = drop;
                it.A = Math.Max(legs[0], legs[1]); it.C = Math.Min(legs[0], legs[1]);
            }
            else if (ramps == 2)
            {
                it.Shape = BarShape.Dropped; it.B = drop;
                it.C = legs[1];
                it.A = Math.Max(legs[0], legs[2]); it.D = Math.Min(legs[0], legs[2]);
            }
            else throw new InvalidOperationException("Trắc dọc thanh có quá nhiều đoạn xiên (" + ramps + ").");
            return it;
        }
    }

    /// <summary>Một nhóm thanh giống hệt nhau trước khi đánh số hiệu.</summary>
    public class BarItem
    {
        /// <summary>Khoá bộ thép logic (vd "WallVert") để vẽ ký hiệu trên bản vẽ.</summary>
        public string SetKey;
        /// <summary>Tiền tố số hiệu: W tường, B đáy, R nắp, T gia cường.</summary>
        public char Group;
        public int Dia;
        public BarShape Shape;
        /// <summary>Kích thước hình học đo theo tim thanh (đỉnh giao hai tim tới đỉnh / đầu thanh).</summary>
        public double A, B, C, D;
        public int Count;
        /// <summary>Chân cog 90° ở hai đầu thanh (0 = không có), đo từ đỉnh giao tim tới đầu thanh.</summary>
        public double CogStart, CogEnd;
        /// <summary>Đường kính trong chỗ uốn (0 = uốn gãy, không trừ chiều dài).</summary>
        public double BendDia;

        public int CogCount { get { return (CogStart > 0 ? 1 : 0) + (CogEnd > 0 ? 1 : 0); } }

        /// <summary>Chiều dài đoạn xiên của thanh gãy có độ nhấn B (dốc 1:CrankSlope).</summary>
        public static double RampLength(double drop) { return drop * Math.Sqrt(1 + RebarDesigner.CrankSlope * RebarDesigner.CrankSlope); }

        /// <summary>Góc ngoặt tại chỗ gãy của thanh nhấn (dốc 1:CrankSlope).</summary>
        public static readonly double CrankAngle = Math.Atan(1.0 / RebarDesigner.CrankSlope);

        /// <summary>Tổng chiều dài theo đỉnh (chưa trừ chỗ uốn), gồm cả cog.</summary>
        public double VertexLength
        {
            get
            {
                double l;
                switch (Shape)
                {
                    case BarShape.L: l = A + B; break;
                    case BarShape.U: l = A + B + C; break;
                    case BarShape.Ring: l = Math.PI * A + B; break;
                    case BarShape.Cranked: l = A + C + RampLength(B); break;
                    case BarShape.Dropped: l = A + C + D + 2 * RampLength(B); break;
                    default: l = A; break;
                }
                return l + CogStart + CogEnd;
            }
        }

        /// <summary>Chiều dài cắt thanh: chiều dài theo đỉnh trừ phần dư tại mỗi chỗ uốn (cung bán kính tim thay cho góc gãy).</summary>
        public double Length
        {
            get
            {
                double r = (BendDia + Dia) / 2.0;
                double ded = 0;
                if (BendDia > 0)
                {
                    int right = CogCount;
                    double crank = 0;
                    switch (Shape)
                    {
                        case BarShape.L: right += 1; break;
                        case BarShape.U: right += 2; break;
                        case BarShape.Cranked: crank = 2; break;
                        case BarShape.Dropped: crank = 4; break;
                    }
                    ded = right * GeomUtil.BendDeduction(Math.PI / 2, r) + crank * GeomUtil.BendDeduction(CrankAngle, r);
                }
                return VertexLength - ded;
            }
        }

        /// <summary>Bản sao với A, B, C, D, cog làm tròn lên 10 mm như thông lệ bảng thống kê (không sửa bản gốc).</summary>
        internal BarItem RoundedTo10()
        {
            var r = (BarItem)MemberwiseClone();
            r.A = GeomUtil.RoundUp(A, 10);
            r.B = GeomUtil.RoundUp(B, 10);
            r.C = GeomUtil.RoundUp(C, 10);
            r.D = GeomUtil.RoundUp(D, 10);
            r.CogStart = GeomUtil.RoundUp(CogStart, 10);
            r.CogEnd = GeomUtil.RoundUp(CogEnd, 10);
            return r;
        }

        internal string Signature
        {
            get { return string.Format("{0}|{1}|{2}|{3:0}|{4:0}|{5:0}|{6:0}|{7:0}|{8:0}|{9:0}", Group, Dia, Shape, A, B, C, D, CogStart, CogEnd, BendDia); }
        }
    }

    /// <summary>Một dòng trong bảng thống kê thép.</summary>
    public class BarMark
    {
        public string Mark;
        public int Dia;
        public BarShape Shape;
        public double A, B, C, D;
        public double CogStart, CogEnd, BendDia;
        public int Count;
        public double Length;
        public int CogCount { get { return (CogStart > 0 ? 1 : 0) + (CogEnd > 0 ? 1 : 0); } }
        public double TotalLength { get { return Count * Length; } }
        public double Mass { get { return Count * Length / 1000.0 * RebarCatalog.KgPerMetre(Dia); } }
        public string Size { get { return "N" + Dia; } }
    }

    /// <summary>Một bộ thép logic trên bản vẽ (vd thép đứng tường) và các số hiệu thuộc nó.</summary>
    public class BarSetInfo
    {
        public string Key;
        public int Dia;
        public double Spacing;
        /// <summary>Hậu tố mô tả, vd "EF VERT", "BOT", "TOP".</summary>
        public string Suffix;
        public readonly List<string> Marks = new List<string>();
        /// <summary>Số thanh cố định (thanh gia cường) thay cho khoảng cách.</summary>
        public int FixedCount;

        /// <summary>Ký hiệu trên bản vẽ, vd "W1 N12-200 EF VERT" hoặc "T1 2-N16 TRIMMERS".</summary>
        public string Callout { get { return CalloutWith(" " + Suffix); } }

        /// <summary>Như Callout nhưng thay hậu tố (đã gồm ký tự ngăn cách đầu, vd " TOP" hoặc "\nEACH SIDE ...").</summary>
        public string CalloutWith(string suffix)
        {
            string marks = string.Join("/", Marks);
            string bar = FixedCount > 0
                ? string.Format("{0}-N{1}", FixedCount, Dia)
                : string.Format("N{0}-{1:0}", Dia, Spacing);
            return (marks.Length > 0 ? marks + " " : "") + bar + (string.IsNullOrWhiteSpace(suffix) ? "" : suffix);
        }
    }

    public class RebarSchedule
    {
        public readonly List<BarMark> Marks = new List<BarMark>();
        public readonly Dictionary<string, BarSetInfo> Sets = new Dictionary<string, BarSetInfo>();

        public double TotalMass { get { return Marks.Sum(m => m.Mass); } }

        public BarSetInfo Set(string key)
        {
            BarSetInfo s;
            return Sets.TryGetValue(key, out s) ? s : null;
        }

        /// <summary>Ký hiệu của bộ thép. Khoá không tồn tại là lỗi lập trình → báo ngay thay vì để ghi chú biến mất.</summary>
        public string Callout(string key)
        {
            var s = Set(key);
            if (s == null) throw new KeyNotFoundException("Bảng thống kê không có bộ thép '" + key + "'.");
            return s.Callout;
        }

        /// <summary>
        /// Ký hiệu một lưới 2 phương, vd "B1/B2 N16-200 BOT EW". Null nếu lưới không có trong bảng (vd không có lưới trên).
        /// </summary>
        public string MatCallout(string mat, bool top)
        {
            var sx = Set(BarSets.X(mat));
            var sy = Set(BarSets.Y(mat));
            if (sx == null || sy == null) return null;
            return string.Format("{0} N{1}-{2:0} {3} EW", string.Join("/", sx.Marks.Concat(sy.Marks).Distinct()), sx.Dia, sx.Spacing, top ? "TOP" : "BOT");
        }

        /// <summary>Gộp các BarItem giống nhau và đánh số hiệu W1, W2, B1...</summary>
        public static RebarSchedule Build(IList<BarItem> items, IEnumerable<BarSetInfo> sets)
        {
            var sch = new RebarSchedule();
            foreach (var s in sets) sch.Sets[s.Key] = s;

            var groupOrder = "WBRT";
            var counters = new Dictionary<char, int>();
            var bySig = new Dictionary<string, BarMark>();

            var ordered = items
                .Where(i => i.Count > 0 && i.Length > 0)
                .Select((it, idx) => new { it, idx })
                .OrderBy(x => groupOrder.IndexOf(x.it.Group) < 0 ? 99 : groupOrder.IndexOf(x.it.Group))
                .ThenBy(x => x.idx)
                .Select(x => x.it.RoundedTo10());

            foreach (var it in ordered)
            {
                BarMark m;
                if (!bySig.TryGetValue(it.Signature, out m))
                {
                    int n;
                    counters.TryGetValue(it.Group, out n);
                    counters[it.Group] = ++n;
                    m = new BarMark
                    {
                        Mark = it.Group.ToString() + n, Dia = it.Dia, Shape = it.Shape,
                        A = it.A, B = it.B, C = it.C, D = it.D, Length = it.Length,
                        CogStart = it.CogStart, CogEnd = it.CogEnd, BendDia = it.BendDia,
                    };
                    bySig[it.Signature] = m;
                    sch.Marks.Add(m);
                }
                m.Count += it.Count;

                BarSetInfo set;
                if (it.SetKey != null && sch.Sets.TryGetValue(it.SetKey, out set) && !set.Marks.Contains(m.Mark))
                    set.Marks.Add(m.Mark);
            }
            // số hiệu trong ký hiệu theo thứ tự tự nhiên: R1/R2/R3
            foreach (var s in sch.Sets.Values)
                s.Marks.Sort((a, b) =>
                {
                    int c = a[0].CompareTo(b[0]);
                    return c != 0 ? c : int.Parse(a.Substring(1)).CompareTo(int.Parse(b.Substring(1)));
                });
            return sch;
        }
    }
}
