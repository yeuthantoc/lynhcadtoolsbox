using System.Text;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using LynhCad.Manhole.Core.Geometry;
using LynhCad.Manhole.Core.Model;

namespace LynhCad.Manhole.Plugin
{
    /// <summary>
    /// Thông số hố ga lưu kèm chi tiết đã vẽ (Xrecord trong extension dictionary của Group),
    /// để lệnh LCT_MANHOLE_EDIT đọc lại và vẽ lại. Lưu theo file DWG, không phụ thuộc máy.
    /// </summary>
    internal class DetailData
    {
        private const string Key = "LCT_MANHOLE_DATA";
        private const short Version = 1;

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
            foreach (var chunk in DetailNaming.Chunk(ParamsJson.Serialize(p), DetailNaming.XrecordChunkSize))
                rb.Add(new TypedValue((int)DxfCode.Text, chunk));

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
                if (c != null) center = center + (c.StartPoint - anchorPt);
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
}
