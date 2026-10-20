using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace LynhCad.Manhole.Core.Model
{
    /// <summary>Đọc/ghi ManholeParams dạng JSON (dùng cho lưu cấu hình, vẽ hàng loạt, preview).</summary>
    public static class ParamsJson
    {
        private static DataContractJsonSerializer Serializer()
        {
            return new DataContractJsonSerializer(typeof(ManholeParams), new DataContractJsonSerializerSettings
            {
                UseSimpleDictionaryFormat = true,
            });
        }

        public static string Serialize(ManholeParams p)
        {
            using (var ms = new MemoryStream())
            {
                using (var w = JsonReaderWriterFactory.CreateJsonWriter(ms, Encoding.UTF8, false, true, "  "))
                {
                    Serializer().WriteObject(w, p);
                    w.Flush();
                }
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        public static ManholeParams Deserialize(string json)
        {
            using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                try
                {
                    return (ManholeParams)Serializer().ReadObject(ms);
                }
                catch (SerializationException ex)
                {
                    throw new InvalidDataException("File JSON thông số hố ga không hợp lệ: " + ex.Message, ex);
                }
            }
        }

        public static ManholeParams Load(string path) { return Deserialize(File.ReadAllText(path, Encoding.UTF8)); }

        /// <summary>Đọc file chứa MỘT hố ga ({...}) hoặc NHIỀU hố ga ([{...}, {...}]) để vẽ hàng loạt.</summary>
        public static List<ManholeParams> LoadMany(string path)
        {
            string json = File.ReadAllText(path, Encoding.UTF8).TrimStart('﻿', ' ', '\t', '\r', '\n');
            if (!json.StartsWith("[")) return new List<ManholeParams> { Deserialize(json) };
            var ser = new DataContractJsonSerializer(typeof(List<ManholeParams>));
            using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                try { return (List<ManholeParams>)ser.ReadObject(ms); }
                catch (SerializationException ex) { throw new InvalidDataException("File JSON danh sách hố ga không hợp lệ: " + ex.Message, ex); }
            }
        }

        public static void Save(ManholeParams p, string path) { File.WriteAllText(path, Serialize(p), new UTF8Encoding(false)); }
    }
}
