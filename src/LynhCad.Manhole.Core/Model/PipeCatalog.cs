using System;
using System.Collections.Generic;
using System.Linq;

namespace LynhCad.Manhole.Core.Model
{
    /// <summary>
    /// Danh mục ống cho hộp thoại: cỡ DN thường dùng, vật liệu, và đường kính ngoài gợi ý theo vật liệu + DN.
    /// OD chỉ là giá trị điển hình (theo catalogue phổ biến tại Úc) để điền sẵn — người dùng sửa được.
    /// Vật liệu không có bảng: OD ≈ 1.2·DN làm tròn 5 mm (cùng quy ước với PipeSpec.EffectiveOD).
    /// </summary>
    public static class PipeCatalog
    {
        public static readonly double[] Sizes =
            { 100, 150, 225, 300, 375, 450, 525, 600, 675, 750, 825, 900, 1050, 1200, 1350, 1500, 1650, 1800 };

        public static readonly string[] Materials = { "RCP", "PVC-U", "PE100", "DICL", "VC", "GRP", "FRC" };

        // OD (mm) theo vật liệu và DN. RCP không liệt kê: dùng 1.2·DN (vách ≈ DN/10).
        private static readonly Dictionary<string, Dictionary<double, double>> Od = new Dictionary<string, Dictionary<double, double>>(StringComparer.OrdinalIgnoreCase)
        {
            // AS/NZS 1260 (DWV / sewer)
            { "PVC-U", new Dictionary<double, double> { { 100, 110 }, { 150, 160 }, { 225, 250 }, { 300, 315 }, { 375, 400 } } },
            // AS/NZS 4130: DN danh định ≈ OD cỡ gần nhất
            { "PE100", new Dictionary<double, double> { { 100, 110 }, { 150, 160 }, { 225, 250 }, { 300, 315 }, { 375, 400 }, { 450, 500 }, { 525, 560 }, { 600, 630 }, { 675, 710 }, { 750, 800 }, { 900, 1000 } } },
            // AS/NZS 2280 (gang dẻo)
            { "DICL", new Dictionary<double, double> { { 100, 122 }, { 150, 177 }, { 225, 259 }, { 300, 345 }, { 375, 426 }, { 450, 507 }, { 525, 586 }, { 600, 667 }, { 750, 826 } } },
        };

        /// <summary>OD gợi ý cho vật liệu + DN (luôn có giá trị).</summary>
        public static double OutsideDiameter(string material, double dn)
        {
            Dictionary<double, double> table; double od;
            if (material != null && Od.TryGetValue(material.Trim(), out table) && table.TryGetValue(dn, out od)) return od;
            return Math.Round(dn * 1.2 / 5) * 5;
        }

        /// <summary>Các OD có thể chọn cho một DN: của mọi vật liệu trong bảng + giá trị mặc định, tăng dần, không trùng.</summary>
        public static double[] OutsideDiameters(double dn)
        {
            var list = new List<double> { Math.Round(dn * 1.2 / 5) * 5 };
            foreach (var t in Od.Values)
            {
                double od;
                if (t.TryGetValue(dn, out od)) list.Add(od);
            }
            return list.Where(v => v > dn).Distinct().OrderBy(v => v).ToArray();
        }
    }
}
