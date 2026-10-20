using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using LynhCad.Manhole.Core.Drawing;
using LynhCad.Manhole.Core.Geometry;
using LynhCad.Manhole.Core.Model;
using LynhCad.Manhole.Plugin.UI;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

[assembly: ExtensionApplication(typeof(LynhCad.Manhole.Plugin.PluginApp))]
[assembly: CommandClass(typeof(LynhCad.Manhole.Plugin.Commands))]

namespace LynhCad.Manhole.Plugin
{
    public class PluginApp : IExtensionApplication
    {
        public void Initialize()
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            doc.Editor.WriteMessage("\nLynhCadToolsBox - Manhole Detail đã nạp. Lệnh: LCT_MANHOLE (hộp thoại), LCT_MANHOLE_JSON (từ file JSON).\n");
        }

        public void Terminate() { }
    }

    public class Commands
    {
        /// <summary>Mở hộp thoại nhập thông số rồi vẽ chi tiết hố ga.</summary>
        [CommandMethod("LCT_MANHOLE", CommandFlags.Modal)]
        public void DrawManhole()
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            var last = PluginSettings.LoadLast() ?? ManholeParams.SampleRectangular();
            var win = new ManholeWindow(last);
            var ok = AcApp.ShowModalWindow(win);
            if (ok != true) return;

            var p = win.Params;
            PluginSettings.SaveLast(p);
            DrawAll(doc, new List<ManholeParams> { p });
        }

        /// <summary>Vẽ từ file JSON: một hố ga {...} hoặc nhiều hố ga [{...},{...}] đặt cạnh nhau.</summary>
        [CommandMethod("LCT_MANHOLE_JSON", CommandFlags.Modal)]
        public void DrawManholeFromJson()
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var ed = doc.Editor;

            var opt = new PromptOpenFileOptions("\nChọn file thông số hố ga (.json)")
            {
                Filter = "Manhole parameters (*.json)|*.json",
            };
            var res = ed.GetFileNameForOpen(opt);
            if (res.Status != PromptStatus.OK) return;

            List<ManholeParams> list;
            try { list = ParamsJson.LoadMany(res.StringResult); }
            catch (System.Exception ex)
            {
                ed.WriteMessage("\n" + ex.Message);
                return;
            }
            DrawAll(doc, list);
        }

        private static void DrawAll(Document doc, List<ManholeParams> list)
        {
            var ed = doc.Editor;

            // dựng hình trước, báo lỗi trước khi bắt chọn điểm
            var models = new List<Tuple<ManholeParams, DrawingModel>>();
            foreach (var p in list)
            {
                var res = DetailComposer.Compose(p);
                foreach (var w in res.Warnings) ed.WriteMessage("\n[{0}] Cảnh báo: {1}", p.Id, w);
                if (!res.Ok)
                {
                    foreach (var e in res.Errors) ed.WriteMessage("\n[{0}] LỖI: {1}", p.Id, e);
                    if (list.Count == 1)
                    {
                        AcApp.ShowAlertDialog("Thông số hố ga " + p.Id + " có lỗi:\n\n- " + string.Join("\n- ", res.Errors));
                        return;
                    }
                    continue;
                }
                models.Add(Tuple.Create(p, res.Model));
            }
            if (models.Count == 0) return;

            var ppr = ed.GetPoint("\nChọn điểm đặt (tâm hố ga trên mặt bằng): ");
            if (ppr.Status != PromptStatus.OK) return;
            var basePt = ppr.Value.TransformBy(ed.CurrentUserCoordinateSystem);

            var db = doc.Database;
            // tạo block mũi tên phải làm ngoài transaction (dùng sysvar DIMBLK)
            var oblique = AcadSetup.EnsureArrow(db, "_OBLIQUE");
            var dotArrow = AcadSetup.EnsureArrow(db, "_DOT");

            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                double x = basePt.X;
                int drawn = 0;
                foreach (var item in models)
                {
                    var p = item.Item1;
                    var model = item.Item2;
                    var b = model.GetBounds();
                    // điểm chọn = tâm hố của hố đầu tiên; các hố sau xếp sang phải
                    double dx = drawn == 0 ? x : x - b.MinX;
                    model.Translate(new Vec2(dx, basePt.Y));

                    var setup = new AcadSetup();
                    setup.Run(db, tr, p.Scale, oblique, dotArrow);
                    var renderer = new AcadRenderer(db, tr, space, setup);
                    renderer.Render(model);
                    MakeGroup(db, tr, p.Id, renderer.Created);
                    foreach (var msg in model.Messages)
                        if (msg.StartsWith("Bỏ qua")) ed.WriteMessage("\n[{0}] {1}", p.Id, msg);

                    var nb = model.GetBounds();
                    x = nb.MaxX + new DrawStyle(p.Scale).ViewGap * 2;
                    drawn++;
                    ed.WriteMessage("\n[{0}] Đã vẽ {1} đối tượng.", p.Id, renderer.Created.Count);
                }
                tr.Commit();
            }
            ed.Regen();
        }

        private static void MakeGroup(Database db, Transaction tr, string id, ObjectIdCollection ids)
        {
            if (ids.Count == 0) return;
            var gd = (DBDictionary)tr.GetObject(db.GroupDictionaryId, OpenMode.ForWrite);
            string baseName = "LCT_" + Sanitize(string.IsNullOrEmpty(id) ? "MH" : id);
            string name = baseName;
            int n = 1;
            while (gd.Contains(name)) name = baseName + "_" + (++n);
            var grp = new Group("LynhCadToolsBox manhole detail " + id, true);
            gd.SetAt(name, grp);
            tr.AddNewlyCreatedDBObject(grp, true);
            grp.Append(ids);
        }

        private static string Sanitize(string s)
        {
            var chars = s.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
                if (!char.IsLetterOrDigit(chars[i]) && chars[i] != '-' && chars[i] != '_') chars[i] = '_';
            return new string(chars);
        }
    }

    /// <summary>Nhớ thông số lần vẽ trước tại %APPDATA%\LynhCadToolsBox\manhole-last.json.</summary>
    internal static class PluginSettings
    {
        private static string PathFile
        {
            get
            {
                string dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LynhCadToolsBox");
                return System.IO.Path.Combine(dir, "manhole-last.json");
            }
        }

        public static ManholeParams LoadLast()
        {
            try { return File.Exists(PathFile) ? ParamsJson.Load(PathFile) : null; }
            catch (System.Exception) { return null; }
        }

        public static void SaveLast(ManholeParams p)
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PathFile));
                ParamsJson.Save(p, PathFile);
            }
            catch (System.Exception) { /* không quan trọng */ }
        }
    }
}
