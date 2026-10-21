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
        /// Ký hiệu cao độ = block LCT_LEVEL có attribute LEVEL ("label RL xx.xxx"): tam giác chỉ xuống tại (x, z),
        /// gạch ngang, chữ phía trên. side = +1 chữ bên phải, -1 bên trái.
        /// </summary>
        public static PBlock Level(DrawingModel m, DrawStyle st, double x, double z, string label, double rl, int side)
        {
            var b = new PBlock
            {
                Layer = Layers.Text, Name = side > 0 ? SymbolLibrary.LevelRight : SymbolLibrary.LevelLeft,
                Position = new Vec2(x, z), Scale = st.Scale,
            };
            b.Attributes[SymbolLibrary.LevelTag] = string.Format("{0} RL {1:0.000}", label, rl);
            return m.Add(b);
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
            /// <summary>Đầu leader theo thứ chỉ vào: mũi tên (mặc định), vòng rỗng = chấm thép, vòng đặc = vùng hatch.</summary>
            public LeaderEnd End = LeaderEnd.Arrow;
            /// <summary>
            /// Điểm đặt nằm trên một nét ngang (thanh thép, đường ống, mặt rãnh...): leader không được tới ngang (trùng hướng nét),
            /// chữ đặt lệch cao độ ≥ DiagFactor × chiều cao chữ để leader vào xiên 45° (hoặc đứng) rồi landing ngang.
            /// </summary>
            public bool Horizontal;
            /// <summary>Ưu tiên đặt chữ PHÍA TRÊN điểm đặt (leader vòng từ trên xuống) — ghi chú lưới thép lớp trên. Mặc định (nét ngang) là phía dưới.</summary>
            public bool Above;
            /// <summary>Cột bắt buộc: −1 trái, +1 phải, 0 = theo vị trí điểm đặt. Cặp ghi chú lớp trên / lớp dưới cùng một bản đặt cùng cột.</summary>
            public int Side;
            /// <summary>Ghi chú của chính đối tượng vật cản (vd ghi chú ống): bỏ qua vật cản chứa điểm đặt (nới 2 mm giấy) khi xét leader.</summary>
            public bool OwnObstacle;
            /// <summary>Điểm đặt thay thế (theo thứ tự ưu tiên) khi leader từ Target không tránh được chữ kích thước — vd điểm khác dọc theo thanh thép.</summary>
            public List<Vec2> AltTargets = new List<Vec2>();
            public Callout(Vec2 target, string text, LeaderEnd end = LeaderEnd.Arrow) { Target = target; Text = text; End = end; }
        }

        /// <summary>Khoảng trống phải giữ quanh số kích thước / nhãn cao độ (theo chiều cao chữ): ghi chú và leader không được sát hơn.</summary>
        public const double ClearanceFactor = 1.2;

        /// <summary>
        /// Hộp chữ của một kích thước (chữ nằm phía DimLinePoint, cách đường kích thước 1 mm giấy, như renderer vẽ),
        /// nới thêm ClearanceFactor × chiều cao chữ mỗi phía. Dùng làm vật cản khi xếp ghi chú: chữ và leader phải ở ngoài hộp này.
        /// </summary>
        public static Rect2 DimTextRect(PDim d, DrawStyle st)
        {
            var dir = Vec2.FromAngle(d.AngleDeg * GeomUtil.Deg);
            var n = dir.Perp();
            var q1 = d.P1 + n * (d.DimLinePoint - d.P1).Dot(n);
            var q2 = d.P2 + n * (d.DimLinePoint - d.P2).Dot(n);
            string text = string.IsNullOrEmpty(d.TextOverride) ? d.Measured.ToString("0", System.Globalization.CultureInfo.InvariantCulture) : d.TextOverride;
            double h = st.TextSmall, w = text.Length * 0.8 * h, pad = ClearanceFactor * h;
            var centre = (q1 + q2) * 0.5 + n * (1.0 * st.Scale + h / 2);
            var b = new Bounds();
            foreach (var sx in new[] { -1, 1 })
                foreach (var sy in new[] { -1, 1 })
                    b.Add(centre + dir * (sx * (w / 2 + pad)) + n * (sy * (h / 2 + pad)));
            return new Rect2(b.MinX, b.MinY, b.MaxX, b.MaxY);
        }

        /// <summary>Hộp chữ (ước lượng) của một PText, nới thêm ClearanceFactor × chiều cao chữ mỗi phía.</summary>
        public static Rect2 TextRect(PText t) { return TextRect(t, ClearanceFactor); }

        /// <summary>Hộp chữ (ước lượng) của một PText, nới thêm padFactor × chiều cao chữ mỗi phía (0 = hộp sát chữ).</summary>
        public static Rect2 TextRect(PText t, double padFactor)
        {
            var b = new Bounds();
            t.AddBounds(b);
            double pad = padFactor * t.Height;
            return new Rect2(b.MinX - pad, b.MinY - pad, b.MaxX + pad, b.MaxY + pad);
        }

        /// <summary>Khoảng hở giữa hai ghi chú liền nhau trong một cột = 1.2 × chiều cao chữ (sát hơn thì khó đọc).</summary>
        public const double CalloutGapFactor = 1.2;
        /// <summary>Ghi chú chỉ vào nét ngang: chữ thấp hơn điểm đặt ít nhất 2 chiều cao chữ để đoạn leader đầu tiên xiên 45°.</summary>
        public const double DiagFactor = 2.0;

        /// <summary>
        /// Xếp một cột ghi chú tại hoành độ xText, tự giãn cách để chữ không đè nhau, kẻ leader tới từng điểm.
        /// side = -1: cột nằm bên trái (chữ căn phải); +1: bên phải.
        /// Leader ngang khi chữ cùng cao độ với điểm đặt; nếu phải dời chữ thì leader gãy khúc: đoạn xiên 45° (hoặc đứng 90°
        /// khi không đủ chỗ) tính từ điểm đặt, rồi landing ngang tới chữ.
        /// Các leader không cắt nhau và không vào vùng vật cản (avoid: hộp chữ kích thước, nhãn cao độ, đã nới khoảng trống):
        /// ứng viên vi phạm bị loại, ghi chú bị đẩy xuống / đổi điểm đặt thay thế. Thử hai thứ tự xếp (theo cao độ điểm đặt,
        /// và theo phương 45° của leader) rồi lấy thứ tự ít vi phạm nhất, hòa thì lấy tổng leader ngắn hơn.
        /// </summary>
        public static void CalloutColumn(DrawingModel m, DrawStyle st, IEnumerable<Callout> items, double xText, int side, IList<Rect2> avoid = null)
        {
            var all = items.Where(c => !string.IsNullOrEmpty(c.Text)).ToList();
            if (all.Count == 0) return;
            double h0 = st.TextSmall;
            // xếp theo cao độ chữ mong muốn (lớp trên: trên điểm đặt; nét ngang: dưới điểm đặt) để cặp trên / dưới nằm đúng thứ tự
            var byHeight = all.OrderByDescending(c => DesiredY(c, h0)).ThenBy(c => -side * c.Target.X).ToList();
            // key tăng dần xuống dưới: với leader 45° cùng chiều, hai leader không cắt nhau khi key của ghi chú dưới >= ghi chú trên
            var bySweep = all.OrderBy(c => -side * c.Target.X - DesiredY(c, h0)).ThenByDescending(c => DesiredY(c, h0)).ToList();

            List<PLeader> best = null; int bestBad = int.MaxValue; double bestLen = double.MaxValue;
            foreach (var order in new[] { byHeight, bySweep })
            {
                int bad; double len;
                var leaders = LayoutColumn(st, order, xText, side, avoid, out bad, out len);
                if (bad < bestBad || (bad == bestBad && len < bestLen - 1e-6)) { best = leaders; bestBad = bad; bestLen = len; }
            }
            foreach (var l in best) m.Add(l);
        }

        private static List<PLeader> LayoutColumn(DrawStyle st, List<Callout> list, double xText, int side, IList<Rect2> avoid,
            out int unresolved, out double totalLength)
        {
            double h = st.TextSmall;
            double gap = CalloutGapFactor * h;
            double? prevBottom = null;
            var placed = new List<List<Vec2>>();
            var blocks = new List<Interval>(); // khối chữ đã xếp trong cột (để chữ đặt phía trên không đè lên)
            var result = new List<PLeader>();
            unresolved = 0; totalLength = 0;
            foreach (var c in list)
            {
                int lines = c.Text.Split('\n').Length;
                double blockH = lines * h * PText.LineSpacing;
                // nét ngang / lớp trên: chữ đặt lệch cao độ so với điểm đặt để leader vào xiên (dưới lên hoặc trên xuống)
                double diagMin = c.Horizontal || c.Above ? DiagFactor * h : 0;
                double y = DesiredY(c, h);
                if (prevBottom != null && y + blockH / 2 > prevBottom.Value - gap)
                    y = prevBottom.Value - gap - blockH / 2;
                double xElbow = xText - side * 1.5 * st.Scale;
                // ghi chú của chính vật cản (ống): bỏ các hộp chứa điểm đặt để leader được đi qua đối tượng của mình
                IList<Rect2> avoidC = avoid;
                if (c.OwnObstacle && avoid != null)
                {
                    double mg = 2 * st.Scale;
                    avoidC = avoid.Where(r => !(c.Target.X >= r.X1 - mg && c.Target.X <= r.X2 + mg && c.Target.Y >= r.Y1 - mg && c.Target.Y <= r.Y2 + mg)).ToList();
                }
                var pts = LeaderPoints(c.Target, xElbow, y, h);
                // ghi chú ưu tiên phía trên mà bị đẩy xuống dưới điểm đặt thì cũng đi tìm chỗ khác (phía trên trước)
                if (Blocked(pts, avoidC, placed) || (c.Above && y < c.Target.Y + diagMin - 1e-6))
                {
                    // Lần lượt với điểm đặt chính rồi các điểm thay thế, đoạn xiên 45° trước rồi mới đoạn đứng 90°:
                    // đẩy chữ xuống từng nửa chiều cao chữ (tối đa 12 chiều cao); ghi chú nét ngang còn được thử đặt chữ
                    // PHÍA TRÊN điểm đặt (leader đi vòng từ trên xuống) ở chỗ cột còn trống. Không cách nào lọt thì giữ
                    // vị trí ban đầu (chấp nhận vi phạm còn hơn chữ chạy đi quá xa).
                    double y0 = y;
                    bool clear = false;
                    var targets = new List<Vec2> { c.Target };
                    targets.AddRange(c.AltTargets);
                    // Thứ tự ưu tiên: phía ưu tiên (trên với Above, dưới với các ghi chú khác) với đoạn xiên 45° rồi đoạn đứng 90°,
                    // sau đó mới sang phía kia (cũng xiên trước, đứng sau).
                    var phases = new List<Tuple<bool, bool>>(); // (up?, diag?)
                    foreach (bool upPhase in c.Above ? new[] { true, false } : new[] { false, true })
                        foreach (bool diag in new[] { true, false })
                            phases.Add(Tuple.Create(upPhase, diag));
                    foreach (var ph in phases)
                    {
                        bool upPhase = ph.Item1, diag = ph.Item2;
                        foreach (var tg in targets)
                        {
                            var cands = new List<double>();
                            if (upPhase)
                                // phía trên điểm đặt (leader vòng từ trên xuống): ghi chú nét ngang / lớp trên từ diagMin, ghi chú khác từ nửa chữ
                                for (int i = 0; i <= 24; i++) cands.Add(tg.Y + Math.Max(diagMin, 0.5 * h) + i * h * 0.5);
                            else
                                for (int i = 0; i <= 24; i++) cands.Add(y0 - i * h * 0.5);
                            foreach (var yi in cands)
                            {
                                if (diagMin > 0 && Math.Abs(yi - tg.Y) < diagMin - 1e-6) continue; // vẫn phải vào xiên
                                if (!Fits(yi, blockH, gap, blocks)) continue;
                                var cand = LeaderPoints(tg, xElbow, yi, h, diag);
                                if (Blocked(cand, avoidC, placed)) continue;
                                y = yi; pts = cand; clear = true;
                                break;
                            }
                            if (clear) break;
                        }
                        if (clear) break;
                    }
                    if (!clear) { y = y0; pts = LeaderPoints(c.Target, xElbow, y0, h); unresolved++; }
                }
                prevBottom = Math.Min(prevBottom ?? double.MaxValue, y - blockH / 2);
                blocks.Add(new Interval(y - blockH / 2, y + blockH / 2));
                placed.Add(pts);
                for (int i = 0; i + 1 < pts.Count; i++) totalLength += (pts[i + 1] - pts[i]).Length;

                result.Add(new PLeader
                {
                    Layer = Layers.Text,
                    Points = pts,
                    TextPosition = new Vec2(xText, y),
                    Text = c.Text, TextHeight = h,
                    Align = side > 0 ? TextAlign.Left : TextAlign.Right,
                    End = c.End,
                });
            }
            return result;
        }

        /// <summary>Cao độ chữ mong muốn: trên điểm đặt (Above), dưới điểm đặt (nét ngang), hoặc ngang điểm đặt.</summary>
        private static double DesiredY(Callout c, double h)
        {
            if (c.Above) return c.Target.Y + DiagFactor * h;
            if (c.Horizontal) return c.Target.Y - DiagFactor * h;
            return c.Target.Y;
        }

        /// <summary>Khối chữ tâm y, cao blockH không đè (kể cả khoảng hở gap) lên khối chữ nào đã xếp trong cột.</summary>
        private static bool Fits(double y, double blockH, double gap, List<Interval> blocks)
        {
            var me = new Interval(y - blockH / 2 - gap, y + blockH / 2 + gap);
            return !blocks.Any(b => b.Overlaps(me));
        }

        private static bool Blocked(List<Vec2> pts, IList<Rect2> avoid, List<List<Vec2>> placed)
        {
            if (avoid != null && LeaderHits(pts, avoid)) return true;
            foreach (var other in placed)
                for (int i = 0; i + 1 < pts.Count; i++)
                    for (int j = 0; j + 1 < other.Count; j++)
                        if (GeomUtil.SegmentsCross(pts[i], pts[i + 1], other[j], other[j + 1])) return true;
            return false;
        }

        /// <summary>
        /// Các đỉnh leader từ điểm đặt tới khuỷu (xElbow, y): ngang nếu cùng cao độ; không thì gãy khúc tại điểm
        /// cách điểm đặt theo đoạn 45°, hoặc đứng 90° khi đoạn 45° không chừa đủ landing ngang (≥ 2 chiều cao chữ).
        /// </summary>
        public static List<Vec2> LeaderPoints(Vec2 target, double xElbow, double y, double h, bool preferDiagonal = true)
        {
            double dy = y - target.Y, dx = xElbow - target.X;
            var pts = new List<Vec2> { target };
            if (Math.Abs(dy) > 1e-6)
            {
                double minLanding = 2 * h;
                bool diag = preferDiagonal && Math.Abs(dy) <= Math.Abs(dx) - minLanding;
                pts.Add(diag ? new Vec2(target.X + Math.Sign(dx) * Math.Abs(dy), y) : new Vec2(target.X, y));
            }
            pts.Add(new Vec2(xElbow, y));
            return pts;
        }

        private static bool LeaderHits(List<Vec2> pts, IList<Rect2> avoid)
        {
            for (int i = 0; i + 1 < pts.Count; i++)
                foreach (var r in avoid)
                    if (GeomUtil.SegmentIntersectsRect(pts[i], pts[i + 1], r)) return true;
            return false;
        }

        /// <summary>
        /// Ký hiệu đường cắt trên mặt bằng: tim mảnh nối hai đầu; mỗi đầu là block LCT_SECTION (nét đậm, mũi tên chỉ hướng nhìn,
        /// attribute LETTER = chữ cái, luôn đứng thẳng).
        /// </summary>
        public static void SectionMarker(DrawingModel m, DrawStyle st, Vec2 a, Vec2 b, Vec2 viewDir, string letter)
        {
            var dir = (b - a).Normalized();
            double seg = 8 * st.Scale;
            m.Add(PPolyline.Line(a + dir * seg, b - dir * seg, Layers.Centre));
            // quay block sao cho +Y của block = hướng nhìn; nét đậm (+X hoặc -X của block) phải hướng về đầu kia
            double rot = viewDir.Angle() - Math.PI / 2;
            var xl = Vec2.FromAngle(rot);
            for (int i = 0; i < 2; i++)
            {
                var p = i == 0 ? a : b;
                var toOther = i == 0 ? dir : -dir;
                var blk = new PBlock
                {
                    Layer = Layers.SectionMark, Name = toOther.Dot(xl) >= 0 ? SymbolLibrary.SectionRight : SymbolLibrary.SectionLeft,
                    Position = p, Scale = st.Scale, Rotation = rot,
                };
                blk.Attributes[SymbolLibrary.SectionTag] = letter;
                m.Add(blk);
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
