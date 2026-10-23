using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using LynhCad.Manhole.Core.Drawing;
using LynhCad.Manhole.Core.Geometry;
using LynhCad.Manhole.Core.Model;

namespace LynhCad.Manhole.Plugin
{
    /// <summary>
    /// Tạo layout cho chi tiết hố ga theo SheetPlan (Core): mỗi tờ một layout "MH01-A1" / "MH01-A3-2"...,
    /// khổ giấy "DWG To PDF.pc3" full bleed (gốc giấy = (0,0)), khung tên ở (0,0), mỗi hình chiếu một viewport khoá tỉ lệ.
    /// Mỗi bước lỗi chỉ ghi cảnh báo, không làm hỏng chi tiết đã vẽ trong model.
    /// </summary>
    internal static class SheetLayouts
    {
        public const string Device = "DWG To PDF.pc3";

        /// <param name="offset">Độ dời của chi tiết trong model (điểm chọn), cộng vào cửa sổ model của viewport.</param>
        public static List<ObjectId> Create(Database db, Transaction tr, AcadSetup setup, SheetPlan plan, string id, Vec2 offset, List<string> messages)
        {
            var result = new List<ObjectId>();
            if (plan == null || !plan.HasSheet) return result;
            var lm = LayoutManager.Current;
            string baseName = DetailNaming.Sanitize(string.IsNullOrEmpty(id) ? "MH" : id) + "-" + plan.Format.Size;
            foreach (var page in plan.Pages)
            {
                string name = DetailNaming.UniqueName(plan.Pages.Count > 1 ? baseName + "-" + page.Number : baseName, lm.LayoutExists);
                ObjectId lid;
                try { lid = lm.CreateLayout(name); }
                catch (Autodesk.AutoCAD.Runtime.Exception ex) { messages.Add("Không tạo được layout " + name + ": " + ex.Message); continue; }
                result.Add(lid);

                var lay = (Layout)tr.GetObject(lid, OpenMode.ForWrite);
                SetupPlot(lay, plan.Format, messages);
                // tạo viewport giấy mặc định để viewport thêm vào bật được; bỏ viewport model mặc định
                try { lay.Initialize(); } catch (Autodesk.AutoCAD.Runtime.Exception) { }
                RemoveFloatingViewports(tr, lay);

                var btr = (BlockTableRecord)tr.GetObject(lay.BlockTableRecordId, OpenMode.ForWrite);
                var sheet = new DrawingModel();
                sheet.Add(page.TitleBlock);
                var renderer = new AcadRenderer(db, tr, btr, setup);
                renderer.Render(sheet);
                foreach (var msg in renderer.Skipped) messages.Add(msg);

                foreach (var v in page.Viewports)
                {
                    try { AddViewport(tr, btr, v, plan.Scale, offset); }
                    catch (Autodesk.AutoCAD.Runtime.Exception ex) { messages.Add(string.Format("Layout {0}: không tạo được viewport {1}: {2}", name, v.Name, ex.Message)); }
                }
                messages.Add(string.Format("Layout {0}: khổ {1}, 1:{2:0}, {3} viewport.", name, plan.Format.Size, plan.Scale, page.Viewports.Count));
            }
            return result;
        }

        /// <summary>Xoá các layout đã tạo cho chi tiết (khi cập nhật). Đang đứng ở layout đó thì chuyển về Model trước.</summary>
        public static void Delete(IEnumerable<string> names, List<string> messages)
        {
            var lm = LayoutManager.Current;
            foreach (var name in names)
            {
                if (string.IsNullOrEmpty(name) || !lm.LayoutExists(name)) continue;
                try
                {
                    if (string.Equals(lm.CurrentLayout, name, StringComparison.OrdinalIgnoreCase)) lm.CurrentLayout = "Model";
                    lm.DeleteLayout(name);
                }
                catch (System.Exception ex) { messages.Add("Không xoá được layout cũ " + name + ": " + ex.Message); }
            }
        }

        /// <summary>Tên layout của các id còn tồn tại.</summary>
        public static List<string> Names(Transaction tr, IEnumerable<ObjectId> ids)
        {
            var names = new List<string>();
            foreach (var id in ids)
            {
                if (id.IsNull || id.IsErased) continue;
                var lay = tr.GetObject(id, OpenMode.ForRead, false) as Layout;
                if (lay != null) names.Add(lay.LayoutName);
            }
            return names;
        }

        private static void SetupPlot(Layout lay, SheetFormat f, List<string> messages)
        {
            var psv = PlotSettingsValidator.Current;
            try
            {
                psv.SetPlotConfigurationName(lay, Device, null);
                psv.RefreshLists(lay);
                psv.SetCanonicalMediaName(lay, f.CanonicalMedia);
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex)
            {
                messages.Add(string.Format("Layout {0}: không đặt được máy in {1} / khổ {2} ({3}) — chọn lại trong Page Setup.",
                    lay.LayoutName, Device, f.Size, ex.Message));
            }
            try
            {
                psv.SetPlotPaperUnits(lay, PlotPaperUnit.Millimeters);
                psv.SetPlotRotation(lay, PlotRotation.Degrees000);
                psv.SetPlotType(lay, Autodesk.AutoCAD.DatabaseServices.PlotType.Layout);
                psv.SetUseStandardScale(lay, true);
                psv.SetStdScaleType(lay, StdScaleType.StdScale1To1);
                psv.SetPlotCentered(lay, false);
                psv.SetPlotOrigin(lay, new Point2d(0, 0));
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex)
            {
                messages.Add(string.Format("Layout {0}: thiết lập in chưa đầy đủ ({1}).", lay.LayoutName, ex.Message));
            }
        }

        /// <summary>Giữ viewport giấy (phần tử đầu tiên), xoá viewport model mặc định do Initialize tạo.</summary>
        private static void RemoveFloatingViewports(Transaction tr, Layout lay)
        {
            ObjectIdCollection ids;
            try { ids = lay.GetViewports(); } catch (Autodesk.AutoCAD.Runtime.Exception) { return; }
            if (ids == null) return;
            for (int i = 1; i < ids.Count; i++)
            {
                var vp = tr.GetObject(ids[i], OpenMode.ForWrite, false) as Viewport;
                if (vp != null) vp.Erase();
            }
        }

        private static void AddViewport(Transaction tr, BlockTableRecord btr, SheetViewport v, double scale, Vec2 offset)
        {
            var p = v.Paper;
            var w = v.ModelWindow;
            var vp = new Viewport
            {
                CenterPoint = new Point3d((p.X1 + p.X2) / 2, (p.Y1 + p.Y2) / 2, 0),
                Width = p.X2 - p.X1,
                Height = p.Y2 - p.Y1,
            };
            btr.AppendEntity(vp);
            tr.AddNewlyCreatedDBObject(vp, true);
            vp.Layer = Layers.Viewport;
            vp.ViewDirection = Vector3d.ZAxis;
            vp.ViewTarget = Point3d.Origin;
            vp.ViewCenter = new Point2d((w.X1 + w.X2) / 2 + offset.X, (w.Y1 + w.Y2) / 2 + offset.Y);
            vp.CustomScale = 1.0 / scale;
            vp.On = true;
            vp.Locked = true;
        }
    }
}
