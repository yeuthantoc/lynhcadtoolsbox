using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using LynhCad.Manhole.Core.Drawing;
using LynhCad.Manhole.Core.Geometry;

namespace LynhCad.Manhole.Plugin.UI
{
    /// <summary>
    /// Vẽ một DrawingModel nhỏ (sơ đồ ống trong hộp thoại) lên Canvas WPF, co giãn vừa khung.
    /// Toạ độ model (mm, Y lên trên) → pixel (Y xuống dưới).
    /// </summary>
    internal sealed class PreviewRenderer : IPrimVisitor
    {
        private const double Margin = 8;
        private readonly Canvas canvas;
        private readonly double k, ox, oy, minX, maxY;
        private readonly Dictionary<string, Brush> brushes = new Dictionary<string, Brush>();

        private PreviewRenderer(Canvas canvas, Bounds b)
        {
            this.canvas = canvas;
            double w = Math.Max(1, canvas.ActualWidth - 2 * Margin), h = Math.Max(1, canvas.ActualHeight - 2 * Margin);
            k = Math.Min(w / Math.Max(1, b.Width), h / Math.Max(1, b.Height));
            minX = b.MinX; maxY = b.MaxY;
            ox = Margin + (w - b.Width * k) / 2;
            oy = Margin + (h - b.Height * k) / 2;
        }

        /// <summary>Xoá Canvas rồi vẽ model vừa khung hiện tại. Model rỗng hoặc khung chưa có kích thước → để trống.</summary>
        public static void Render(Canvas canvas, DrawingModel model)
        {
            canvas.Children.Clear();
            if (model == null || model.Items.Count == 0 || canvas.ActualWidth < 10 || canvas.ActualHeight < 10) return;
            var b = model.GetBounds();
            if (!b.IsValid) return;
            var r = new PreviewRenderer(canvas, b);
            // hatch trước để nằm dưới nét
            foreach (var p in model.Items) if (p is PHatch) p.Accept(r);
            foreach (var p in model.Items) if (!(p is PHatch)) p.Accept(r);
        }

        private Point P(Vec2 v) { return new Point(ox + (v.X - minX) * k, oy + (maxY - v.Y) * k); }

        private Brush BrushOf(string layer)
        {
            Brush br;
            if (!brushes.TryGetValue(layer ?? "", out br))
            {
                br = new SolidColorBrush((Color)ColorConverter.ConvertFromString(PipeLayoutPreview.DisplayColor(layer)));
                br.Freeze();
                brushes[layer ?? ""] = br;
            }
            return br;
        }

        /// <summary>Nét: ống ra / ống lỗi đậm hơn; layer HIDDEN / CENTER vẽ nét đứt.</summary>
        private void Stroke(Shape s, string layer)
        {
            s.Stroke = BrushOf(layer);
            bool strong = layer == PipeLayoutPreview.OutletLayer || layer == PipeLayoutPreview.ErrorLayer || layer == Layers.SectionMark;
            s.StrokeThickness = strong ? 2 : 1;
            var def = Layers.Find(layer);
            if (def != null && def.Linetype == "HIDDEN") s.StrokeDashArray = new DoubleCollection { 4, 3 };
            else if (def != null && def.Linetype == "CENTER") s.StrokeDashArray = new DoubleCollection { 10, 3, 2, 3 };
        }

        private void AddPath(Geometry geom, string layer, bool fill)
        {
            var path = new Path { Data = geom };
            if (fill) path.Fill = BrushOf(layer); else Stroke(path, layer);
            canvas.Children.Add(path);
        }

        private PathFigure Figure(PPolyline pl)
        {
            int n = pl.Points.Count;
            var fig = new PathFigure { StartPoint = P(pl.Points[0]), IsClosed = pl.Closed, IsFilled = true };
            int segs = pl.Closed ? n : n - 1;
            for (int i = 0; i < segs; i++)
            {
                var a = pl.Points[i];
                var c = pl.Points[(i + 1) % n];
                double bulge = pl.Bulges[i];
                if (Math.Abs(bulge) < 1e-9) { fig.Segments.Add(new LineSegment(P(c), true)); continue; }
                double theta = 4 * Math.Atan(Math.Abs(bulge));
                double r = (c - a).Length / (2 * Math.Sin(theta / 2)) * k;
                // bulge > 0 = ngược chiều kim đồng hồ trong model; hình hiển thị giữ nguyên chiều đó
                fig.Segments.Add(new ArcSegment(P(c), new Size(r, r), 0, theta > Math.PI,
                    bulge > 0 ? SweepDirection.Counterclockwise : SweepDirection.Clockwise, true));
            }
            return fig;
        }

        public void Visit(PPolyline pl)
        {
            if (pl.Points.Count < 2) return;
            var g = new PathGeometry();
            g.Figures.Add(Figure(pl));
            AddPath(g, pl.Layer, false);
        }

        public void Visit(PHatch h)
        {
            var g = new PathGeometry { FillRule = FillRule.EvenOdd };
            foreach (var loop in h.Loops) if (loop.Points.Count >= 2) g.Figures.Add(Figure(loop));
            AddPath(g, h.Layer, true);
        }

        public void Visit(PCircle c)
        {
            AddPath(new EllipseGeometry(P(c.Center), c.Radius * k, c.Radius * k), c.Layer, false);
        }

        public void Visit(PArc a)
        {
            double sweep = GeomUtil.NormAngle(a.EndAngle - a.StartAngle);
            var fig = new PathFigure { StartPoint = P(a.Center + Vec2.FromAngle(a.StartAngle) * a.Radius) };
            fig.Segments.Add(new ArcSegment(P(a.Center + Vec2.FromAngle(a.EndAngle) * a.Radius), new Size(a.Radius * k, a.Radius * k), 0,
                sweep > Math.PI, SweepDirection.Counterclockwise, true));
            var g = new PathGeometry();
            g.Figures.Add(fig);
            AddPath(g, a.Layer, false);
        }

        public void Visit(PEllipse e)
        {
            AddPath(new EllipseGeometry(P(e.Center), e.RadiusX * k, e.RadiusY * k), e.Layer, false);
        }

        public void Visit(PDot d)
        {
            double r = Math.Max(1.5, d.Diameter / 2 * k);
            AddPath(new EllipseGeometry(P(d.Center), r, r), d.Layer, true);
        }

        public void Visit(PText t)
        {
            var tb = new TextBlock
            {
                Text = t.Text ?? "",
                FontSize = Math.Max(9, t.Height * k * 1.3),
                Foreground = BrushOf(t.Layer),
                TextAlignment = t.Align == TextAlign.Left ? TextAlignment.Left : t.Align == TextAlign.Center ? TextAlignment.Center : TextAlignment.Right,
            };
            tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var sz = tb.DesiredSize;
            var p = P(t.Position);
            double x = t.Align == TextAlign.Left ? p.X : t.Align == TextAlign.Center ? p.X - sz.Width / 2 : p.X - sz.Width;
            double y = t.VAlign == TextVAlign.Top ? p.Y : t.VAlign == TextVAlign.Bottom ? p.Y - sz.Height : p.Y - sz.Height / 2;
            Canvas.SetLeft(tb, x);
            Canvas.SetTop(tb, y);
            canvas.Children.Add(tb);
        }

        // Sơ đồ ống không sinh kích thước / leader / bảng; nếu sau này có thì vẽ phần chữ cho khỏi mất thông tin.
        public void Visit(PDim d) { }

        public void Visit(PLeader l)
        {
            Visit(new PText { Layer = l.Layer, Position = l.TextPosition, Height = l.TextHeight, Text = l.Text, Align = l.Align });
        }

        public void Visit(PTable t) { }
    }
}
