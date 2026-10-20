using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using LynhCad.Manhole.Core.Drawing;
using LynhCad.Manhole.Core.Geometry;
using LynhCad.Manhole.Core.Model;
using LynhCad.Manhole.Plugin.UI;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace LynhCad.Manhole.Plugin
{
    public class Commands
    {
        /// <summary>Block mũi tên cho dim style / leader. Phải tạo NGOÀI transaction (dùng sysvar DIMBLK).</summary>
        private struct Arrows
        {
            public ObjectId Oblique, Dot;

            public static Arrows Ensure(Database db)
            {
                return new Arrows { Oblique = AcadSetup.EnsureArrow(db, "_OBLIQUE"), Dot = AcadSetup.EnsureArrow(db, "_DOT") };
            }
        }

        /// <summary>Mở hộp thoại nhập thông số rồi vẽ chi tiết hố ga.</summary>
        public void DrawManhole()
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            var last = PluginSettings.LoadLast() ?? ManholeParams.SampleRectangular();
            var win = new ManholeWindow(last);
            if (AcApp.ShowModalWindow(win) != true) return;

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
            var models = new List<KeyValuePair<ManholeParams, DrawingModel>>();
            foreach (var p in list)
            {
                // một hố: báo lỗi bằng hộp thoại và dừng; nhiều hố: ghi lỗi ra dòng lệnh, bỏ qua hố đó
                var res = Compose(ed, p, list.Count == 1);
                if (res == null)
                {
                    if (list.Count == 1) return;
                    continue;
                }
                models.Add(new KeyValuePair<ManholeParams, DrawingModel>(p, res.Model));
            }
            if (models.Count == 0) return;

            var ppr = ed.GetPoint("\nChọn điểm đặt (tâm hố ga trên mặt bằng): ");
            if (ppr.Status != PromptStatus.OK) return;
            var basePt = ppr.Value.TransformBy(ed.CurrentUserCoordinateSystem);

            var db = doc.Database;
            var arrows = Arrows.Ensure(db);

            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                double x = basePt.X;
                bool first = true;
                foreach (var item in models)
                {
                    var p = item.Key;
                    var model = item.Value;
                    // điểm chọn = tâm hố của hố đầu tiên; các hố sau xếp sang phải
                    double dx = first ? x : x - model.GetBounds().MinX;
                    var center = new Vec2(dx, basePt.Y);
                    model.Translate(center);

                    var created = RenderDetail(db, tr, space, p, model, arrows, ed);
                    var grp = MakeGroup(db, tr, p.Id, created);
                    if (grp != null) DetailData.Save(tr, grp, p, center, created);

                    x = model.GetBounds().MaxX + new DrawStyle(p.Scale).ViewGap * 2;
                    first = false;
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

            var res = Compose(ed, p, true);
            if (res == null) return;
            var model = res.Model;
            model.Translate(data.Center);

            var arrows = Arrows.Ensure(db);

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
                var created = RenderDetail(db, tr, space, p, model, arrows, ed);
                if (created.Count > 0) grp.Append(created);
                grp.Description = DetailNaming.GroupDescription(p.Id);
                RenameGroup(db, tr, grp, p.Id);
                DetailData.Save(tr, grp, p, data.Center, created);
                tr.Commit();
                ed.WriteMessage("\n[{0}] Đã cập nhật: xoá {1}, vẽ lại {2} đối tượng.", p.Id, erased, created.Count);
            }
            ed.Regen();
        }

        /// <summary>
        /// Dựng chi tiết và ghi cảnh báo / lỗi ra dòng lệnh. Trả về null nếu thông số có lỗi
        /// (alert = true: báo thêm bằng hộp thoại).
        /// </summary>
        private static ComposeResult Compose(Editor ed, ManholeParams p, bool alert)
        {
            var res = DetailComposer.Compose(p);
            foreach (var w in res.Warnings) ed.WriteMessage("\n[{0}] Cảnh báo: {1}", p.Id, w);
            if (res.Ok) return res;
            foreach (var e in res.Errors) ed.WriteMessage("\n[{0}] LỖI: {1}", p.Id, e);
            if (alert) AcApp.ShowAlertDialog("Thông số hố ga " + p.Id + " có lỗi:\n\n- " + string.Join("\n- ", res.Errors));
            return null;
        }

        private static ObjectIdCollection RenderDetail(Database db, Transaction tr, BlockTableRecord space, ManholeParams p,
            DrawingModel model, Arrows arrows, Editor ed)
        {
            var setup = new AcadSetup();
            setup.Run(db, tr, p.Scale, arrows.Oblique, arrows.Dot);
            var renderer = new AcadRenderer(db, tr, space, setup);
            renderer.Render(model);
            foreach (var msg in renderer.Skipped) ed.WriteMessage("\n[{0}] {1}", p.Id, msg);
            return renderer.Created;
        }

        private static Group MakeGroup(Database db, Transaction tr, string id, ObjectIdCollection ids)
        {
            if (ids.Count == 0) return null;
            var gd = (DBDictionary)tr.GetObject(db.GroupDictionaryId, OpenMode.ForWrite);
            string name = DetailNaming.UniqueName(DetailNaming.GroupBaseName(id), gd.Contains);
            var grp = new Group(DetailNaming.GroupDescription(id), true);
            gd.SetAt(name, grp);
            tr.AddNewlyCreatedDBObject(grp, true);
            grp.Append(ids);
            return grp;
        }

        /// <summary>Đổi tên group khi người dùng đổi mã hố ga (Id) lúc cập nhật.</summary>
        private static void RenameGroup(Database db, Transaction tr, Group grp, string id)
        {
            string baseName = DetailNaming.GroupBaseName(id);
            string cur = grp.Name;
            if (DetailNaming.BelongsTo(cur, baseName)) return;
            var gd = (DBDictionary)tr.GetObject(db.GroupDictionaryId, OpenMode.ForWrite);
            string name = DetailNaming.UniqueName(baseName, gd.Contains);
            try { gd.SetName(cur, name); }
            catch (Autodesk.AutoCAD.Runtime.Exception) { /* giữ tên cũ */ }
        }
    }
}
