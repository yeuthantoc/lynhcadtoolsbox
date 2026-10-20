using System;

namespace LynhCad.Manhole.Plugin
{
    /// <summary>
    /// Điểm vào duy nhất, được LynhCad.Loader.dll gọi qua reflection.
    /// Plugin KHÔNG khai báo [CommandMethod]: lệnh AutoCAD do loader đăng ký một lần,
    /// nhờ vậy loader nạp lại được DLL mới (LCT_RELOAD) mà không cần khởi động lại AutoCAD.
    /// Thêm lệnh mới: thêm case ở đây + [CommandMethod] trong src/LynhCad.Loader/Loader.cs + PackageContents.xml.
    /// </summary>
    public static class Entry
    {
        public static void Run(string command)
        {
            var c = new Commands();
            switch ((command ?? "").ToUpperInvariant())
            {
                case "LCT_MANHOLE": c.DrawManhole(); break;
                case "LCT_MANHOLE_JSON": c.DrawManholeFromJson(); break;
                case "LCT_MANHOLE_EDIT": c.EditManhole(); break;
                default: throw new ArgumentException("Lệnh không có trong plugin: " + command);
            }
        }
    }
}
