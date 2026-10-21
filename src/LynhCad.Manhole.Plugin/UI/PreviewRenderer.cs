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

        private void AddPath(Geometry geom, string layer, bool fill, string tip = null)
        {
            var path = new Path { Data = geom };
            if (fill) path.Fill = BrushOf(layer); else Stroke(path, layer);
            if (!string.IsNullOrEmpty(tip)) path.ToolTip = tip;
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
            AddPath(g, pl.Layer, false, pl.Tip);
        }

        public void Visit(PHatch h)
        {
            var g = new PathGeometry { FillRule = FillRule.EvenOdd };
            foreach (var loop in h.Loops) if (loop.Points.Count >= 2) g.Figures.Add(Figure(loop));
            AddPath(g, h.Layer, true, h.Tip);
        }

        public void Visit(PCircle c)
        {
            AddPath(new EllipseGeometry(P(c.Center), c.Radius * k, c.Radius * k), c.Layer, false, c.Tip);
        }

        public void Visit(PArc a)
        {
            double sweep = GeomUtil.NormAngle(a.EndAngle - a.StartAngle);
            var fig = new PathFigure { StartPoint = P(a.Center + Vec2.FromAngle(a.StartAngle) * a.Radius) };
            fig.Segments.Add(new ArcSegment(P(a.Center + Vec2.FromAngle(a.EndAngle) * a.Radius), new Size(a.Radius * k, a.Radius * k), 0,
                sweep > Math.PI, SweepDirection.Counterclockwise, true));
            var g = new PathGeometry();
            g.Figures.Add(fig);
            AddPath(g, a.Layer, false, a.Tip);
        }

        public void Visit(PEllipse e)
        {
            AddPath(new EllipseGeometry(P(e.Center), e.RadiusX * k, e.RadiusY * k), e.Layer, false, e.Tip);
        }

        public void Visit(PDot d)
        {
            double r = Math.Max(1.5, d.Diameter / 2 * k);
            AddPath(new EllipseGeometry(P(d.Center), r, r), d.Layer, true, d.Tip);
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
            if (!string.IsNullOrEmpty(t.Tip)) tb.ToolTip = t.Tip;
            tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var sz = tb.DesiredSize;
            var p = P(t.Position);
            double x = t.Align == TextAlign.Left ? p.X : t.Align == TextAlign.Center ? p.X - sz.Width / 2 : p.X - sz.Width;
            double y = t.VAlign == TextVAlign.Top ? p.Y : t.VAlign == TextVAlign.Bottom ? p.Y - sz.Height : p.Y - sz.Height / 2;
            Canvas.SetLeft(tb, x);
            Canvas.SetTop(tb, y);
            canvas.Children.Add(tb);
        }

        /// <summary>
        /// Kích thước vẽ theo đơn vị pixel (đường gióng, đường kích thước, gạch chéo 45°, chữ 10px) để nhìn được ở mọi tỉ lệ
        /// co giãn; Tip của kích thước thành tooltip trên chữ và trên một dải trong suốt dọc đường kích thước cho dễ rê chuột trúng.
        /// </summary>
        public void Visit(PDim d)
        {
            var dir = Vec2.FromAngle(d.AngleDeg * GeomUtil.Deg);
            var n = dir.Perp();
            var q1 = d.P1 + n * (d.DimLinePoint - d.P1).Dot(n);
            var q2 = d.P2 + n * (d.DimLinePoint - d.P2).Dot(n);
            Point a1 = P(d.P1), a2 = P(d.P2), b1 = P(q1), b2 = P(q2);
            const double gap = 2, over = 3, tick = 3.5, fontSize = 10;

            var g = new PathGeometry();
            foreach (var pr in new[] { new[] { a1, b1 }, new[] { a2, b2 } })
            {
                var v = pr[1] - pr[0];
                if (v.Length < 0.5) continue;
                v.Normalize();
                var f = new PathFigure { StartPoint = pr[0] + v * gap };
                f.Segments.Add(new LineSegment(pr[1] + v * over, true));
                g.Figures.Add(f);
            }
            var dp = b2 - b1;
            if (dp.Length < 0.5) return;
            dp.Normalize();
            var np = new Vector(-dp.Y, dp.X);
            var line = new PathFigure { StartPoint = b1 - dp * over };
            line.Segments.Add(new LineSegment(b2 + dp * over, true));
            g.Figures.Add(line);
            var t = dp + np; t.Normalize(); t *= tick;
            foreach (var q in new[] { b1, b2 })
            {
                var f = new PathFigure { StartPoint = q - t };
                f.Segments.Add(new LineSegment(q + t, true));
                g.Figures.Add(f);
            }
            AddPath(g, d.Layer, false, d.Tip);

            // dải trong suốt để tooltip bắt được khi rê chuột gần đường kích thước
            if (!string.IsNullOrEmpty(d.Tip))
            {
                var hit = new Path { Data = new LineGeometry(b1, b2), Stroke = Brushes.Transparent, StrokeThickness = 10, ToolTip = d.Tip };
                canvas.Children.Add(hit);
            }

            string text = string.IsNullOrEmpty(d.TextOverride) ? d.Measured.ToString("0") : d.TextOverride;
            var tb = new TextBlock { Text = text, FontSize = fontSize, Foreground = BrushOf(d.Layer) };
            if (!string.IsNullOrEmpty(d.Tip)) tb.ToolTip = d.Tip;
            bool vertical = Math.Abs(Math.Abs(dp.Y) - 1) < 1e-6;
            if (vertical) tb.LayoutTransform = new RotateTransform(-90);
            tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var sz = tb.DesiredSize;
            // chữ nằm phía DimLinePoint so với P1/P2 (phía n trong model = phía -np hay +np tuỳ hướng), cách đường 2px
            var mid = new Point((b1.X + b2.X) / 2, (b1.Y + b2.Y) / 2);
            var side = P(d.DimLinePoint + n) - P(d.DimLinePoint);
            if (side.Length > 1e-9) side.Normalize(); else side = np;
            var centre = mid + side * (2 + (vertical ? sz.Width : sz.Height) / 2);
            Canvas.SetLeft(tb, centre.X - sz.Width / 2);
            Canvas.SetTop(tb, centre.Y - sz.Height / 2);
            canvas.Children.Add(tb);
        }

        public void Visit(PLeader l)
        {
            Visit(new PText { Layer = l.Layer, Position = l.TextPosition, Height = l.TextHeight, Text = l.Text, Align = l.Align, Tip = l.Tip });
        }

        public void Visit(PTable t) { }

        public void Visit(PBlock b)
        {
            foreach (var p in SymbolLibrary.Expand(b).Items) p.Accept(this);
        }
    }
}
