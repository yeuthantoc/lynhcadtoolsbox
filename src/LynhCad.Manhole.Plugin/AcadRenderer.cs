using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using LynhCad.Manhole.Core.Drawing;
using LynhCad.Manhole.Core.Geometry;

namespace LynhCad.Manhole.Plugin
{
    /// <summary>Vẽ DrawingModel (lõi) thành entity AutoCAD trong một BlockTableRecord.</summary>
    internal class AcadRenderer : IPrimVisitor
    {
        private readonly Database db;
        private readonly Transaction tr;
        private readonly BlockTableRecord space;
        private readonly AcadSetup setup;
        private readonly ObjectIdCollection created = new ObjectIdCollection();
        private readonly ObjectIdCollection hatches = new ObjectIdCollection();
        private readonly List<string> skipped = new List<string>();
        /// <summary>Đang vẽ vào một BlockTableRecord định nghĩa block: PText có AttributeTag → AttributeDefinition.</summary>
        private bool inBlockDefinition;

        public AcadRenderer(Database db, Transaction tr, BlockTableRecord space, AcadSetup setup)
        {
            this.db = db; this.tr = tr; this.space = space; this.setup = setup;
        }

        public ObjectIdCollection Created { get { return created; } }

        /// <summary>Primitive bị bỏ qua vì AutoCAD báo lỗi khi tạo (để báo cho người dùng).</summary>
        public List<string> Skipped { get { return skipped; } }

        public void Render(DrawingModel model)
        {
            foreach (var p in model.Items)
            {
                try { p.Accept(this); }
                catch (Autodesk.AutoCAD.Runtime.Exception ex)
                {
                    // một primitive lỗi (vd hatch không đóng) không làm hỏng cả chi tiết
                    skipped.Add(string.Format("Bỏ qua {0} trên layer {1}: {2}", p.GetType().Name, p.Layer, ex.Message));
                }
            }
            if (hatches.Count > 0)
            {
                var dot = (DrawOrderTable)tr.GetObject(space.DrawOrderTableId, OpenMode.ForWrite);
                dot.MoveToBottom(hatches);
            }
        }

        private static Point3d P3(Vec2 p) { return new Point3d(p.X, p.Y, 0); }
        private static Point2d P2(Vec2 p) { return new Point2d(p.X, p.Y); }

        /// <summary>
        /// Thêm entity vào không gian vẽ. SetDatabaseDefaults đặt màu/linetype/... = ByLayer theo bản vẽ,
        /// nhưng nó cũng đè dim style / text style đã gán → lưu lại và gán lại sau.
        /// </summary>
        private ObjectId Add(Entity e, string layer)
        {
            var mt = e as MText;
            var dt = e as DBText;
            var dim = e as Dimension;
            var ld = e as Leader;
            ObjectId keepStyle = mt != null ? mt.TextStyleId : dt != null ? dt.TextStyleId : dim != null ? dim.DimensionStyle : ld != null ? ld.DimensionStyle : ObjectId.Null;
            double keepHeight = mt != null ? mt.TextHeight : dt != null ? dt.Height : 0;

            e.SetDatabaseDefaults(db);

            if (mt != null) { if (!keepStyle.IsNull) mt.TextStyleId = keepStyle; mt.TextHeight = keepHeight; }
            if (dt != null) { if (!keepStyle.IsNull) dt.TextStyleId = keepStyle; dt.Height = keepHeight; }
            if (dim != null && !keepStyle.IsNull) dim.DimensionStyle = keepStyle;
            if (ld != null && !keepStyle.IsNull) ld.DimensionStyle = keepStyle;
            return Append(e, layer);
        }

        /// <summary>Thêm entity đã đặt đủ thuộc tính (không gọi SetDatabaseDefaults nữa).</summary>
        private ObjectId Append(Entity e, string layer)
        {
            e.Layer = layer;
            var id = space.AppendEntity(e);
            tr.AddNewlyCreatedDBObject(e, true);
            created.Add(id);
            return id;
        }

        public void Visit(PPolyline pl) { Add(ToPolyline(pl), pl.Layer); }

        public void Visit(PCircle ci) { Add(new Circle(P3(ci.Center), Vector3d.ZAxis, ci.Radius), ci.Layer); }

        public void Visit(PArc arc) { Add(new Arc(P3(arc.Center), arc.Radius, arc.StartAngle, arc.EndAngle), arc.Layer); }

        public void Visit(PText t)
        {
            if (inBlockDefinition && t.AttributeTag != null) { Add(ToAttributeDefinition(t), t.Layer); return; }
            Add(ToMText(t.Position, t.Text, t.Height, t.Align, t.VAlign, t.Rotation, t.Underline), t.Layer);
        }

        private AttributeDefinition ToAttributeDefinition(PText t)
        {
            var ad = new AttributeDefinition(P3(t.Position), t.Text ?? "", t.AttributeTag, t.AttributeTag, setup.TextStyleId)
            {
                Height = t.Height,
                Rotation = t.Rotation,
                Justify = Attachment(t.Align, t.VAlign),
                LockPositionInBlock = false,
            };
            ad.AlignmentPoint = P3(t.Position);
            try { ad.AdjustAlignment(db); } catch (Autodesk.AutoCAD.Runtime.Exception) { }
            return ad;
        }

        /// <summary>
        /// Ký hiệu cao độ / ký hiệu cắt = BlockReference thật có attribute (định nghĩa trong SymbolLibrary, tạo nếu bản vẽ chưa có).
        /// AutoCAD từ chối thì vẽ bản khai triển (polyline + hatch + MText) để không mất ký hiệu.
        /// </summary>
        public void Visit(PBlock src)
        {
            try { AddBlockReference(src); }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
                foreach (var p in SymbolLibrary.Expand(src).Items) p.Accept(this);
            }
        }

        private ObjectId EnsureBlockDefinition(string name)
        {
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            if (bt.Has(name)) return bt[name]; // giữ block người dùng đã chỉnh
            var btr = new BlockTableRecord { Name = name, Origin = Point3d.Origin };
            bt.UpgradeOpen();
            var id = bt.Add(btr);
            tr.AddNewlyCreatedDBObject(btr, true);
            var sub = new AcadRenderer(db, tr, btr, setup) { inBlockDefinition = true };
            sub.Render(SymbolLibrary.Get(name));
            skipped.AddRange(sub.Skipped);
            return id;
        }

        private void AddBlockReference(PBlock src)
        {
            var defId = EnsureBlockDefinition(src.Name);
            var upright = new HashSet<string>(SymbolLibrary.Get(src.Name).Items.OfType<PText>()
                .Where(t => t.AttributeTag != null && t.KeepUpright).Select(t => t.AttributeTag));

            var br = new BlockReference(P3(src.Position), defId) { Rotation = src.Rotation, ScaleFactors = new Scale3d(src.Scale) };
            Add(br, src.Layer);

            var btr = (BlockTableRecord)tr.GetObject(defId, OpenMode.ForRead);
            foreach (ObjectId eid in btr)
            {
                var ad = tr.GetObject(eid, OpenMode.ForRead) as AttributeDefinition;
                if (ad == null || ad.Constant) continue;
                var ar = new AttributeReference();
                ar.SetAttributeFromBlock(ad, br.BlockTransform);
                string value;
                if (src.Attributes.TryGetValue(ad.Tag, out value)) ar.TextString = value;
                if (upright.Contains(ad.Tag))
                {
                    // chữ cái ký hiệu cắt luôn đứng thẳng dù block quay theo đường cắt
                    ar.Rotation = 0;
                    try { ar.AdjustAlignment(db); } catch (Autodesk.AutoCAD.Runtime.Exception) { }
                }
                br.AttributeCollection.AppendAttribute(ar);
                tr.AddNewlyCreatedDBObject(ar, true);
            }
        }

        private static Polyline ToPolyline(PPolyline src)
        {
            var pl = new Polyline();
            for (int i = 0; i < src.Points.Count; i++)
                pl.AddVertexAt(i, P2(src.Points[i]), src.Bulges[i], src.ConstantWidth, src.ConstantWidth);
            pl.Closed = src.Closed;
            return pl;
        }

        public void Visit(PEllipse e)
        {
            double a = Math.Max(e.RadiusX, e.RadiusY), b = Math.Min(e.RadiusX, e.RadiusY);
            if (a < 1e-6 || b < 1e-6) return;
            var major = e.RadiusX >= e.RadiusY ? new Vector3d(e.RadiusX, 0, 0) : new Vector3d(0, e.RadiusY, 0);
            if (b / a > 0.9999)
            {
                Add(new Circle(P3(e.Center), Vector3d.ZAxis, a), e.Layer);
                return;
            }
            Add(new Ellipse(P3(e.Center), Vector3d.ZAxis, major, b / a, 0, 2 * Math.PI), e.Layer);
        }

        /// <summary>Chấm thép = polyline 2 đỉnh, bulge 1, bề rộng = D/2 (giống lệnh DONUT).</summary>
        public void Visit(PDot d)
        {
            double r4 = d.Diameter / 4;
            var pl = new Polyline();
            pl.AddVertexAt(0, new Point2d(d.Center.X - r4, d.Center.Y), 1.0, d.Diameter / 2, d.Diameter / 2);
            pl.AddVertexAt(1, new Point2d(d.Center.X + r4, d.Center.Y), 1.0, d.Diameter / 2, d.Diameter / 2);
            pl.Closed = true;
            Add(pl, d.Layer);
        }

        public void Visit(PHatch src)
        {
            var h = new Hatch();
            var id = Add(h, src.Layer);
            h.Normal = Vector3d.ZAxis;
            h.Elevation = 0;
            // PatternScale/Angle phải đặt TRƯỚC SetHatchPattern mới có tác dụng
            h.PatternScale = src.PatternScale > 0 ? src.PatternScale : 1;
            h.PatternAngle = src.PatternAngle;
            string pattern = string.IsNullOrEmpty(src.Pattern) ? "SOLID" : src.Pattern.ToUpperInvariant();
            try { h.SetHatchPattern(HatchPatternType.PreDefined, pattern); }
            catch (Autodesk.AutoCAD.Runtime.Exception) { h.SetHatchPattern(HatchPatternType.PreDefined, "ANSI31"); }
            h.Associative = false;

            bool first = true;
            foreach (var loop in src.Loops)
            {
                var pts = new Point2dCollection();
                var bulges = new DoubleCollection();
                for (int i = 0; i < loop.Points.Count; i++)
                {
                    pts.Add(P2(loop.Points[i]));
                    bulges.Add(loop.Bulges[i]);
                }
                // vòng kín: lặp lại điểm đầu
                pts.Add(P2(loop.Points[0]));
                bulges.Add(0);
                h.AppendLoop(first ? HatchLoopTypes.External | HatchLoopTypes.Polyline : HatchLoopTypes.Polyline, pts, bulges);
                first = false;
            }
            h.EvaluateHatch(true);
            hatches.Add(id);
        }

        private MText ToMText(Vec2 pos, string text, double height, TextAlign align, TextVAlign valign, double rotation, bool underline)
        {
            var mt = new MText
            {
                Location = P3(pos),
                TextHeight = height,
                Rotation = rotation,
                Attachment = Attachment(align, valign),
                Width = 0,
            };
            if (!setup.TextStyleId.IsNull) mt.TextStyleId = setup.TextStyleId;
            string body = string.Join("\\P", (text ?? "").Split('\n').Select(EscapeMText));
            mt.Contents = underline ? "\\L" + body + "\\l" : body;
            return mt;
        }

        private static string EscapeMText(string s)
        {
            return s.Replace("\\", "\\\\").Replace("{", "\\{").Replace("}", "\\}").Replace("Ø", "%%C").Replace("→", "TO");
        }

        private static AttachmentPoint Attachment(TextAlign a, TextVAlign v)
        {
            switch (v)
            {
                case TextVAlign.Top:
                    return a == TextAlign.Left ? AttachmentPoint.TopLeft : a == TextAlign.Center ? AttachmentPoint.TopCenter : AttachmentPoint.TopRight;
                case TextVAlign.Bottom:
                    return a == TextAlign.Left ? AttachmentPoint.BottomLeft : a == TextAlign.Center ? AttachmentPoint.BottomCenter : AttachmentPoint.BottomRight;
                default:
                    return a == TextAlign.Left ? AttachmentPoint.MiddleLeft : a == TextAlign.Center ? AttachmentPoint.MiddleCenter : AttachmentPoint.MiddleRight;
            }
        }

        public void Visit(PDim d)
        {
            var dim = new RotatedDimension(d.AngleDeg * GeomUtil.Deg, P3(d.P1), P3(d.P2), P3(d.DimLinePoint), d.TextOverride ?? "", setup.DimStyleId);
            Add(dim, d.Layer);
        }

        /// <summary>
        /// Ghi chú = một MLeader thật của AutoCAD (đường dẫn + chữ là một đối tượng, di chuyển chữ thì đường dẫn đi theo).
        /// Nếu AutoCAD từ chối (style MLeader lạ...) thì vẽ Leader + MText như bản cũ để không mất ghi chú.
        /// </summary>
        public void Visit(PLeader src)
        {
            try { AddMLeader(src); }
            catch (Autodesk.AutoCAD.Runtime.Exception) { AddLeaderAndText(src); }
        }

        private void AddMLeader(PLeader src)
        {
            if (src.Points.Count == 0) return;
            double h = src.TextHeight;
            int side = src.Align == TextAlign.Left ? 1 : -1;
            var last = src.Points[src.Points.Count - 1];
            double landingGap = 0.4 * h;                                    // khoảng hở đường dẫn → chữ (như bản Leader cũ)
            double dogleg = Math.Max(0.1 * h, Math.Abs(src.TextPosition.X - last.X) - landingGap);

            var ml = new MLeader();
            ml.SetDatabaseDefaults(db);
            ml.Scale = 1.0;                                                 // kích thước dưới đây là mm model, không nhân scale của style
            ml.ContentType = ContentType.MTextContent;
            ml.LeaderLineType = LeaderType.StraightLeader;
            ml.EnableLanding = true;
            ml.EnableDogleg = true;
            ml.DoglegLength = dogleg;
            ml.LandingGap = landingGap;
            ml.ArrowSize = 0.6 * h;                                         // = Dimasz 1.5 mm giấy
            if (src.DotArrow && !setup.DotArrowId.IsNull) ml.ArrowSymbolId = setup.DotArrowId;
            if (!setup.TextStyleId.IsNull) ml.TextStyleId = setup.TextStyleId;
            ml.TextHeight = h;
            ml.TextAlignmentType = side > 0 ? TextAlignmentType.LeftAlignment : TextAlignmentType.RightAlignment;
            ml.SetTextAttachmentType(TextAttachmentType.AttachmentMiddle, LeaderDirectionType.LeftLeader);
            ml.SetTextAttachmentType(TextAttachmentType.AttachmentMiddle, LeaderDirectionType.RightLeader);

            var mt = ToMText(src.TextPosition, src.Text, h, src.Align, TextVAlign.Middle, 0, false);
            ml.MText = mt;

            int ld = ml.AddLeader();
            int ln = ml.AddLeaderLine(ld);
            ml.AddFirstVertex(ln, P3(src.Points[0]));
            for (int i = 1; i < src.Points.Count; i++) ml.AddLastVertex(ln, P3(src.Points[i]));
            ml.SetDogleg(ld, side > 0 ? Vector3d.XAxis : -Vector3d.XAxis);
            ml.TextLocation = P3(src.TextPosition);
            Append(ml, src.Layer);
        }

        private void AddLeaderAndText(PLeader src)
        {
            var ld = new Leader();
            foreach (var pt in src.Points) ld.AppendVertex(P3(pt));
            // đoạn ngang ngắn tới sát chữ
            double gap = src.TextHeight * 0.4;
            var end = new Vec2(src.TextPosition.X + (src.Align == TextAlign.Left ? -gap : gap), src.TextPosition.Y);
            ld.AppendVertex(P3(end));
            ld.HasArrowHead = true;
            ld.DimensionStyle = setup.DimStyleId;
            Add(ld, src.Layer);
            if (src.DotArrow && !setup.DotArrowId.IsNull)
            {
                try { ld.Dimldrblk = setup.DotArrowId; } catch (Autodesk.AutoCAD.Runtime.Exception) { }
            }
            Add(ToMText(src.TextPosition, src.Text, src.TextHeight, src.Align, TextVAlign.Middle, 0, false), src.Layer);
        }

        public void Visit(PTable src)
        {
            bool hasTitle = !string.IsNullOrEmpty(src.Title);
            int rows = src.Rows.Count + (hasTitle ? 1 : 0);
            int cols = src.ColumnCount;

            var t = new Table();
            t.TableStyle = db.Tablestyle;
            t.SetSize(rows, cols);
            t.Position = P3(src.Origin);
            for (int c = 0; c < cols; c++) t.Columns[c].Width = src.ColumnWidths[c];
            for (int r = 0; r < rows; r++) t.Rows[r].Height = src.RowHeight;

            int r0 = 0;
            if (hasTitle)
            {
                try { t.MergeCells(CellRange.Create(t, 0, 0, 0, cols - 1)); } catch (Autodesk.AutoCAD.Runtime.Exception) { }
                SetCell(t, 0, 0, src.Title, src.TextHeight * 1.2);
                r0 = 1;
            }
            for (int r = 0; r < src.Rows.Count; r++)
                for (int c = 0; c < cols; c++)
                    SetCell(t, r0 + r, c, c < src.Rows[r].Length ? src.Rows[r][c] : "", src.TextHeight);

            t.GenerateLayout();
            Add(t, src.Layer);
        }

        private void SetCell(Table t, int r, int c, string text, double height)
        {
            var cell = t.Cells[r, c];
            cell.TextString = text ?? "";
            cell.TextHeight = height;
            cell.Alignment = CellAlignment.MiddleCenter;
            if (!setup.TextStyleId.IsNull) cell.TextStyleId = setup.TextStyleId;
        }
    }
}
