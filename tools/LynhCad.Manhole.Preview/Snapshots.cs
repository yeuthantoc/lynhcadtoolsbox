using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using LynhCad.Manhole.Core.Drawing;
using LynhCad.Manhole.Core.Export;
using LynhCad.Manhole.Core.Model;

namespace LynhCad.Manhole.Preview
{
    /// <summary>
    /// Kiểm tra snapshot: SVG của mọi file trong samples/*.json được so với bản đã commit trong samples/snapshots.
    /// Bắt được mọi thay đổi nét vẽ (mất hatch, mất kích thước, ...) mà các kiểm tra số lượng không thấy.
    /// Sửa bản vẽ có chủ đích → chạy "snapshot" để ghi lại rồi xem diff SVG trước khi commit.
    /// </summary>
    internal static class Snapshots
    {
        private static readonly Regex Number = new Regex(@"-?\d+(\.\d+)?", RegexOptions.Compiled);
        /// <summary>Sai số cho phép khi so số (SVG ghi 2 chữ số thập phân; libm Windows/Linux có thể lệch ở chữ số cuối).</summary>
        private const double Tolerance = 0.015;

        /// <summary>Tìm thư mục samples/ đi ngược lên từ thư mục hiện hành.</summary>
        public static string FindSamplesDir()
        {
            foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            {
                var dir = new DirectoryInfo(start);
                while (dir != null)
                {
                    string s = Path.Combine(dir.FullName, "samples");
                    if (File.Exists(Path.Combine(s, "rect-mh01.json"))) return s;
                    dir = dir.Parent;
                }
            }
            return null;
        }

        /// <summary>Tên file snapshot → nội dung SVG, cho mọi hố ga trong samples/*.json.</summary>
        public static SortedDictionary<string, string> Render(string samplesDir)
        {
            var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var file in Directory.GetFiles(samplesDir, "*.json").OrderBy(f => f, StringComparer.Ordinal))
            {
                string baseName = Path.GetFileNameWithoutExtension(file);
                var list = ParamsJson.LoadMany(file);
                for (int i = 0; i < list.Count; i++)
                {
                    var p = list[i];
                    var res = DetailComposer.Compose(p);
                    string name = baseName + (list.Count > 1 ? "-" + (i + 1) : "") + ".svg";
                    result[name] = res.Ok
                        ? SvgExporter.Export(res.Model, p.Scale)
                        : "ERROR\n" + string.Join("\n", res.Errors) + "\n";
                }
            }
            return result;
        }

        public static string SnapshotDir(string samplesDir) { return Path.Combine(samplesDir, "snapshots"); }

        public static int Write(string samplesDir)
        {
            string dir = SnapshotDir(samplesDir);
            Directory.CreateDirectory(dir);
            foreach (var old in Directory.GetFiles(dir, "*.svg")) File.Delete(old);
            foreach (var kv in Render(samplesDir))
            {
                File.WriteAllText(Path.Combine(dir, kv.Key), kv.Value, new UTF8Encoding(false));
                Console.WriteLine("wrote " + Path.Combine(dir, kv.Key));
            }
            return 0;
        }

        /// <summary>So hai SVG: phần chữ phải giống hệt, phần số được lệch ≤ Tolerance. Trả về null nếu khớp.</summary>
        public static string Diff(string expected, string actual)
        {
            var e = Normalize(expected).Split('\n');
            var a = Normalize(actual).Split('\n');
            for (int i = 0; i < Math.Max(e.Length, a.Length); i++)
            {
                string le = i < e.Length ? e[i] : "<end of file>";
                string la = i < a.Length ? a[i] : "<end of file>";
                if (!LineEquals(le, la))
                    return string.Format("line {0}:\n  expected: {1}\n  actual:   {2}", i + 1, Clip(le), Clip(la));
            }
            return null;
        }

        private static string Normalize(string s) { return s.Replace("\r\n", "\n").TrimEnd('\n'); }

        private static string Clip(string s) { return s.Length > 200 ? s.Substring(0, 200) + "..." : s; }

        private static bool LineEquals(string a, string b)
        {
            if (a == b) return true;
            if (Number.Replace(a, "#") != Number.Replace(b, "#")) return false;
            var na = Number.Matches(a);
            var nb = Number.Matches(b);
            if (na.Count != nb.Count) return false;
            for (int i = 0; i < na.Count; i++)
            {
                double x = double.Parse(na[i].Value, CultureInfo.InvariantCulture);
                double y = double.Parse(nb[i].Value, CultureInfo.InvariantCulture);
                if (Math.Abs(x - y) > Tolerance) return false;
            }
            return true;
        }
    }
}
