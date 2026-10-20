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

        public AcadRenderer(Database db, Transaction tr, BlockTableRecord space, AcadSetup setup)
        {
            this.db = db; this.tr = tr; this.space = space; this.setup = setup;
        }

        public ObjectIdCollection Created { get { return created; } }

        public void Render(DrawingModel model)
        {
            foreach (var p in model.Items)
            {
                try { p.Accept(this); }
                catch (Autodesk.AutoCAD.Runtime.Exception ex)
                {
                    // một primitive lỗi (vd hatch không đóng) không làm hỏng cả chi tiết
                    model.Messages.Add(string.Format("Bỏ qua {0} trên layer {1}: {2}", p.GetType().Name, p.Layer, ex.Message));
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
            var dim = e as Dimension;
            var ld = e as Leader;
            ObjectId keepStyle = mt != null ? mt.TextStyleId : dim != null ? dim.DimensionStyle : ld != null ? ld.DimensionStyle : ObjectId.Null;
            double keepHeight = mt != null ? mt.TextHeight : 0;

            e.SetDatabaseDefaults(db);

            if (mt != null) { if (!keepStyle.IsNull) mt.TextStyleId = keepStyle; mt.TextHeight = keepHeight; }
            if (dim != null && !keepStyle.IsNull) dim.DimensionStyle = keepStyle;
            if (ld != null && !keepStyle.IsNull) ld.DimensionStyle = keepStyle;
            e.Layer = layer;
            var id = space.AppendEntity(e);
            tr.AddNewlyCreatedDBObject(e, true);
            created.Add(id);
            return id;
        }

        public void Visit(PPolyline pl) { Add(ToPolyline(pl), pl.Layer); }

        public void Visit(PCircle ci) { Add(new Circle(P3(ci.Center), Vector3d.ZAxis, ci.Radius), ci.Layer); }

        public void Visit(PArc arc) { Add(new Arc(P3(arc.Center), arc.Radius, arc.StartAngle, arc.EndAngle), arc.Layer); }

        public void Visit(PText t) { Add(ToMText(t.Position, t.Text, t.Height, t.Align, t.VAlign, t.Rotation, t.Underline), t.Layer); }

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

        public void Visit(PLeader src)
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
