using System;
using System.Collections.Generic;
using System.Linq;
using LynhCad.Manhole.Core.Geometry;

namespace LynhCad.Manhole.Core.Rebar
{
    public enum BarShape { Straight, L, U }

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

        public static bool IsStandard(int dia) { return MassPerMetre.ContainsKey(dia); }

        public static double KgPerMetre(int dia)
        {
            double m;
            if (MassPerMetre.TryGetValue(dia, out m)) return m;
            return 0.00785 * Math.PI * dia * dia / 4.0; // kích thước lạ: tính theo diện tích
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
        public double A, B, C;
        public int Count;

        public double Length
        {
            get
            {
                switch (Shape)
                {
                    case BarShape.L: return A + B;
                    case BarShape.U: return A + B + C;
                    default: return A;
                }
            }
        }

        /// <summary>Bản sao với A, B, C làm tròn lên 10 mm như thông lệ bảng thống kê (không sửa bản gốc).</summary>
        internal BarItem RoundedTo10()
        {
            var r = (BarItem)MemberwiseClone();
            r.A = GeomUtil.RoundUp(A, 10);
            r.B = GeomUtil.RoundUp(B, 10);
            r.C = GeomUtil.RoundUp(C, 10);
            return r;
        }

        internal string Signature
        {
            get { return string.Format("{0}|{1}|{2}|{3:0}|{4:0}|{5:0}", Group, Dia, Shape, A, B, C); }
        }
    }

    /// <summary>Một dòng trong bảng thống kê thép.</summary>
    public class BarMark
    {
        public string Mark;
        public int Dia;
        public BarShape Shape;
        public double A, B, C;
        public int Count;
        public double Length;
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
                        A = it.A, B = it.B, C = it.C, Length = it.Length,
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
