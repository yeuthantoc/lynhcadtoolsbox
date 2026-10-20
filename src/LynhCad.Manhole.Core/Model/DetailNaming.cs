using System;
using System.Collections.Generic;

namespace LynhCad.Manhole.Core.Model
{
    /// <summary>
    /// Quy tắc đặt tên Group "LCT_&lt;id&gt;" và chia nhỏ chuỗi lưu trong Xrecord cho chi tiết đã vẽ.
    /// Đặt ở lõi (không phụ thuộc AutoCAD) để kiểm tra được bằng lệnh "check".
    /// </summary>
    public static class DetailNaming
    {
        /// <summary>Chuỗi trong Xrecord có giới hạn độ dài → JSON được chia thành các đoạn tối đa chừng này ký tự.</summary>
        public const int XrecordChunkSize = 250;

        /// <summary>Tên group gốc của một hố ga: "LCT_" + mã hố đã thay ký tự lạ bằng "_".</summary>
        public static string GroupBaseName(string id)
        {
            return "LCT_" + Sanitize(string.IsNullOrEmpty(id) ? "MH" : id);
        }

        public static string GroupDescription(string id) { return "LynhCadToolsBox manhole detail " + id; }

        /// <summary>Giữ chữ, số, '-' và '_'; ký tự khác thành '_'.</summary>
        public static string Sanitize(string s)
        {
            var chars = s.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
                if (!char.IsLetterOrDigit(chars[i]) && chars[i] != '-' && chars[i] != '_') chars[i] = '_';
            return new string(chars);
        }

        /// <summary>baseName nếu chưa dùng, nếu không thì baseName_2, baseName_3, ...</summary>
        public static string UniqueName(string baseName, Func<string, bool> taken)
        {
            string name = baseName;
            int n = 1;
            while (taken(name)) name = baseName + "_" + (++n);
            return name;
        }

        /// <summary>Tên group đã thuộc mã hố này chưa (đúng baseName hoặc baseName_n) → không cần đổi tên.</summary>
        public static bool BelongsTo(string groupName, string baseName)
        {
            return groupName == baseName || groupName.StartsWith(baseName + "_", StringComparison.Ordinal);
        }

        /// <summary>Chia chuỗi thành các đoạn dài tối đa size ký tự (ghép lại bằng string.Concat).</summary>
        public static List<string> Chunk(string s, int size)
        {
            var list = new List<string>();
            for (int i = 0; i < s.Length; i += size)
                list.Add(s.Substring(i, Math.Min(size, s.Length - i)));
            return list;
        }
    }
}
