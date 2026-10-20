using System;
using System.IO;
using LynhCad.Manhole.Core.Model;

namespace LynhCad.Manhole.Plugin
{
    /// <summary>Nhớ thông số lần vẽ trước tại %APPDATA%\LynhCadToolsBox\manhole-last.json.</summary>
    internal static class PluginSettings
    {
        private static string PathFile
        {
            get
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LynhCadToolsBox");
                return Path.Combine(dir, "manhole-last.json");
            }
        }

        public static ManholeParams LoadLast()
        {
            try { return File.Exists(PathFile) ? ParamsJson.Load(PathFile) : null; }
            catch (Exception) { return null; }
        }

        public static void SaveLast(ManholeParams p)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(PathFile));
                ParamsJson.Save(p, PathFile);
            }
            catch (Exception) { /* không quan trọng */ }
        }
    }
}
