using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Autodesk.AutoCAD.Runtime;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

[assembly: ExtensionApplication(typeof(LynhCad.Loader.LoaderApp))]
[assembly: CommandClass(typeof(LynhCad.Loader.LoaderCommands))]

namespace LynhCad.Loader
{
    public class LoaderApp : IExtensionApplication
    {
        public void Initialize()
        {
            PluginHost.Init();
            PluginHost.Write("LynhCadToolsBox đã nạp. Lệnh: LCT_MANHOLE, LCT_MANHOLE_JSON, LCT_MANHOLE_EDIT, LCT_RELOAD (nạp lại DLL mới không cần mở lại AutoCAD).");
        }

        public void Terminate() { }
    }

    /// <summary>Lệnh cố định của AutoCAD; mỗi lệnh chuyển sang Entry.Run trong bản plugin đang nạp.</summary>
    public class LoaderCommands
    {
        [CommandMethod("LCT", "LCT_MANHOLE", "LCT_MANHOLE", CommandFlags.Modal)]
        public void Manhole() { PluginHost.Run("LCT_MANHOLE"); }

        [CommandMethod("LCT", "LCT_MANHOLE_JSON", "LCT_MANHOLE_JSON", CommandFlags.Modal)]
        public void ManholeJson() { PluginHost.Run("LCT_MANHOLE_JSON"); }

        [CommandMethod("LCT", "LCT_MANHOLE_EDIT", "LCT_MANHOLE_EDIT", CommandFlags.Modal)]
        public void ManholeEdit() { PluginHost.Run("LCT_MANHOLE_EDIT"); }

        [CommandMethod("LCT", "LCT_RELOAD", "LCT_RELOAD", CommandFlags.Modal)]
        public void Reload() { PluginHost.Load(true); }
    }

    /// <summary>
    /// Nạp plugin từ byte (Assembly.Load(byte[])) để file DLL không bị khoá và có thể nạp nhiều bản trong một phiên.
    /// Bản cũ vẫn nằm trong bộ nhớ đến khi đóng AutoCAD (.NET Framework không gỡ được assembly) - chấp nhận được khi phát triển.
    /// </summary>
    internal static class PluginHost
    {
        private const string PluginFile = "LynhCad.Manhole.dll";
        private const string CoreFile = "LynhCad.Manhole.Core.dll";
        private const string PluginName = "LynhCad.Manhole";
        private const string CoreName = "LynhCad.Manhole.Core";
        private const string EntryType = "LynhCad.Manhole.Plugin.Entry";

        private static Assembly plugin, core;
        private static DateTime loadedStamp;
        private static int generation;
        private static readonly Dictionary<Assembly, Assembly> coreOf = new Dictionary<Assembly, Assembly>();
        private static bool initialized;

        public static void Init()
        {
            if (initialized) return;
            initialized = true;
            AppDomain.CurrentDomain.AssemblyResolve += OnResolve;
        }

        /// <summary>
        /// Thư mục chứa DLL plugin: biến môi trường LYNHCAD_PLUGIN_DIR (vd thư mục bin\Release\net48 khi phát triển),
        /// mặc định là thư mục của loader.
        /// </summary>
        private static string Dir
        {
            get
            {
                string env = Environment.GetEnvironmentVariable("LYNHCAD_PLUGIN_DIR");
                if (!string.IsNullOrEmpty(env) && Directory.Exists(env)) return env;
                return Path.GetDirectoryName(typeof(PluginHost).Assembly.Location);
            }
        }

        private static DateTime Stamp(string dir)
        {
            var a = File.GetLastWriteTimeUtc(Path.Combine(dir, PluginFile));
            var b = File.GetLastWriteTimeUtc(Path.Combine(dir, CoreFile));
            return a > b ? a : b;
        }

        public static void Write(string msg)
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc != null) doc.Editor.WriteMessage("\n" + msg + "\n");
        }

        /// <summary>Nạp (lại) plugin. force = false: chỉ nạp khi chưa nạp hoặc DLL trên đĩa mới hơn bản đang chạy.</summary>
        public static bool Load(bool force)
        {
            Init();
            string dir = Dir;
            string pluginPath = Path.Combine(dir, PluginFile);
            string corePath = Path.Combine(dir, CoreFile);
            if (!File.Exists(pluginPath) || !File.Exists(corePath))
            {
                Write("LynhCadToolsBox: không thấy " + PluginFile + " / " + CoreFile + " trong " + dir);
                return false;
            }
            var stamp = Stamp(dir);
            if (!force && plugin != null && stamp <= loadedStamp) return true;

            try
            {
                var newCore = Assembly.Load(File.ReadAllBytes(corePath), ReadPdb(corePath));
                // đặt core trước khi nạp plugin để OnResolve trả đúng bản
                core = newCore;
                var newPlugin = Assembly.Load(File.ReadAllBytes(pluginPath), ReadPdb(pluginPath));
                coreOf[newPlugin] = newCore;
                plugin = newPlugin;
                loadedStamp = stamp;
                generation++;
            }
            catch (System.Exception ex)
            {
                // thường gặp: đang build/chép dở file
                Write("LynhCadToolsBox: nạp DLL lỗi (" + ex.Message + "). Chờ build/chép xong rồi chạy lại lệnh.");
                return plugin != null;
            }
            Write(string.Format("LynhCadToolsBox: đã nạp {0} {1} (lần {2}) từ {3}",
                PluginName, plugin.GetName().Version, generation, dir));
            return true;
        }

        private static byte[] ReadPdb(string dllPath)
        {
            string pdb = Path.ChangeExtension(dllPath, ".pdb");
            try { return File.Exists(pdb) ? File.ReadAllBytes(pdb) : null; }
            catch (IOException) { return null; }
        }

        public static void Run(string command)
        {
            if (!Load(false)) return;
            try
            {
                var t = plugin.GetType(EntryType, true);
                var m = t.GetMethod("Run", BindingFlags.Public | BindingFlags.Static);
                m.Invoke(null, new object[] { command });
            }
            catch (TargetInvocationException ex)
            {
                Write(command + " lỗi: " + (ex.InnerException ?? ex));
            }
            catch (System.Exception ex)
            {
                Write(command + " lỗi: " + ex);
            }
        }

        /// <summary>Plugin nạp từ byte không có thư mục để .NET tự tìm Core → trả về đúng bản Core đi cùng bản plugin.</summary>
        private static Assembly OnResolve(object sender, ResolveEventArgs args)
        {
            string name = new AssemblyName(args.Name).Name;
            if (name == CoreName)
            {
                Assembly c;
                if (args.RequestingAssembly != null && coreOf.TryGetValue(args.RequestingAssembly, out c)) return c;
                return core;
            }
            if (name == PluginName) return plugin;
            return null;
        }
    }
}
