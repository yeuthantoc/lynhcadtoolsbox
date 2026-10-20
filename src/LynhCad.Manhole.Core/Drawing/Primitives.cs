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

    public abstract class Prim
    {
        public string Layer;
        public abstract void AddBounds(Bounds b);
        public abstract void Translate(Vec2 d);
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

        public static PPolyline Line(Vec2 a, Vec2 b, string layer)
        {
            return new PPolyline { Layer = layer }.Add(a).Add(b);
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
    }

    public class PCircle : Prim
    {
        public Vec2 Center; public double Radius;
        public override void AddBounds(Bounds b) { b.Add(Center.X - Radius, Center.Y - Radius); b.Add(Center.X + Radius, Center.Y + Radius); }
        public override void Translate(Vec2 d) { Center = Center + d; }
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
    }

    /// <summary>Ellipse trục song song X/Y.</summary>
    public class PEllipse : Prim
    {
        public Vec2 Center; public double RadiusX, RadiusY;
        public override void AddBounds(Bounds b) { b.Add(Center.X - RadiusX, Center.Y - RadiusY); b.Add(Center.X + RadiusX, Center.Y + RadiusY); }
        public override void Translate(Vec2 d) { Center = Center + d; }
    }

    /// <summary>Chấm thép đặc (donut) đường kính Diameter.</summary>
    public class PDot : Prim
    {
        public Vec2 Center; public double Diameter;
        public override void AddBounds(Bounds b) { b.Add(Center.X - Diameter / 2, Center.Y - Diameter / 2); b.Add(Center.X + Diameter / 2, Center.Y + Diameter / 2); }
        public override void Translate(Vec2 d) { Center = Center + d; }
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

        public string[] Lines { get { return (Text ?? "").Split('\n'); } }

        public double EstimatedWidth
        {
            get { return Lines.Max(l => l.Length) * Height * 0.8; }
        }

        public double EstimatedHeight { get { return Lines.Length * Height * 1.5; } }

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
    }

    /// <summary>Đường dẫn ghi chú: Points[0] là đầu mũi tên, chữ đặt tại TextPosition.</summary>
    public class PLeader : Prim
    {
        public List<Vec2> Points = new List<Vec2>();
        public Vec2 TextPosition;
        public string Text;
        public double TextHeight;
        public TextAlign Align;
        /// <summary>Mũi tên dạng chấm (cho thép) thay vì mũi tên đặc.</summary>
        public bool DotArrow;

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
