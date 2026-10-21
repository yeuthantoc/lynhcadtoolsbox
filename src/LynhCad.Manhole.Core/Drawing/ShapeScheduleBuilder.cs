using System;
using System.Collections.Generic;
using System.Linq;
using LynhCad.Manhole.Core.Geometry;
using LynhCad.Manhole.Core.Model;
using LynhCad.Manhole.Core.Rebar;

namespace LynhCad.Manhole.Core.Drawing
{
    /// <summary>
    /// BẢNG HÌNH DẠNG THÉP: mỗi số hiệu một ô gồm nhãn (mác, đường kính, số thanh, chiều dài cắt) và hình dạng thanh
    /// (không theo tỉ lệ, chỗ uốn bo cung) với kích thước ghi trực tiếp lên từng đoạn: A/B/C/D, chân cog, đường kính vòng, nối chồng.
    /// Kích thước hình tính theo mm giấy × tỉ lệ (DrawStyle).
    /// </summary>
    public static class ShapeScheduleBuilder
    {
        /// <summary>Số ô trên một hàng.</summary>
        public const int Columns = 2;

        public static DrawingModel Build(ManholeGeometry g, RebarSchedule sch, DrawStyle st)
        {
            var m = new DrawingModel();
            double s = st.Scale;
            double cellW = 78 * s, cellH = 26 * s, labelW = 22 * s;
            double h = st.TextSmall;

            int n = sch.Marks.Count;
            int rows = (n + Columns - 1) / Columns;
            double totalW = Columns * cellW;
            double titleH = 6 * s;

            // khung + tiêu đề
            m.Add(PPolyline.Rect(new Rect2(0, -titleH - rows * cellH, totalW, 0), Layers.Table));
            m.Add(PPolyline.Line(new Vec2(0, -titleH), new Vec2(totalW, -titleH), Layers.Table));
            m.Add(new PText { Layer = Layers.Table, Position = new Vec2(totalW / 2, -titleH / 2), Text = "BAR SHAPES - " + (g.P.Id ?? "MH"), Height = h * 1.2, Align = TextAlign.Center });
            for (int r = 1; r < rows; r++)
            {
                double y = -titleH - r * cellH;
                m.Add(PPolyline.Line(new Vec2(0, y), new Vec2(totalW, y), Layers.Table));
            }
            for (int c = 1; c < Columns; c++)
                m.Add(PPolyline.Line(new Vec2(c * cellW, -titleH), new Vec2(c * cellW, -titleH - rows * cellH), Layers.Table));

            for (int i = 0; i < n; i++)
            {
                var bm = sch.Marks[i];
                int r = i / Columns, c = i % Columns;
                double x0 = c * cellW, y0 = -titleH - r * cellH;   // góc trên-trái ô
                // nhãn
                string label = string.Format("{0}  {1}\n{2} No.\nL = {3:0}", bm.Mark, bm.Size, bm.Count, bm.Length);
                m.Add(new PText { Layer = Layers.Table, Position = new Vec2(x0 + 2 * s, y0 - cellH / 2), Text = label, Height = h, Align = TextAlign.Left, VAlign = TextVAlign.Middle });
                m.Add(PPolyline.Line(new Vec2(x0 + labelW, y0), new Vec2(x0 + labelW, y0 - cellH), Layers.Table));
                // hình dạng trong vùng còn lại của ô
                var area = new Rect2(x0 + labelW + 4 * s, y0 - cellH + 3 * s, x0 + cellW - 4 * s, y0 - 3 * s);
                DrawShape(m, bm, area, st);
            }
            return m;
        }

        /// <summary>Vẽ hình dạng thanh vào vùng area; mọi kích thước là mm giấy × scale.</summary>
        private static void DrawShape(DrawingModel m, BarMark bm, Rect2 area, DrawStyle st)
        {
            double s = st.Scale, h = st.TextSmall * 0.8, r = 1.2 * s;
            double cx = (area.X1 + area.X2) / 2, cy = (area.Y1 + area.Y2) / 2;
            double cog = 5 * s;
            var pts = new List<Vec2>();
            var labels = new List<Tuple<Vec2, Vec2, string>>(); // đoạn (a→b) + chữ ghi bên cạnh đoạn

            switch (bm.Shape)
            {
                case BarShape.Ring:
                {
                    double rad = Math.Min(area.Height / 2 - h, 8 * s);
                    m.Add(new PCircle { Layer = Layers.Rebar, Center = new Vec2(cx, cy), Radius = rad });
                    m.Add(new PText { Layer = Layers.Table, Position = new Vec2(cx, cy), Text = string.Format("Ø{0:0}", bm.A), Height = h, Align = TextAlign.Center });
                    m.Add(new PText { Layer = Layers.Table, Position = new Vec2(cx + rad + 1.5 * s, cy), Text = string.Format("LAP {0:0}", bm.B), Height = h, Align = TextAlign.Left });
                    return;
                }
                case BarShape.L:
                {
                    // A đứng, B chân ngang (thép đứng tường chân L vào bản đáy)
                    double a = Math.Min(area.Height - 2 * h, 14 * s), b = Math.Min(area.Width * 0.5, 18 * s);
                    var p0 = new Vec2(cx - b / 2, cy + a / 2);
                    var p1 = new Vec2(cx - b / 2, cy - a / 2);
                    var p2 = new Vec2(cx + b / 2, cy - a / 2);
                    pts.AddRange(new[] { p0, p1, p2 });
                    labels.Add(Tuple.Create(p0, p1, Num(bm.A)));
                    labels.Add(Tuple.Create(p1, p2, Num(bm.B)));
                    break;
                }
                case BarShape.U:
                {
                    // A, C: hai chân cùng phía; B: đoạn giữa (nằm ngang)
                    double b = Math.Min(area.Width - 10 * s, 34 * s), legLen = Math.Min(area.Height - 2 * h, 9 * s);
                    var p0 = new Vec2(cx - b / 2, cy + legLen / 2);
                    var p1 = new Vec2(cx - b / 2, cy - legLen / 2);
                    var p2 = new Vec2(cx + b / 2, cy - legLen / 2);
                    var p3 = new Vec2(cx + b / 2, cy + legLen / 2);
                    pts.AddRange(new[] { p0, p1, p2, p3 });
                    labels.Add(Tuple.Create(p0, p1, Num(bm.A)));
                    labels.Add(Tuple.Create(p1, p2, Num(bm.B)));
                    labels.Add(Tuple.Create(p3, p2, Num(bm.C)));
                    break;
                }
                case BarShape.Cranked:
                case BarShape.Dropped:
                {
                    bool dropped = bm.Shape == BarShape.Dropped;
                    double drop = 4 * s, run = drop * RebarDesigner.CrankSlope;
                    double w = Math.Min(area.Width - 8 * s, 40 * s);
                    double flats = w - (dropped ? 2 : 1) * run;
                    double aLen = dropped ? flats * 0.3 : flats * 0.5, cLen = dropped ? flats * 0.4 : flats * 0.5, dLen = flats * 0.3;
                    double x = cx - w / 2, yHi = cy + drop / 2, yLo = cy - drop / 2;
                    var p0 = new Vec2(x, yHi); var p1 = new Vec2(x + aLen, yHi);
                    var p2 = new Vec2(p1.X + run, yLo); var p3 = new Vec2(p2.X + cLen, yLo);
                    pts.AddRange(new[] { p0, p1, p2, p3 });
                    labels.Add(Tuple.Create(p0, p1, Num(bm.A)));
                    labels.Add(Tuple.Create(p2, p3, Num(bm.C)));
                    m.Add(new PText { Layer = Layers.Table, Position = new Vec2(p1.X + run / 2 + 0.8 * s, cy + h * 0.9), Text = Num(bm.B), Height = h, Align = TextAlign.Left });
                    if (dropped)
                    {
                        var p4 = new Vec2(p3.X + run, yHi); var p5 = new Vec2(p4.X + dLen, yHi);
                        pts.AddRange(new[] { p4, p5 });
                        labels.Add(Tuple.Create(p4, p5, Num(bm.D)));
                    }
                    break;
                }
                default:
                {
                    double w = Math.Min(area.Width - 8 * s, 40 * s);
                    var p0 = new Vec2(cx - w / 2, cy); var p1 = new Vec2(cx + w / 2, cy);
                    pts.AddRange(new[] { p0, p1 });
                    labels.Add(Tuple.Create(p0, p1, Num(bm.A)));
                    break;
                }
            }

            // cog ở hai đầu (vuông góc với đoạn đầu / cuối, quay lên)
            if (bm.CogStart > 0)
            {
                var d = (pts[1] - pts[0]).Normalized();
                var up = Vec2Up(d);
                pts.Insert(0, pts[0] + up * cog);
                labels.Add(Tuple.Create(pts[0], pts[1], Num(bm.CogStart)));
            }
            if (bm.CogEnd > 0)
            {
                int k = pts.Count - 1;
                var d = (pts[k] - pts[k - 1]).Normalized();
                var up = Vec2Up(d);
                pts.Add(pts[k] + up * cog);
                labels.Add(Tuple.Create(pts[k + 1], pts[k], Num(bm.CogEnd)));
            }

            m.Add(PPolyline.Fillet(pts, r, Layers.Rebar));
            foreach (var lb in labels)
            {
                var a = lb.Item1; var b = lb.Item2;
                var mid = (a + b) * 0.5;
                var dir = (b - a).Normalized();
                bool vertical = Math.Abs(dir.X) < 1e-9;
                // chữ ghi phía trên đoạn ngang, bên trái đoạn đứng (cog: bên ngoài thanh)
                var off = vertical ? new Vec2(-1.2 * s, 0) : new Vec2(0, 0.9 * h);
                if (vertical && mid.X > cx) off = new Vec2(1.2 * s, 0);
                m.Add(new PText
                {
                    Layer = Layers.Table, Position = mid + off, Text = lb.Item3, Height = h,
                    Align = vertical ? (mid.X > cx ? TextAlign.Left : TextAlign.Right) : TextAlign.Center,
                    VAlign = TextVAlign.Middle,
                });
            }
        }

        /// <summary>Hướng "lên" vuông góc với d (cog quay về phía +Y; đoạn đứng thì quay về +X).</summary>
        private static Vec2 Vec2Up(Vec2 d)
        {
            var n = d.Perp();
            if (n.Y < -1e-9 || (Math.Abs(n.Y) < 1e-9 && n.X < 0)) n = n * -1;
            return n;
        }

        private static string Num(double v) { return v.ToString("0"); }
    }
}
