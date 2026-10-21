using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using LynhCad.Manhole.Core.Drawing;
using LynhCad.Manhole.Core.Geometry;

namespace LynhCad.Manhole.Core.Export
{
    /// <summary>
    /// Xuất DrawingModel ra SVG để xem trước / kiểm tra mà không cần AutoCAD.
    /// Chỉ là preview gần đúng (hatch tô màu, font hệ thống), không thay thế bản vẽ DWG.
    /// </summary>
    public static class SvgExporter
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static string Export(DrawingModel model, double scale)
        {
            var b = model.GetBounds();
            double margin = 20 * scale;
            double minX = b.MinX - margin, maxY = b.MaxY + margin;
            double w = b.Width + 2 * margin, h = b.Height + 2 * margin;

            var sb = new StringBuilder();
            sb.AppendFormat(Inv, "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"{0:0.#} {1:0.#} {2:0.#} {3:0.#}\" width=\"{4:0}\" height=\"{5:0}\" font-family=\"Arial, Helvetica, sans-serif\">\n",
                minX, -maxY, w, h, w / scale * 3, h / scale * 3);
            sb.AppendFormat(Inv, "<rect x=\"{0:0.#}\" y=\"{1:0.#}\" width=\"{2:0.#}\" height=\"{3:0.#}\" fill=\"white\"/>\n", minX, -maxY, w, h);

            var writer = new Writer(sb, scale);
            // hatch vẽ trước để nằm dưới đường nét
            foreach (var p in model.Items.OfType<PHatch>()) p.Accept(writer);
            foreach (var p in model.Items.Where(i => !(i is PHatch))) p.Accept(writer);

            sb.Append("</svg>\n");
            return sb.ToString();
        }

        private static string F(double v) { return v.ToString("0.##", Inv); }
        private static string Pt(Vec2 p) { return F(p.X) + "," + F(-p.Y); }

        private static string Color(short aci) { return Layers.AciToHex(aci); }

        private static string PathData(PPolyline pl)
        {
            var sb = new StringBuilder();
            int n = pl.Points.Count;
            if (n == 0) return "";
            sb.Append("M").Append(Pt(pl.Points[0]));
            int segs = pl.Closed ? n : n - 1;
            for (int i = 0; i < segs; i++)
            {
                var a = pl.Points[i];
                var c = pl.Points[(i + 1) % n];
                double bulge = pl.Bulges[i];
                if (Math.Abs(bulge) < 1e-9) { sb.Append(" L").Append(Pt(c)); continue; }
                double theta = 4 * Math.Atan(Math.Abs(bulge));
                double chord = (c - a).Length;
                double r = chord / (2 * Math.Sin(theta / 2));
                int large = theta > Math.PI ? 1 : 0;
                int sweep = bulge > 0 ? 0 : 1; // lật trục y: CCW trong model = chiều góc âm trong SVG → sweep-flag 0
                sb.AppendFormat(Inv, " A{0},{0} 0 {1} {2} {3}", F(r), large, sweep, Pt(c));
            }
            if (pl.Closed) sb.Append(" Z");
            return sb.ToString();
        }

        private static string Esc(string s)
        {
            return (s ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        }

        /// <summary>Ghi từng primitive ra SVG. Một instance cho một lần xuất.</summary>
        private sealed class Writer : IPrimVisitor
        {
            private readonly StringBuilder sb;
            private readonly double scale;
            private readonly Dictionary<string, LayerDef> map = Layers.All.ToDictionary(l => l.Name);

            public Writer(StringBuilder sb, double scale) { this.sb = sb; this.scale = scale; }

            private string Stroke(Prim p, double weightOverride = -1)
            {
                LayerDef def;
                if (!map.TryGetValue(p.Layer ?? "", out def)) def = new LayerDef { Color = 7, Linetype = "Continuous", LineWeight = 25 };
                double lw = (weightOverride > 0 ? weightOverride : def.LineWeight) / 100.0 * scale;
                string dash = "";
                if (def.Linetype == "HIDDEN") dash = string.Format(Inv, " stroke-dasharray=\"{0} {1}\"", F(3 * scale), F(1.5 * scale));
                else if (def.Linetype == "CENTER") dash = string.Format(Inv, " stroke-dasharray=\"{0} {1} {2} {1}\"", F(10 * scale), F(2 * scale), F(2 * scale));
                return string.Format(Inv, "stroke=\"{0}\" stroke-width=\"{1}\" fill=\"none\"{2}", Color(def.Color), F(Math.Max(lw, 0.1 * scale)), dash);
            }

            private string FillColor(Prim p)
            {
                LayerDef def;
                return map.TryGetValue(p.Layer ?? "", out def) ? Color(def.Color) : "#111";
            }

            public void Visit(PHatch h)
            {
                string fill;
                switch ((h.Pattern ?? "").ToUpperInvariant())
                {
                    case "SOLID": fill = FillColor(h); break;
                    case "AR-SAND": fill = "#efe6cc"; break;
                    default: fill = "#dcdcdc"; break;
                }
                string d = string.Join(" ", h.Loops.Select(PathData));
                sb.AppendFormat("<path d=\"{0}\" fill=\"{1}\" fill-rule=\"evenodd\" stroke=\"none\"/>\n", d, fill);
            }

            public void Visit(PPolyline pl)
            {
                sb.AppendFormat("<path d=\"{0}\" {1}/>\n", PathData(pl), Stroke(pl));
            }

            public void Visit(PCircle ci)
            {
                sb.AppendFormat(Inv, "<circle cx=\"{0}\" cy=\"{1}\" r=\"{2}\" {3}/>\n", F(ci.Center.X), F(-ci.Center.Y), F(ci.Radius), Stroke(ci));
            }

            public void Visit(PArc arc)
            {
                var a = arc.Center + Vec2.FromAngle(arc.StartAngle) * arc.Radius;
                var c = arc.Center + Vec2.FromAngle(arc.EndAngle) * arc.Radius;
                double sweep = GeomUtil.NormAngle(arc.EndAngle - arc.StartAngle);
                sb.AppendFormat(Inv, "<path d=\"M{0} A{1},{1} 0 {2} 0 {3}\" {4}/>\n", Pt(a), F(arc.Radius), sweep > Math.PI ? 1 : 0, Pt(c), Stroke(arc));
            }

            public void Visit(PEllipse el)
            {
                sb.AppendFormat(Inv, "<ellipse cx=\"{0}\" cy=\"{1}\" rx=\"{2}\" ry=\"{3}\" {4}/>\n", F(el.Center.X), F(-el.Center.Y), F(el.RadiusX), F(el.RadiusY), Stroke(el));
            }

            public void Visit(PDot dot)
            {
                sb.AppendFormat(Inv, "<circle cx=\"{0}\" cy=\"{1}\" r=\"{2}\" fill=\"{3}\"/>\n", F(dot.Center.X), F(-dot.Center.Y), F(dot.Diameter / 2), FillColor(dot));
            }

            public void Visit(PText t)
            {
                var lines = t.Lines;
                double fs = t.Height / 0.72;
                double lineStep = t.Height * PText.LineSpacing;
                string anchor = t.Align == TextAlign.Left ? "start" : t.Align == TextAlign.Center ? "middle" : "end";
                double blockH = (lines.Length - 1) * lineStep;
                // y (model) của baseline dòng đầu
                double yFirst;
                switch (t.VAlign)
                {
                    case TextVAlign.Top: yFirst = t.Position.Y - t.Height; break;
                    case TextVAlign.Bottom: yFirst = t.Position.Y + blockH; break;
                    default: yFirst = t.Position.Y + blockH / 2 - t.Height / 2; break;
                }
                string rot = Math.Abs(t.Rotation) > 1e-9
                    ? string.Format(Inv, " transform=\"rotate({0} {1} {2})\"", F(-t.Rotation * 180 / Math.PI), F(t.Position.X), F(-t.Position.Y))
                    : "";
                sb.AppendFormat(Inv, "<text x=\"{0}\" y=\"{1}\" font-size=\"{2}\" text-anchor=\"{3}\" fill=\"{4}\"{5}{6}>",
                    F(t.Position.X), F(-yFirst), F(fs), anchor, FillColor(t), t.Underline ? " text-decoration=\"underline\"" : "", rot);
                for (int i = 0; i < lines.Length; i++)
                    sb.AppendFormat(Inv, "<tspan x=\"{0}\" dy=\"{1}\">{2}</tspan>", F(t.Position.X), i == 0 ? "0" : F(lineStep), Esc(lines[i]));
                sb.Append("</text>\n");
            }

            public void Visit(PDim d)
            {
                var dir = Vec2.FromAngle(d.AngleDeg * GeomUtil.Deg);
                var n = dir.Perp();
                var q1 = d.P1 + n * (d.DimLinePoint - d.P1).Dot(n);
                var q2 = d.P2 + n * (d.DimLinePoint - d.P2).Dot(n);
                string s = Stroke(d);
                double exo = 1.5 * scale, exe = 1.5 * scale;
                foreach (var pr in new[] { Tuple.Create(d.P1, q1), Tuple.Create(d.P2, q2) })
                {
                    var v = pr.Item2 - pr.Item1;
                    if (v.Length < 1e-6) continue;
                    var vu = v.Normalized();
                    sb.AppendFormat("<path d=\"M{0} L{1}\" {2}/>\n", Pt(pr.Item1 + vu * exo), Pt(pr.Item2 + vu * exe), s);
                }
                sb.AppendFormat("<path d=\"M{0} L{1}\" {2}/>\n", Pt(q1 - dir * (1.5 * scale) * Math.Sign((q2 - q1).Dot(dir))), Pt(q2 + dir * (1.5 * scale) * Math.Sign((q2 - q1).Dot(dir))), s);
                var tick = (dir + n).Normalized() * (1.2 * scale);
                foreach (var q in new[] { q1, q2 })
                    sb.AppendFormat("<path d=\"M{0} L{1}\" {2}/>\n", Pt(q - tick), Pt(q + tick), Stroke(d, 35));
                string text = string.IsNullOrEmpty(d.TextOverride) ? d.Measured.ToString("0", Inv) : d.TextOverride;
                var mid = (q1 + q2) * 0.5 + n * (1.0 * scale);
                double h = 2.5 * scale;
                string rot = Math.Abs(d.AngleDeg - 90) < 1e-6
                    ? string.Format(Inv, " transform=\"rotate(-90 {0} {1})\"", F(mid.X), F(-mid.Y))
                    : "";
                sb.AppendFormat(Inv, "<text x=\"{0}\" y=\"{1}\" font-size=\"{2}\" text-anchor=\"middle\" fill=\"{3}\"{4}>{5}</text>\n",
                    F(mid.X), F(-mid.Y), F(h / 0.72), FillColor(d), rot, Esc(text));
            }

            public void Visit(PLeader l)
            {
                var pts = new List<Vec2>(l.Points);
                double gapX = l.Align == TextAlign.Left ? -0.8 * scale : 0.8 * scale;
                pts.Add(new Vec2(l.TextPosition.X + gapX, l.TextPosition.Y));
                var path = "M" + string.Join(" L", pts.Select(Pt));
                sb.AppendFormat("<path d=\"{0}\" {1}/>\n", path, Stroke(l));
                if (l.End == LeaderEnd.FilledCircle)
                    sb.AppendFormat(Inv, "<circle cx=\"{0}\" cy=\"{1}\" r=\"{2}\" fill=\"{3}\"/>\n", F(pts[0].X), F(-pts[0].Y), F(0.6 * scale), FillColor(l));
                else if (l.End == LeaderEnd.OpenCircle)
                    sb.AppendFormat(Inv, "<circle cx=\"{0}\" cy=\"{1}\" r=\"{2}\" {3}/>\n", F(pts[0].X), F(-pts[0].Y), F(0.6 * scale), Stroke(l).Replace("fill=\"none\"", "fill=\"white\""));
                else
                {
                    var dir = (pts[1] - pts[0]).Normalized();
                    var side = dir.Perp() * (0.6 * scale);
                    var bp = pts[0] + dir * (2.5 * scale);
                    sb.AppendFormat("<path d=\"M{0} L{1} L{2} Z\" fill=\"{3}\"/>\n", Pt(pts[0]), Pt(bp + side), Pt(bp - side), FillColor(l));
                }
                Visit(new PText { Layer = l.Layer, Position = l.TextPosition, Height = l.TextHeight, Text = l.Text, Align = l.Align });
            }

            public void Visit(PBlock b)
            {
                foreach (var p in SymbolLibrary.Expand(b).Items) p.Accept(this);
            }

            public void Visit(PTable t)
            {
                string s = Stroke(t);
                double x0 = t.Origin.X, y0 = t.Origin.Y, W = t.Width;
                bool hasTitle = !string.IsNullOrEmpty(t.Title);
                int nRows = t.Rows.Count + (hasTitle ? 1 : 0);
                for (int r = 0; r <= nRows; r++)
                {
                    double y = y0 - r * t.RowHeight;
                    sb.AppendFormat("<path d=\"M{0} L{1}\" {2}/>\n", Pt(new Vec2(x0, y)), Pt(new Vec2(x0 + W, y)), s);
                }
                double yTop = hasTitle ? y0 - t.RowHeight : y0;
                sb.AppendFormat("<path d=\"M{0} L{1}\" {2}/>\n", Pt(new Vec2(x0, y0)), Pt(new Vec2(x0, y0 - nRows * t.RowHeight)), s);
                sb.AppendFormat("<path d=\"M{0} L{1}\" {2}/>\n", Pt(new Vec2(x0 + W, y0)), Pt(new Vec2(x0 + W, y0 - nRows * t.RowHeight)), s);
                double x = x0;
                for (int c = 0; c < t.ColumnCount - 1; c++)
                {
                    x += t.ColumnWidths[c];
                    sb.AppendFormat("<path d=\"M{0} L{1}\" {2}/>\n", Pt(new Vec2(x, yTop)), Pt(new Vec2(x, y0 - nRows * t.RowHeight)), s);
                }
                if (hasTitle)
                    Visit(new PText { Layer = t.Layer, Position = new Vec2(x0 + W / 2, y0 - t.RowHeight / 2), Height = t.TextHeight * 1.2, Text = t.Title, Align = TextAlign.Center });
                for (int r = 0; r < t.Rows.Count; r++)
                {
                    double yc = yTop - (r + 0.5) * t.RowHeight;
                    double xc = x0;
                    for (int c = 0; c < t.ColumnCount; c++)
                    {
                        string cell = c < t.Rows[r].Length ? t.Rows[r][c] : "";
                        Visit(new PText { Layer = t.Layer, Position = new Vec2(xc + t.ColumnWidths[c] / 2, yc), Height = t.TextHeight, Text = cell, Align = TextAlign.Center });
                        xc += t.ColumnWidths[c];
                    }
                }
            }
        }
    }
}
