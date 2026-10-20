using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
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

    public class Commands
    {
        /// <summary>Mở hộp thoại nhập thông số rồi vẽ chi tiết hố ga.</summary>
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

                    var created = RenderDetail(db, tr, space, p, model, oblique, dotArrow, ed);
                    var grp = MakeGroup(db, tr, p.Id, created);
                    if (grp != null) DetailData.Save(tr, grp, p, new Vec2(dx, basePt.Y), created);

                    var nb = model.GetBounds();
                    x = nb.MaxX + new DrawStyle(p.Scale).ViewGap * 2;
                    drawn++;
                    ed.WriteMessage("\n[{0}] Đã vẽ {1} đối tượng.", p.Id, created.Count);
                }
                tr.Commit();
            }
            ed.Regen();
        }

        /// <summary>
        /// Sửa chi tiết hố ga đã vẽ: chọn một đối tượng bất kỳ của chi tiết, hộp thoại mở với thông số cũ,
        /// bấm Update → xoá các đối tượng cũ của chi tiết và vẽ lại tại đúng vị trí (kể cả khi đã MOVE).
        /// </summary>
        public void EditManhole()
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var ed = doc.Editor;
            var db = doc.Database;

            var per = ed.GetEntity(new PromptEntityOptions("\nChọn chi tiết hố ga cần cập nhật: "));
            if (per.Status != PromptStatus.OK) return;

            ObjectId groupId;
            DetailData data;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                groupId = DetailData.FindGroup(tr, per.ObjectId);
                data = groupId.IsNull ? null : DetailData.Read(tr, (Group)tr.GetObject(groupId, OpenMode.ForRead));
                tr.Commit();
            }
            if (data == null)
            {
                ed.WriteMessage("\nĐối tượng này không thuộc chi tiết hố ga có lưu thông số (vẽ bằng bản cũ hoặc đã EXPLODE/UNGROUP)."
                    + " Hãy xoá và vẽ lại bằng LCT_MANHOLE.");
                return;
            }

            var win = new ManholeWindow(data.Params, true);
            if (AcApp.ShowModalWindow(win) != true) return;
            var p = win.Params;
            PluginSettings.SaveLast(p);

            var res = DetailComposer.Compose(p);
            foreach (var w in res.Warnings) ed.WriteMessage("\n[{0}] Cảnh báo: {1}", p.Id, w);
            if (!res.Ok)
            {
                AcApp.ShowAlertDialog("Thông số hố ga " + p.Id + " có lỗi:\n\n- " + string.Join("\n- ", res.Errors));
                return;
            }
            var model = res.Model;
            model.Translate(data.Center);

            var oblique = AcadSetup.EnsureArrow(db, "_OBLIQUE");
            var dotArrow = AcadSetup.EnsureArrow(db, "_DOT");

            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var grp = (Group)tr.GetObject(groupId, OpenMode.ForWrite);
                int erased = 0;
                foreach (var id in grp.GetAllEntityIds())
                {
                    if (id.IsErased) continue;
                    var ent = tr.GetObject(id, OpenMode.ForWrite, false, true) as Entity;
                    if (ent == null) continue;
                    ent.Erase();
                    erased++;
                }
                grp.Clear();

                // vẽ vào đúng không gian chứa chi tiết cũ (model hoặc layout)
                var ownerId = tr.GetObject(per.ObjectId, OpenMode.ForRead, true).OwnerId;
                var space = (BlockTableRecord)tr.GetObject(ownerId, OpenMode.ForWrite);
                var created = RenderDetail(db, tr, space, p, model, oblique, dotArrow, ed);
                if (created.Count > 0) grp.Append(created);
                grp.Description = "LynhCadToolsBox manhole detail " + p.Id;
                RenameGroup(db, tr, grp, p.Id);
                DetailData.Save(tr, grp, p, data.Center, created);
                tr.Commit();
                ed.WriteMessage("\n[{0}] Đã cập nhật: xoá {1}, vẽ lại {2} đối tượng.", p.Id, erased, created.Count);
            }
            ed.Regen();
        }

        private static ObjectIdCollection RenderDetail(Database db, Transaction tr, BlockTableRecord space, ManholeParams p,
            DrawingModel model, ObjectId oblique, ObjectId dotArrow, Editor ed)
        {
            var setup = new AcadSetup();
            setup.Run(db, tr, p.Scale, oblique, dotArrow);
            var renderer = new AcadRenderer(db, tr, space, setup);
            renderer.Render(model);
            foreach (var msg in model.Messages)
                if (msg.StartsWith("Bỏ qua")) ed.WriteMessage("\n[{0}] {1}", p.Id, msg);
            return renderer.Created;
        }

        private static string GroupBaseName(string id)
        {
            return "LCT_" + Sanitize(string.IsNullOrEmpty(id) ? "MH" : id);
        }

        private static Group MakeGroup(Database db, Transaction tr, string id, ObjectIdCollection ids)
        {
            if (ids.Count == 0) return null;
            var gd = (DBDictionary)tr.GetObject(db.GroupDictionaryId, OpenMode.ForWrite);
            string baseName = GroupBaseName(id);
            string name = baseName;
            int n = 1;
            while (gd.Contains(name)) name = baseName + "_" + (++n);
            var grp = new Group("LynhCadToolsBox manhole detail " + id, true);
            gd.SetAt(name, grp);
            tr.AddNewlyCreatedDBObject(grp, true);
            grp.Append(ids);
            return grp;
        }

        /// <summary>Đổi tên group khi người dùng đổi mã hố ga (Id) lúc cập nhật.</summary>
        private static void RenameGroup(Database db, Transaction tr, Group grp, string id)
        {
            string baseName = GroupBaseName(id);
            string cur = grp.Name;
            if (cur == baseName || cur.StartsWith(baseName + "_")) return;
            var gd = (DBDictionary)tr.GetObject(db.GroupDictionaryId, OpenMode.ForWrite);
            string name = baseName;
            int n = 1;
            while (gd.Contains(name)) name = baseName + "_" + (++n);
            try { gd.SetName(cur, name); }
            catch (Autodesk.AutoCAD.Runtime.Exception) { /* giữ tên cũ */ }
        }

        private static string Sanitize(string s)
        {
            var chars = s.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
                if (!char.IsLetterOrDigit(chars[i]) && chars[i] != '-' && chars[i] != '_') chars[i] = '_';
            return new string(chars);
        }
    }

    /// <summary>
    /// Thông số hố ga lưu kèm chi tiết đã vẽ (Xrecord trong extension dictionary của Group),
    /// để lệnh LCT_MANHOLE_EDIT đọc lại và vẽ lại. Lưu theo file DWG, không phụ thuộc máy.
    /// </summary>
    internal class DetailData
    {
        private const string Key = "LCT_MANHOLE_DATA";
        private const short Version = 1;
        private const int ChunkSize = 250; // chuỗi trong Xrecord có giới hạn độ dài → chia nhỏ JSON

        public ManholeParams Params;
        /// <summary>Tâm hố ga trên mặt bằng hiện tại (đã cộng độ dời nếu người dùng MOVE chi tiết).</summary>
        public Vec2 Center;

        public static void Save(Transaction tr, Group grp, ManholeParams p, Vec2 center, ObjectIdCollection created)
        {
            // điểm neo: điểm đầu của đường đầu tiên trong chi tiết, dùng để biết chi tiết đã bị MOVE bao xa
            ObjectId anchorId = ObjectId.Null;
            Point3d anchorPt = Point3d.Origin;
            foreach (ObjectId id in created)
            {
                var c = tr.GetObject(id, OpenMode.ForRead) as Curve;
                if (c == null) continue;
                anchorId = id;
                anchorPt = c.StartPoint;
                break;
            }

            var rb = new ResultBuffer();
            rb.Add(new TypedValue((int)DxfCode.Int16, Version));
            rb.Add(new TypedValue(10, new Point3d(center.X, center.Y, 0)));
            if (!anchorId.IsNull)
            {
                rb.Add(new TypedValue((int)DxfCode.SoftPointerId, anchorId));
                rb.Add(new TypedValue(11, anchorPt));
            }
            string json = ParamsJson.Serialize(p);
            for (int i = 0; i < json.Length; i += ChunkSize)
                rb.Add(new TypedValue((int)DxfCode.Text, json.Substring(i, Math.Min(ChunkSize, json.Length - i))));

            if (grp.ExtensionDictionary.IsNull)
            {
                if (!grp.IsWriteEnabled) grp.UpgradeOpen();
                grp.CreateExtensionDictionary();
            }
            var xd = (DBDictionary)tr.GetObject(grp.ExtensionDictionary, OpenMode.ForWrite);
            if (xd.Contains(Key))
            {
                var xr = (Xrecord)tr.GetObject(xd.GetAt(Key), OpenMode.ForWrite);
                xr.Data = rb;
            }
            else
            {
                var xr = new Xrecord { Data = rb };
                xd.SetAt(Key, xr);
                tr.AddNewlyCreatedDBObject(xr, true);
            }
        }

        public static DetailData Read(Transaction tr, Group grp)
        {
            if (grp.ExtensionDictionary.IsNull) return null;
            var xd = (DBDictionary)tr.GetObject(grp.ExtensionDictionary, OpenMode.ForRead);
            if (!xd.Contains(Key)) return null;
            var xr = (Xrecord)tr.GetObject(xd.GetAt(Key), OpenMode.ForRead);
            if (xr.Data == null) return null;

            var json = new StringBuilder();
            Point3d center = Point3d.Origin, anchorPt = Point3d.Origin;
            ObjectId anchorId = ObjectId.Null;
            bool hasAnchorPt = false;
            foreach (var tv in xr.Data.AsArray())
            {
                switch (tv.TypeCode)
                {
                    case 10: center = (Point3d)tv.Value; break;
                    case 11: anchorPt = (Point3d)tv.Value; hasAnchorPt = true; break;
                    case (short)DxfCode.SoftPointerId: anchorId = (ObjectId)tv.Value; break;
                    case (short)DxfCode.Text: json.Append((string)tv.Value); break;
                }
            }
            if (json.Length == 0) return null;

            ManholeParams p;
            try { p = ParamsJson.Deserialize(json.ToString()); }
            catch (System.Exception) { return null; }

            // chi tiết đã bị MOVE → dời tâm theo điểm neo
            if (hasAnchorPt && !anchorId.IsNull && !anchorId.IsErased)
            {
                var c = tr.GetObject(anchorId, OpenMode.ForRead, false) as Curve;
                if (c != null)
                {
                    var d = c.StartPoint - anchorPt;
                    center = center + d;
                }
            }
            return new DetailData { Params = p, Center = new Vec2(center.X, center.Y) };
        }

        /// <summary>Tìm Group hố ga (có lưu thông số) chứa đối tượng được chọn.</summary>
        public static ObjectId FindGroup(Transaction tr, ObjectId entityId)
        {
            var ent = tr.GetObject(entityId, OpenMode.ForRead) as Entity;
            if (ent == null) return ObjectId.Null;
            var reactors = ent.GetPersistentReactorIds();
            if (reactors == null) return ObjectId.Null;
            foreach (ObjectId rid in reactors)
            {
                if (rid.IsNull || rid.IsErased) continue;
                var grp = tr.GetObject(rid, OpenMode.ForRead) as Group;
                if (grp == null || grp.ExtensionDictionary.IsNull) continue;
                var xd = (DBDictionary)tr.GetObject(grp.ExtensionDictionary, OpenMode.ForRead);
                if (xd.Contains(Key)) return rid;
            }
            return ObjectId.Null;
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
