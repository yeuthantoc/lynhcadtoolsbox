using System;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using LynhCad.Manhole.Core.Drawing;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace LynhCad.Manhole.Plugin
{
    /// <summary>Chuẩn bị layer, linetype, text style, dim style trong bản vẽ hiện hành.</summary>
    internal class AcadSetup
    {
        public const string TextStyleName = "LCT_ROMANS";

        public ObjectId TextStyleId { get; private set; }
        public ObjectId DimStyleId { get; private set; }
        public ObjectId DotArrowId { get; private set; }
        /// <summary>Block "_DOTBLANK" (vòng tròn rỗng) cho leader chỉ vào chấm thép.</summary>
        public ObjectId OpenDotArrowId { get; private set; }

        /// <summary>Lấy (và tạo nếu cần) block mũi tên chuẩn như "_OBLIQUE", "_DOT". Phải gọi NGOÀI transaction.</summary>
        public static ObjectId EnsureArrow(Database db, string arrowName)
        {
            string old = AcApp.GetSystemVariable("DIMBLK") as string ?? "";
            try
            {
                AcApp.SetSystemVariable("DIMBLK", arrowName); // AutoCAD tự tạo block mũi tên nếu chưa có
            }
            finally
            {
                AcApp.SetSystemVariable("DIMBLK", old.Length == 0 ? "." : old);
            }
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var id = bt.Has(arrowName) ? bt[arrowName] : ObjectId.Null;
                tr.Commit();
                return id;
            }
        }

        public void Run(Database db, Transaction tr, double scale, ObjectId obliqueArrow, ObjectId dotArrow, ObjectId openDotArrow)
        {
            EnsureLinetypes(db, tr);
            EnsureLayers(db, tr);
            TextStyleId = EnsureTextStyle(db, tr);
            DimStyleId = EnsureDimStyle(db, tr, scale, obliqueArrow);
            DotArrowId = dotArrow;
            OpenDotArrowId = openDotArrow;
        }

        private static void EnsureLinetypes(Database db, Transaction tr)
        {
            var lt = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
            foreach (var name in new[] { "HIDDEN", "CENTER" })
            {
                if (lt.Has(name)) continue;
                try { db.LoadLineTypeFile(name, "acadiso.lin"); }
                catch (Exception)
                {
                    try { db.LoadLineTypeFile(name, "acad.lin"); } catch (Exception) { /* để Continuous */ }
                }
            }
        }

        private static void EnsureLayers(Database db, Transaction tr)
        {
            var lt = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
            var layers = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            foreach (var def in Layers.All)
            {
                if (layers.Has(def.Name)) continue; // giữ nguyên layer người dùng đã chỉnh
                var ltr = new LayerTableRecord
                {
                    Name = def.Name,
                    Color = Color.FromColorIndex(ColorMethod.ByAci, def.Color),
                    LineWeight = ToLineWeight(def.LineWeight),
                    Description = def.Description,
                };
                if (lt.Has(def.Linetype)) ltr.LinetypeObjectId = lt[def.Linetype];
                if (!layers.IsWriteEnabled) layers.UpgradeOpen();
                layers.Add(ltr);
                tr.AddNewlyCreatedDBObject(ltr, true);
            }
        }

        private static LineWeight ToLineWeight(int w)
        {
            return Enum.IsDefined(typeof(LineWeight), w) ? (LineWeight)w : LineWeight.ByLineWeightDefault;
        }

        private static ObjectId EnsureTextStyle(Database db, Transaction tr)
        {
            var ts = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
            if (ts.Has(TextStyleName)) return ts[TextStyleName];
            var rec = new TextStyleTableRecord { Name = TextStyleName, FileName = "romans.shx", TextSize = 0, XScale = 1.0 };
            ts.UpgradeOpen();
            var id = ts.Add(rec);
            tr.AddNewlyCreatedDBObject(rec, true);
            return id;
        }

        private ObjectId EnsureDimStyle(Database db, Transaction tr, double scale, ObjectId obliqueArrow)
        {
            string name = "LCT_1-" + scale.ToString("0");
            var dst = (DimStyleTable)tr.GetObject(db.DimStyleTableId, OpenMode.ForRead);
            if (dst.Has(name)) return dst[name];

            var rec = new DimStyleTableRecord
            {
                Name = name,
                Dimscale = scale,
                Dimtxt = 2.5,
                Dimasz = 1.5,
                Dimexo = 1.5,
                Dimexe = 1.5,
                Dimgap = 1.0,
                Dimdli = 7.0,
                Dimtad = 1,
                Dimtih = false,
                Dimtoh = false,
                Dimdec = 0,
                Dimlunit = 2,
                Dimzin = 8,
                Dimtxsty = TextStyleId,
            };
            if (!obliqueArrow.IsNull)
            {
                rec.Dimblk = obliqueArrow;
                rec.Dimblk1 = obliqueArrow;
                rec.Dimblk2 = obliqueArrow;
            }
            dst.UpgradeOpen();
            var id = dst.Add(rec);
            tr.AddNewlyCreatedDBObject(rec, true);
            return id;
        }
    }
}
