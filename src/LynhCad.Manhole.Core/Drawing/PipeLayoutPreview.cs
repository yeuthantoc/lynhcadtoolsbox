using System;
using System.Collections.Generic;
using System.Linq;
using LynhCad.Manhole.Core.Geometry;
using LynhCad.Manhole.Core.Model;

namespace LynhCad.Manhole.Core.Drawing
{
    /// <summary>
    /// Sơ đồ mặt bằng gọn để xem vị trí ống trong hộp thoại (tab Pipes) trước khi vẽ:
    /// tường, lỗ thăm, ống (OD + tim + nhãn), ống ra nổi bật, ống có lỗi tô đỏ, đường cắt A-A, hướng tường / góc.
    /// Vẫn dựng được khi thông số có lỗi (đó là lúc cần xem nhất); chỉ bỏ trống khi không tính được hình học.
    /// </summary>
    public static class PipeLayoutPreview
    {
        /// <summary>Layer riêng của preview (không có trong bản vẽ AutoCAD).</summary>
        public const string OutletLayer = "PV-OUTLET";
        public const string ErrorLayer = "PV-ERROR";
        public const string HintLayer = "PV-HINT";

        public static string DisplayColor(string layer)
        {
            switch (layer)
            {
                case OutletLayer: return "#1d4ed8";
                case ErrorLayer: return "#d62828";
                case HintLayer: return "#999999";
            }
            var def = Layers.Find(layer);
            return Layers.AciToHex(def != null ? def.Color : (short)7);
        }

        public class Result
        {
            public DrawingModel Model = new DrawingModel();
            public ManholeGeometry Geometry;
            /// <summary>Chiều cao chữ đã dùng (mm model), để renderer quy ra cỡ chữ màn hình.</summary>
            public double TextHeight;
        }

        public static Result Build(ManholeParams p)
        {
            var res = new Result();
            var g = new ManholeGeometry(p);
            res.Geometry = g;
            var m = res.Model;
            if (g.OuterHalfX <= 0 || g.OuterHalfY <= 0) return res;

            const double stub = 600;
            double ext = Math.Max(g.OuterHalfX, g.OuterHalfY) + stub;
            double h = ext * 0.07;
            res.TextHeight = h;

            // ---- tường (hatch nhạt giữa mặt trong và mặt ngoài) ----
            PPolyline inner, outer;
            if (g.IsRect)
            {
                inner = PPolyline.Rect(Rect2.Centered(0, 0, g.Li, g.Wi), Layers.Concrete);
                outer = PPolyline.Rect(Rect2.Centered(0, 0, g.Lo, g.Wo), Layers.Concrete);
            }
            else
            {
                inner = Circle(g.Ri, Layers.Concrete);
                outer = Circle(g.Ro, Layers.Concrete);
            }
            m.Add(new PHatch { Layer = Layers.Hatch, Pattern = "SOLID", Loops = { Copy(outer, Layers.Hatch), Copy(inner, Layers.Hatch) } });
            m.Add(outer);
            m.Add(inner);

            // ---- lỗ thăm (phía trên, nét khuất) ----
            var o = g.OpeningCenter;
            if (g.OpeningIsCircle)
                m.Add(new PCircle { Layer = Layers.ConcreteHidden, Center = o, Radius = g.OpeningHalfX });
            else
                m.Add(PPolyline.Rect(Rect2.Centered(o.X, o.Y, 2 * g.OpeningHalfX, 2 * g.OpeningHalfY), Layers.ConcreteHidden));

            // ---- hướng: tên tường (hố chữ nhật) hoặc góc (hố tròn), đặt bên trong lòng hố ----
            if (g.IsRect)
            {
                double ix = g.Li / 2 - h, iy = g.Wi / 2 - h;
                Label(m, new Vec2(0, iy), "N", h, TextAlign.Center, HintLayer);
                Label(m, new Vec2(0, -iy), "S", h, TextAlign.Center, HintLayer);
                Label(m, new Vec2(ix, 0), "E", h, TextAlign.Center, HintLayer);
                Label(m, new Vec2(-ix, 0), "W", h, TextAlign.Center, HintLayer);
            }
            else
            {
                double r = g.Ri - h;
                foreach (var deg in new[] { 0, 90, 180, 270 })
                    Label(m, Vec2.FromAngle(deg * GeomUtil.Deg) * r, deg + "°", h * 0.8, TextAlign.Center, HintLayer);
            }

            // ---- ống ----
            foreach (var f in g.Pipes)
            {
                bool bad = g.PipesWithErrors.Contains(f.Spec);
                string layer = bad ? ErrorLayer : f.IsOutlet ? OutletLayer : Layers.Pipe;
                var n = f.Dir.Perp();
                var a = f.InnerPoint;
                var b = f.OuterPoint + f.Dir * stub;
                foreach (var half in new[] { f.OD / 2, -f.OD / 2 })
                    m.Add(PPolyline.Line(a + n * half, b + n * half, layer));
                m.Add(PPolyline.Line(f.AxisPoint, b + f.Dir * (h * 0.5), Layers.Centre));
                // lỗ xuyên tường (khe hở quanh ống)
                var g1 = f.AxisPoint + f.Dir * f.InnerDist;
                var g2 = f.AxisPoint + f.Dir * f.OuterDist;
                var gap = new PPolyline { Layer = layer, Closed = true };
                gap.Add(g1 + n * f.HalfGap).Add(g2 + n * f.HalfGap).Add(g2 - n * f.HalfGap).Add(g1 - n * f.HalfGap);
                m.Add(gap);

                string text = string.Format("{0} {1}\nDN{2:0} IL {3:0.000}", f.Spec.Mark, f.IsOutlet ? "OUT" : "IN", f.DN, f.Spec.InvertLevel);
                // nhãn lệch khỏi trục ống để đường tim / đường cắt A-A không chạy qua chữ:
                // ống nằm ngang → chữ phía trên trục; ống dọc → chữ bên phải trục
                bool horizontal = Math.Abs(f.Dir.X) >= 0.3;
                var tp = b + f.Dir * (h * 0.6);
                if (horizontal)
                    m.Add(new PText { Layer = layer, Position = tp + new Vec2(0, h * 0.4), Text = text, Height = h,
                        Align = f.Dir.X > 0 ? TextAlign.Left : TextAlign.Right, VAlign = TextVAlign.Bottom });
                else
                    m.Add(new PText { Layer = layer, Position = tp + new Vec2(h * 0.4, f.Dir.Y * h), Text = text, Height = h,
                        Align = TextAlign.Left, VAlign = TextVAlign.Middle });
            }

            // ---- đường cắt A-A (dọc ống ra) ----
            if (g.Outlet != null)
            {
                var u = g.SectionDir;
                var origin = g.ViewDir * g.SectionOffset;
                // kéo ra ngoài mọi thứ đã vẽ (kể cả nhãn ống) theo phương cắt
                var bb = m.GetBounds();
                double e = 0;
                foreach (var c in new[] { new Vec2(bb.MinX, bb.MinY), new Vec2(bb.MaxX, bb.MinY), new Vec2(bb.MaxX, bb.MaxY), new Vec2(bb.MinX, bb.MaxY) })
                    e = Math.Max(e, Math.Abs((c - origin).Dot(u)));
                e += h;
                // ký hiệu cắt kiểu mặt bằng: chỉ 2 đoạn ngắn ở hai đầu (ngoài hình), không kẻ xuyên qua chữ
                foreach (int side in new[] { -1, 1 })
                {
                    var end = origin + u * (side * e);
                    m.Add(PPolyline.Line(end, end - u * (side * 2 * h), Layers.SectionMark));
                    Label(m, end + g.ViewDir * (h * 0.9), "A", h, TextAlign.Center, Layers.SectionMark);
                }
            }
            return res;
        }

        private static PPolyline Circle(double r, string layer)
        {
            // 4 cung 90°, polyline đóng (đỉnh cuối nối về đỉnh đầu)
            var pl = new PPolyline { Layer = layer, Closed = true };
            for (int k = 0; k < 4; k++) pl.Add(Vec2.FromAngle(k * Math.PI / 2) * r, GeomUtil.Bulge(Math.PI / 2));
            return pl;
        }

        private static PPolyline Copy(PPolyline src, string layer)
        {
            var c = new PPolyline { Layer = layer, Closed = src.Closed };
            for (int i = 0; i < src.Points.Count; i++) c.Add(src.Points[i], src.Bulges[i]);
            return c;
        }

        private static void Label(DrawingModel m, Vec2 pos, string text, double h, TextAlign align, string layer)
        {
            m.Add(new PText { Layer = layer, Position = pos, Text = text, Height = h, Align = align, VAlign = TextVAlign.Middle });
        }
    }
}
