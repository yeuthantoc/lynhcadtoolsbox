using System;
using System.Collections.Generic;
using System.Linq;
using LynhCad.Manhole.Core.Geometry;

namespace LynhCad.Manhole.Core.Drawing
{
    /*
     * Mô hình bản vẽ trung gian, độc lập với AutoCAD.
     * Builder sinh ra các primitive này; AcadRenderer (plugin) hoặc SvgExporter (preview/test) vẽ chúng ra.
     * Mọi toạ độ là toạ độ model (mm), tỉ lệ 1:1.
     */

    public enum TextAlign { Left, Center, Right }

    /// <summary>Neo dọc của chữ: Middle = giữa khối chữ, Bottom = chân dòng cuối, Top = đỉnh dòng đầu.</summary>
    public enum TextVAlign { Middle, Bottom, Top }

    /// <summary>
    /// Mỗi renderer (SvgExporter, AcadRenderer) cài interface này. Thêm primitive mới = thêm một Visit ở đây,
    /// trình biên dịch sẽ báo mọi renderer chưa xử lý (thay vì primitive lặng lẽ biến mất khỏi bản vẽ).
    /// </summary>
    public interface IPrimVisitor
    {
        void Visit(PPolyline p);
        void Visit(PCircle p);
        void Visit(PArc p);
        void Visit(PEllipse p);
        void Visit(PDot p);
        void Visit(PHatch p);
        void Visit(PText p);
        void Visit(PDim p);
        void Visit(PLeader p);
        void Visit(PTable p);
        void Visit(PBlock p);
    }

    /// <summary>Phép đồng dạng: p' = Origin + quay(p·Scale, Rotation). Dùng để khai triển block định nghĩa theo mm giấy.</summary>
    public struct Similarity
    {
        public readonly Vec2 Origin;
        public readonly double Scale, Rotation;
        public Similarity(Vec2 origin, double scale, double rotation) { Origin = origin; Scale = scale; Rotation = rotation; }

        public Vec2 Apply(Vec2 p)
        {
            double c = Math.Cos(Rotation), s = Math.Sin(Rotation);
            double x = p.X * Scale, y = p.Y * Scale;
            return new Vec2(Origin.X + x * c - y * s, Origin.Y + x * s + y * c);
        }

        public double Length(double l) { return l * Scale; }
    }

    public abstract class Prim
    {
        public string Layer;
        /// <summary>Chú thích khi rê chuột (chỉ preview trong hộp thoại; SVG / AutoCAD bỏ qua).</summary>
        public string Tip;
        /// <summary>Khoá liên kết với ô nhập (tên thuộc tính ManholeParams) để preview làm nổi bật khi ô đó có focus; chỉ preview dùng.</summary>
        public string Key;
        public abstract void AddBounds(Bounds b);
        public abstract void Translate(Vec2 d);
        /// <summary>Biến đổi đồng dạng tại chỗ (dùng khi khai triển block).</summary>
        public abstract void Transform(Similarity t);
        public abstract void Accept(IPrimVisitor v);
    }

    /// <summary>Polyline 2D có bulge (cung tròn).</summary>
    public class PPolyline : Prim
    {
        public List<Vec2> Points = new List<Vec2>();
        /// <summary>Bulge của đoạn từ Points[i] tới Points[i+1]. Cùng số phần tử với Points.</summary>
        public List<double> Bulges = new List<double>();
        public bool Closed;
        /// <summary>Bề rộng nét hằng (0 = không). Dùng cho chấm thép (donut).</summary>
        public double ConstantWidth;

        public PPolyline Add(Vec2 p, double bulge = 0) { Points.Add(p); Bulges.Add(bulge); return this; }

        /// <summary>
        /// Thêm cung tròn tâm center, bán kính r, từ góc a0 tới a1 (radian; a1 &lt; a0 = chiều kim đồng hồ),
        /// chia ≤ 90° mỗi đoạn có bulge. Thêm cả điểm cuối cung (bulge 0).
        /// </summary>
        public PPolyline AddArc(Vec2 center, double r, double a0, double a1)
        {
            double sweep = a1 - a0;
            int n = Math.Max(1, (int)Math.Ceiling(Math.Abs(sweep) / (Math.PI / 2) - 1e-9));
            double step = sweep / n;
            for (int i = 0; i < n; i++)
                Add(center + Vec2.FromAngle(a0 + i * step) * r, GeomUtil.Bulge(step));
            return Add(center + Vec2.FromAngle(a1) * r);
        }

        public static PPolyline Line(Vec2 a, Vec2 b, string layer)
        {
            return new PPolyline { Layer = layer }.Add(a).Add(b);
        }

        /// <summary>Đường gấp khúc hở với mọi đỉnh giữa bo cung bán kính r (vd thanh thép uốn theo bán kính tim).</summary>
        public static PPolyline Fillet(IList<Vec2> pts, double r, string layer)
        {
            var p = new PPolyline { Layer = layer };
            GeomUtil.Fillet(pts, r, p.Points, p.Bulges);
            return p;
        }

        public static PPolyline Rect(Rect2 r, string layer)
        {
            var p = new PPolyline { Layer = layer, Closed = true };
            foreach (var c in r.Corners()) p.Add(c);
            return p;
        }

        public override void AddBounds(Bounds b)
        {
            for (int i = 0; i < Points.Count; i++)
            {
                b.Add(Points[i]);
                // cung tròn có thể phình ra ngoài các đỉnh — lấy thêm điểm giữa cung
                if (Math.Abs(Bulges[i]) > 1e-9 && (i + 1 < Points.Count || Closed))
                    b.Add(ArcMid(Points[i], Points[(i + 1) % Points.Count], Bulges[i]));
            }
        }

        public static Vec2 ArcMid(Vec2 a, Vec2 c, double bulge)
        {
            var mid = (a + c) * 0.5;
            var chord = c - a;
            // sagitta = bulge * chord/2, hướng sang phải của chord khi bulge > 0 (CCW)
            var n = new Vec2(chord.Y, -chord.X).Normalized();
            return mid + n * (bulge * chord.Length / 2);
        }

        public override void Translate(Vec2 d) { for (int i = 0; i < Points.Count; i++) Points[i] = Points[i] + d; }
        public override void Transform(Similarity t)
        {
            for (int i = 0; i < Points.Count; i++) Points[i] = t.Apply(Points[i]);
            ConstantWidth = t.Length(ConstantWidth);
        }
        public override void Accept(IPrimVisitor v) { v.Visit(this); }
    }

    public class PCircle : Prim
    {
        public Vec2 Center; public double Radius;
        public override void AddBounds(Bounds b) { b.Add(Center.X - Radius, Center.Y - Radius); b.Add(Center.X + Radius, Center.Y + Radius); }
        public override void Translate(Vec2 d) { Center = Center + d; }
        public override void Transform(Similarity t) { Center = t.Apply(Center); Radius = t.Length(Radius); }
        public override void Accept(IPrimVisitor v) { v.Visit(this); }
    }

    /// <summary>Cung tròn CCW từ StartAngle tới EndAngle (radian).</summary>
    public class PArc : Prim
    {
        public Vec2 Center; public double Radius, StartAngle, EndAngle;
        public override void AddBounds(Bounds b)
        {
            double sweep = GeomUtil.NormAngle(EndAngle - StartAngle);
            int n = Math.Max(2, (int)(sweep / (Math.PI / 16)) + 1);
            for (int i = 0; i <= n; i++) b.Add(Center + Vec2.FromAngle(StartAngle + sweep * i / n) * Radius);
        }
        public override void Translate(Vec2 d) { Center = Center + d; }
        public override void Transform(Similarity t)
        {
            Center = t.Apply(Center); Radius = t.Length(Radius);
            StartAngle += t.Rotation; EndAngle += t.Rotation;
        }
        public override void Accept(IPrimVisitor v) { v.Visit(this); }
    }

    /// <summary>Ellipse trục song song X/Y.</summary>
    public class PEllipse : Prim
    {
        public Vec2 Center; public double RadiusX, RadiusY;
        public override void AddBounds(Bounds b) { b.Add(Center.X - RadiusX, Center.Y - RadiusY); b.Add(Center.X + RadiusX, Center.Y + RadiusY); }
        public override void Translate(Vec2 d) { Center = Center + d; }
        public override void Transform(Similarity t)
        {
            if (Math.Abs(Math.Sin(t.Rotation)) > 1e-9) throw new NotSupportedException("PEllipse chỉ biến đổi được khi không quay.");
            Center = t.Apply(Center); RadiusX = t.Length(RadiusX); RadiusY = t.Length(RadiusY);
        }
        public override void Accept(IPrimVisitor v) { v.Visit(this); }
    }

    /// <summary>Chấm thép đặc (donut) đường kính Diameter.</summary>
    public class PDot : Prim
    {
        public Vec2 Center; public double Diameter;
        public override void AddBounds(Bounds b) { b.Add(Center.X - Diameter / 2, Center.Y - Diameter / 2); b.Add(Center.X + Diameter / 2, Center.Y + Diameter / 2); }
        public override void Translate(Vec2 d) { Center = Center + d; }
        public override void Transform(Similarity t) { Center = t.Apply(Center); Diameter = t.Length(Diameter); }
        public override void Accept(IPrimVisitor v) { v.Visit(this); }
    }

    public class PHatch : Prim
    {
        /// <summary>Các vòng biên kín (polyline có bulge). Vòng trong tạo lỗ (island).</summary>
        public List<PPolyline> Loops = new List<PPolyline>();
        public string Pattern = "AR-CONC";
        public double PatternScale = 1;
        public double PatternAngle;
        public override void AddBounds(Bounds b) { foreach (var l in Loops) l.AddBounds(b); }
        public override void Translate(Vec2 d) { foreach (var l in Loops) l.Translate(d); }
        public override void Transform(Similarity t)
        {
            foreach (var l in Loops) l.Transform(t);
            PatternScale = t.Length(PatternScale); PatternAngle += t.Rotation;
        }
        public override void Accept(IPrimVisitor v) { v.Visit(this); }
    }

    public class PText : Prim
    {
        public Vec2 Position;
        public double Height;
        public string Text;
        public TextAlign Align = TextAlign.Left;
        public TextVAlign VAlign = TextVAlign.Middle;
        public double Rotation;
        public bool Underline;
        /// <summary>Trong block định nghĩa: chữ này là attribute với tag này (Text = giá trị mặc định / prompt).</summary>
        public string AttributeTag;
        /// <summary>Attribute luôn đứng thẳng (không quay theo block) — chữ cái ký hiệu cắt.</summary>
        public bool KeepUpright;

        public string[] Lines { get { return (Text ?? "").Split('\n'); } }

        public double EstimatedWidth
        {
            get { return Lines.Max(l => l.Length) * Height * 0.8; }
        }

        /// <summary>Bước dòng của MText AutoCAD (line spacing 1.0 = 5/3 chiều cao chữ). SVG và cách xếp ghi chú dùng cùng giá trị.</summary>
        public const double LineSpacing = 5.0 / 3.0;

        public double EstimatedHeight { get { return Lines.Length * Height * LineSpacing; } }

        public override void AddBounds(Bounds b)
        {
            double w = EstimatedWidth, h = EstimatedHeight;
            double x0 = Align == TextAlign.Left ? Position.X : Align == TextAlign.Center ? Position.X - w / 2 : Position.X - w;
            double y0 = VAlign == TextVAlign.Middle ? Position.Y - h / 2 : VAlign == TextVAlign.Bottom ? Position.Y : Position.Y - h;
            if (Math.Abs(Rotation) > 1e-6)
            {
                // ước lượng thô cho chữ xoay
                b.Add(Position.X - h, Position.Y - w); b.Add(Position.X + h, Position.Y + w);
                return;
            }
            b.Add(x0, y0); b.Add(x0 + w, y0 + h);
        }
        public override void Translate(Vec2 d) { Position = Position + d; }
        public override void Transform(Similarity t)
        {
            Position = t.Apply(Position); Height = t.Length(Height);
            if (!KeepUpright) Rotation += t.Rotation;
        }
        public override void Accept(IPrimVisitor v) { v.Visit(this); }
    }

    /// <summary>Kích thước tuyến tính. Angle = 0 đo ngang, 90 đo đứng.</summary>
    public class PDim : Prim
    {
        public Vec2 P1, P2, DimLinePoint;
        public double AngleDeg;
        /// <summary>Ghi đè chữ ("" = để AutoCAD tự đo).</summary>
        public string TextOverride = "";

        public double Measured
        {
            get
            {
                var dir = Vec2.FromAngle(AngleDeg * GeomUtil.Deg);
                return Math.Abs((P2 - P1).Dot(dir));
            }
        }

        public override void AddBounds(Bounds b) { b.Add(P1); b.Add(P2); b.Add(DimLinePoint); }
        public override void Translate(Vec2 d) { P1 = P1 + d; P2 = P2 + d; DimLinePoint = DimLinePoint + d; }
        public override void Transform(Similarity t)
        {
            P1 = t.Apply(P1); P2 = t.Apply(P2); DimLinePoint = t.Apply(DimLinePoint);
            AngleDeg += t.Rotation / GeomUtil.Deg;
        }
        public override void Accept(IPrimVisitor v) { v.Visit(this); }
    }

    /// <summary>Đường dẫn ghi chú: Points[0] là đầu mũi tên, chữ đặt tại TextPosition.</summary>
    /// <summary>
    /// Đầu leader: mũi tên (mặc định, chỉ vào nét / thanh thép vẽ dạng đường), vòng tròn rỗng khi chỉ vào chấm thép (thanh cắt ngang),
    /// vòng tròn đặc khi chỉ vào vùng hatch (bê tông, benching).
    /// </summary>
    public enum LeaderEnd { Arrow, OpenCircle, FilledCircle }

    public class PLeader : Prim
    {
        public List<Vec2> Points = new List<Vec2>();
        public Vec2 TextPosition;
        public string Text;
        public double TextHeight;
        public TextAlign Align;
        public LeaderEnd End = LeaderEnd.Arrow;

        public override void AddBounds(Bounds b)
        {
            foreach (var p in Points) b.Add(p);
            new PText { Position = TextPosition, Height = TextHeight, Text = Text, Align = Align }.AddBounds(b);
        }
        public override void Translate(Vec2 d)
        {
            for (int i = 0; i < Points.Count; i++) Points[i] = Points[i] + d;
            TextPosition = TextPosition + d;
        }
        public override void Transform(Similarity t)
        {
            for (int i = 0; i < Points.Count; i++) Points[i] = t.Apply(Points[i]);
            TextPosition = t.Apply(TextPosition); TextHeight = t.Length(TextHeight);
        }
        public override void Accept(IPrimVisitor v) { v.Visit(this); }
    }

    /// <summary>Bảng (bảng thống kê thép). Origin = góc trên trái.</summary>
    public class PTable : Prim
    {
        public Vec2 Origin;
        public string Title;
        public List<string[]> Rows = new List<string[]>(); // Rows[0] = tiêu đề cột
        public double[] ColumnWidths;
        public double RowHeight;
        public double TextHeight;

        public int ColumnCount { get { return ColumnWidths.Length; } }
        public double Width { get { return ColumnWidths.Sum(); } }
        public double Height { get { return RowHeight * (Rows.Count + (string.IsNullOrEmpty(Title) ? 0 : 1)); } }

        public override void AddBounds(Bounds b) { b.Add(Origin); b.Add(Origin.X + Width, Origin.Y - Height); }
        public override void Translate(Vec2 d) { Origin = Origin + d; }
        public override void Transform(Similarity t)
        {
            if (Math.Abs(Math.Sin(t.Rotation)) > 1e-9) throw new NotSupportedException("PTable chỉ biến đổi được khi không quay.");
            Origin = t.Apply(Origin);
            for (int i = 0; i < ColumnWidths.Length; i++) ColumnWidths[i] = t.Length(ColumnWidths[i]);
            RowHeight = t.Length(RowHeight); TextHeight = t.Length(TextHeight);
        }
        public override void Accept(IPrimVisitor v) { v.Visit(this); }
    }

    /// <summary>
    /// Chèn block có attribute (ký hiệu cao độ, ký hiệu cắt). Định nghĩa block lấy từ SymbolLibrary theo Name,
    /// vẽ theo mm giấy, gốc = điểm chèn; chèn với Scale (= tỉ lệ bản vẽ) và Rotation (radian).
    /// Renderer không có block (SVG, preview) thì vẽ bản khai triển (SymbolLibrary.Expand).
    /// </summary>
    public class PBlock : Prim
    {
        public string Name;
        public Vec2 Position;
        public double Rotation;
        public double Scale = 1;
        /// <summary>Giá trị attribute theo tag.</summary>
        public Dictionary<string, string> Attributes = new Dictionary<string, string>();

        public Similarity Placement { get { return new Similarity(Position, Scale, Rotation); } }

        public override void AddBounds(Bounds b) { foreach (var p in SymbolLibrary.Expand(this).Items) p.AddBounds(b); }
        public override void Translate(Vec2 d) { Position = Position + d; }
        public override void Transform(Similarity t)
        {
            Position = t.Apply(Position); Scale = t.Length(Scale); Rotation += t.Rotation;
        }
        public override void Accept(IPrimVisitor v) { v.Visit(this); }
    }

    /// <summary>Tập primitive của một hình chiếu / cả bản vẽ.</summary>
    public class DrawingModel
    {
        public readonly List<Prim> Items = new List<Prim>();
        /// <summary>Cảnh báo phát sinh khi dựng hình (hiển thị cho người dùng).</summary>
        public readonly List<string> Messages = new List<string>();

        public T Add<T>(T p) where T : Prim { Items.Add(p); return p; }

        public void AddRange(DrawingModel other) { Items.AddRange(other.Items); Messages.AddRange(other.Messages); }

        public Bounds GetBounds()
        {
            var b = new Bounds();
            foreach (var i in Items) i.AddBounds(b);
            return b;
        }

        public void Translate(Vec2 d) { foreach (var i in Items) i.Translate(d); }
    }
}
