using System;
using System.Collections.Generic;
using System.Linq;
using LynhCad.Manhole.Core.Geometry;

namespace LynhCad.Manhole.Core.Drawing
{
    /// <summary>Các tiện ích ghi chú dùng chung: kích thước, tiêu đề, cao độ, ghi chú thép, ký hiệu cắt.</summary>
    public static class Annot
    {
        public static PDim Dim(DrawingModel m, Vec2 p1, Vec2 p2, double angleDeg, Vec2 dimLinePoint, string textOverride = "")
        {
            return m.Add(new PDim { Layer = Layers.Dim, P1 = p1, P2 = p2, AngleDeg = angleDeg, DimLinePoint = dimLinePoint, TextOverride = textOverride ?? "" });
        }

        /// <summary>Kích thước ngang đặt ở cao độ y.</summary>
        public static PDim DimH(DrawingModel m, double x1, double x2, double yRef, double yLine, string over = "")
        {
            return Dim(m, new Vec2(x1, yRef), new Vec2(x2, yRef), 0, new Vec2((x1 + x2) / 2, yLine), over);
        }

        /// <summary>Kích thước đứng đặt ở hoành độ x.</summary>
        public static PDim DimV(DrawingModel m, double y1, double y2, double xRef, double xLine, string over = "")
        {
            return Dim(m, new Vec2(xRef, y1), new Vec2(xRef, y2), 90, new Vec2(xLine, (y1 + y2) / 2), over);
        }

        public static PText Text(DrawingModel m, Vec2 pos, string text, double h, TextAlign align = TextAlign.Left, string layer = Layers.Text)
        {
            return m.Add(new PText { Layer = layer, Position = pos, Text = text, Height = h, Align = align });
        }

        /// <summary>Tiêu đề hình chiếu kiểu AS 1100: tên gạch chân + tỉ lệ bên dưới, căn giữa tại (cx, yTop).</summary>
        public static void Title(DrawingModel m, DrawStyle st, double cx, double yTop, string name, string scaleText)
        {
            m.Add(new PText { Layer = Layers.Title, Position = new Vec2(cx, yTop - st.TextTitle / 2), Text = name, Height = st.TextTitle, Align = TextAlign.Center, Underline = true });
            m.Add(new PText { Layer = Layers.Text, Position = new Vec2(cx, yTop - st.TextTitle - st.TextSubtitle * 1.4), Text = scaleText, Height = st.TextSubtitle, Align = TextAlign.Center });
        }

        /// <summary>
        /// Ký hiệu cao độ: tam giác chỉ xuống tại (x, z), gạch ngang, chữ "label RL xx.xxx".
        /// side = +1 chữ bên phải, -1 bên trái.
        /// </summary>
        public static void Level(DrawingModel m, DrawStyle st, double x, double z, string label, double rl, int side, double lineLength)
        {
            double t = 1.8 * st.Scale;
            var tri = new PPolyline { Layer = Layers.Text, Closed = true };
            tri.Add(new Vec2(x, z)).Add(new Vec2(x - t * 0.6, z + t)).Add(new Vec2(x + t * 0.6, z + t));
            m.Add(tri);
            m.Add(new PHatch { Layer = Layers.Text, Pattern = "SOLID", Loops = { Clone(tri) } });
            double xEnd = x + side * lineLength;
            m.Add(PPolyline.Line(new Vec2(x, z + t), new Vec2(xEnd, z + t), Layers.Text));
            m.Add(new PText
            {
                Layer = Layers.Text, Height = st.TextSmall, VAlign = TextVAlign.Bottom,
                Position = new Vec2(x + side * t * 1.2, z + t * 1.4),
                Align = side > 0 ? TextAlign.Left : TextAlign.Right,
                Text = string.Format("{0} RL {1:0.000}", label, rl),
            });
        }

        private static PPolyline Clone(PPolyline p)
        {
            var c = new PPolyline { Layer = p.Layer, Closed = p.Closed };
            for (int i = 0; i < p.Points.Count; i++) c.Add(p.Points[i], p.Bulges[i]);
            return c;
        }

        public class Callout
        {
            public Vec2 Target;
            public string Text;
            public bool Dot = true;
            public Callout(Vec2 target, string text, bool dot = true) { Target = target; Text = text; Dot = dot; }
        }

        /// <summary>
        /// Hộp chữ của một kích thước (chữ nằm phía DimLinePoint, cách đường kích thước 1 mm giấy, như renderer vẽ),
        /// nới thêm 0.4 chiều cao chữ mỗi phía. Dùng làm vật cản khi xếp ghi chú để leader không cắt qua số kích thước.
        /// </summary>
        public static Rect2 DimTextRect(PDim d, DrawStyle st)
        {
            var dir = Vec2.FromAngle(d.AngleDeg * GeomUtil.Deg);
            var n = dir.Perp();
            var q1 = d.P1 + n * (d.DimLinePoint - d.P1).Dot(n);
            var q2 = d.P2 + n * (d.DimLinePoint - d.P2).Dot(n);
            string text = string.IsNullOrEmpty(d.TextOverride) ? d.Measured.ToString("0", System.Globalization.CultureInfo.InvariantCulture) : d.TextOverride;
            double h = st.TextSmall, w = text.Length * 0.8 * h, pad = 0.4 * h;
            var centre = (q1 + q2) * 0.5 + n * (1.0 * st.Scale + h / 2);
            var b = new Bounds();
            foreach (var sx in new[] { -1, 1 })
                foreach (var sy in new[] { -1, 1 })
                    b.Add(centre + dir * (sx * (w / 2 + pad)) + n * (sy * (h / 2 + pad)));
            return new Rect2(b.MinX, b.MinY, b.MaxX, b.MaxY);
        }

        /// <summary>Hộp chữ (ước lượng) của một PText, nới thêm 0.4 chiều cao chữ mỗi phía.</summary>
        public static Rect2 TextRect(PText t)
        {
            var b = new Bounds();
            t.AddBounds(b);
            double pad = 0.4 * t.Height;
            return new Rect2(b.MinX - pad, b.MinY - pad, b.MaxX + pad, b.MaxY + pad);
        }

        /// <summary>
        /// Xếp một cột ghi chú tại hoành độ xText, tự giãn cách để chữ không đè nhau, kẻ leader tới từng điểm.
        /// side = -1: cột nằm bên trái (chữ căn phải); +1: bên phải.
        /// avoid: các hộp chữ (số kích thước, nhãn cao độ...) mà leader không được cắt qua — ghi chú bị đẩy xuống
        /// cho tới khi leader đi lọt (chỉ đẩy xuống, nên thứ tự cột vẫn giữ nguyên).
        /// </summary>
        public static void CalloutColumn(DrawingModel m, DrawStyle st, IEnumerable<Callout> items, double xText, int side, IList<Rect2> avoid = null)
        {
            var list = items.Where(c => !string.IsNullOrEmpty(c.Text)).OrderByDescending(c => c.Target.Y).ToList();
            if (list.Count == 0) return;
            double h = st.TextSmall;
            double? prevBottom = null;
            foreach (var c in list)
            {
                int lines = c.Text.Split('\n').Length;
                double blockH = lines * h * 1.5;
                double y = c.Target.Y;
                if (prevBottom != null && y + blockH / 2 > prevBottom.Value - h * 0.8)
                    y = prevBottom.Value - h * 0.8 - blockH / 2;
                double xElbow = xText - side * 1.5 * st.Scale;
                if (avoid != null)
                {
                    for (int i = 0; i < 60 && LeaderHits(c.Target, new Vec2(xElbow, y), avoid); i++)
                        y -= h * 0.5;
                }
                prevBottom = y - blockH / 2;

                var elbow = new Vec2(xElbow, y);
                m.Add(new PLeader
                {
                    Layer = Layers.Text,
                    Points = new List<Vec2> { c.Target, elbow },
                    TextPosition = new Vec2(xText, y),
                    Text = c.Text, TextHeight = h,
                    Align = side > 0 ? TextAlign.Left : TextAlign.Right,
                    DotArrow = c.Dot,
                });
            }
        }

        private static bool LeaderHits(Vec2 target, Vec2 elbow, IList<Rect2> avoid)
        {
            foreach (var r in avoid)
                if (GeomUtil.SegmentIntersectsRect(target, elbow, r)) return true;
            return false;
        }

        /// <summary>
        /// Ký hiệu đường cắt trên mặt bằng: nét đậm ở hai đầu, mũi tên chỉ hướng nhìn, chữ cái.
        /// </summary>
        public static void SectionMarker(DrawingModel m, DrawStyle st, Vec2 a, Vec2 b, Vec2 viewDir, string letter)
        {
            var dir = (b - a).Normalized();
            double seg = 8 * st.Scale;
            m.Add(PPolyline.Line(a, a + dir * seg, Layers.SectionMark));
            m.Add(PPolyline.Line(b, b - dir * seg, Layers.SectionMark));
            m.Add(PPolyline.Line(a + dir * seg, b - dir * seg, Layers.Centre));
            foreach (var p in new[] { a, b })
            {
                var tip = p + viewDir * (6 * st.Scale);
                var arrow = new PPolyline { Layer = Layers.SectionMark, Closed = true };
                var side = dir * (1.2 * st.Scale);
                var baseP = p + viewDir * (3 * st.Scale);
                arrow.Add(tip).Add(baseP + side).Add(baseP - side);
                m.Add(arrow);
                m.Add(new PHatch { Layer = Layers.SectionMark, Pattern = "SOLID", Loops = { Clone(arrow) } });
                m.Add(PPolyline.Line(p, baseP, Layers.SectionMark));
                var tp = p - viewDir * (3.5 * st.Scale);
                m.Add(new PText { Layer = Layers.Title, Position = tp, Text = letter, Height = st.TextTitle, Align = TextAlign.Center });
            }
        }

        /// <summary>Ký hiệu cắt ngắn (zigzag) ngang qua đầu ống từ p1 tới p2.</summary>
        public static void BreakLine(DrawingModel m, Vec2 p1, Vec2 p2, double size, string layer)
        {
            var d = p2 - p1;
            var u = d.Normalized();
            var n = u.Perp();
            var mid = p1 + d * 0.5;
            var pl = new PPolyline { Layer = layer };
            pl.Add(p1 - u * size * 0.5);
            pl.Add(mid - u * size * 0.5);
            pl.Add(mid - u * size * 0.25 + n * size * 0.5);
            pl.Add(mid + u * size * 0.25 - n * size * 0.5);
            pl.Add(mid + u * size * 0.5);
            pl.Add(p2 + u * size * 0.5);
            m.Add(pl);
        }
    }
}
