using System;
using System.Collections.Generic;
using System.Linq;
using LynhCad.Manhole.Core.Geometry;
using LynhCad.Manhole.Core.Model;

namespace LynhCad.Manhole.Core.Drawing
{
    /// <summary>Một viewport trên tờ giấy: nhìn vào ModelWindow (toạ độ model, trước khi dời tới điểm chọn) ở tỉ lệ của plan.</summary>
    public class SheetViewport
    {
        public string Name;
        public Rect2 ModelWindow;
        /// <summary>Vị trí trên giấy (mm, gốc = góc trái dưới tờ giấy).</summary>
        public Rect2 Paper;
        /// <summary>Thứ tự của hình chiếu trong chi tiết (thứ tự đọc).</summary>
        public int Order;
    }

    /// <summary>Một tờ (một layout): khung tên và các viewport.</summary>
    public class SheetPage
    {
        public int Number;
        public readonly List<SheetViewport> Viewports = new List<SheetViewport>();
        /// <summary>Khung tên chèn tại (0,0) giấy, attribute đã điền (SHEET = "n OF N").</summary>
        public PBlock TitleBlock;
    }

    /// <summary>Kết quả xếp chi tiết lên khổ giấy.</summary>
    public class SheetPlan
    {
        /// <summary>Null nếu Sheet = None.</summary>
        public SheetFormat Format;
        /// <summary>Mẫu số tỉ lệ đã dùng để dựng chi tiết (có thể khác Params.Scale nếu một hình chiếu to hơn cả vùng vẽ).</summary>
        public double Scale;
        /// <summary>Chi tiết dựng ở tỉ lệ Scale (toạ độ model, gốc = tâm hố). Compose.Ok = false nếu thông số lỗi.</summary>
        public ComposeResult Compose;
        public readonly List<SheetPage> Pages = new List<SheetPage>();
        /// <summary>Mọi hình chiếu vừa vùng vẽ ở tỉ lệ Scale.</summary>
        public bool Fits;
        public readonly List<string> Warnings = new List<string>();
        public bool HasSheet { get { return Format != null; } }
        /// <summary>Chiều cao tối đa bảng hình dạng thép đã dùng khi dựng (mm giấy) — để dựng lại giống hệt.</summary>
        public double MaxTableHeight;
    }

    /// <summary>
    /// Xếp chi tiết hố ga lên các layout A1 / A3. Mỗi hình chiếu / bảng là một viewport riêng cùng tỉ lệ; các viewport xếp
    /// theo hàng từ trên xuống, hết chỗ thì sang tờ mới (mỗi tờ một khung tên "SHEET n OF N").
    /// Chỉ giảm tỉ lệ (dựng lại chi tiết ở tỉ lệ chuẩn kế tiếp để chữ vẫn đúng cỡ) khi có hình chiếu to hơn cả vùng vẽ.
    /// </summary>
    public static class SheetComposer
    {
        /// <summary>Tỉ lệ chuẩn (mẫu số) thử lần lượt.</summary>
        public static readonly double[] StandardScales = { 5, 10, 20, 25, 50, 100, 200, 250, 500 };

        /// <summary>Lề quanh mỗi hình chiếu trong viewport, và khoảng cách giữa các viewport (mm giấy).</summary>
        public const double ViewMargin = 3, ViewGap = 8;

        public static SheetPlan Plan(ManholeParams p, string date)
        {
            var plan = new SheetPlan { Format = SheetFormats.Get(p.Sheet) };
            double baseScale = p.Scale > 0 ? p.Scale : 20;
            if (!plan.HasSheet)
            {
                plan.Scale = baseScale;
                plan.Compose = DetailComposer.Compose(p);
                return plan;
            }

            var candidates = p.FitScaleToSheet
                ? new[] { baseScale }.Concat(StandardScales.Where(s => s > baseScale + 1e-9)).ToList()
                : new List<double> { baseScale };
            var area = plan.Format.DrawArea;
            // bảng hình dạng thép cao quá vùng vẽ thì chia thêm cột
            plan.MaxTableHeight = area.Height - 2 * ViewMargin - 1;
            foreach (var s in candidates)
            {
                var q = p.Clone();
                q.Scale = s;
                plan.Scale = s;
                plan.Compose = DetailComposer.Compose(q, plan.MaxTableHeight);
                if (!plan.Compose.Ok) return plan; // lỗi thông số không phụ thuộc tỉ lệ
                plan.Fits = plan.Compose.Views.All(v => Window(v, s).Width / s <= area.Width + 1e-6 && Window(v, s).Height / s <= area.Height + 1e-6);
                if (plan.Fits) break;
            }

            Pack(plan);

            if (Math.Abs(plan.Scale - baseScale) > 1e-9)
                plan.Warnings.Add(string.Format("Có hình chiếu ở 1:{0:0} lớn hơn vùng vẽ khổ {1} — đã vẽ ở 1:{2:0}.", baseScale, p.Sheet, plan.Scale));
            if (!plan.Fits)
                plan.Warnings.Add(string.Format("Có hình chiếu không vừa khổ {0} kể cả ở 1:{1:0} — viewport tràn khung, hãy chọn khổ lớn hơn.", p.Sheet, plan.Scale));

            int n = plan.Pages.Count;
            foreach (var page in plan.Pages)
            {
                var attrs = SheetFormats.Attributes(p, plan.Format, plan.Scale, date);
                attrs[SheetFormats.TagSheet] = string.Format("{0} OF {1}", page.Number, n);
                // trang có chi tiết khung nắp (tỉ lệ riêng) → "AS SHOWN"
                if (page.Viewports.Any(v => v.Name.StartsWith("ACCESS COVER"))) attrs[SheetFormats.TagScale] = "AS SHOWN";
                page.TitleBlock = new PBlock { Layer = Layers.Sheet, Name = plan.Format.BlockName, Position = Vec2.Zero, Scale = 1, Attributes = attrs };
            }
            return plan;
        }

        private static Rect2 Window(ViewRegion v, double scale)
        {
            double m = ViewMargin * scale;
            return new Rect2(v.Bounds.X1 - m, v.Bounds.Y1 - m, v.Bounds.X2 + m, v.Bounds.Y2 + m);
        }

        /// <summary>
        /// Xếp viewport lên các tờ: thử vài thứ tự (thứ tự đọc, cao trước, rộng trước, diện tích lớn trước); với mỗi thứ tự,
        /// mỗi hình chiếu đặt vào chỗ trống CAO nhất rồi TRÁI nhất của tờ đang mở (vị trí ứng viên ghép từ mép vùng vẽ và
        /// mép các viewport đã đặt), không còn chỗ thì sang tờ mới. Chọn cách ít tờ nhất (hoà thì ưu tiên thứ tự đọc).
        /// Trong mỗi tờ, viewport sắp lại theo thứ tự đọc; nội dung canh giữa theo chiều ngang.
        /// </summary>
        private static void Pack(SheetPlan plan)
        {
            var area = plan.Format.DrawArea;
            double s = plan.Scale;
            var items = plan.Compose.Views.Select((v, i) => new { v, i, win = Window(v, s) })
                .Select(x => new { x.v, x.i, x.win, w = x.win.Width / s, h = x.win.Height / s }).ToList();
            var orders = new[]
            {
                items.ToList(),
                items.OrderByDescending(x => x.h).ThenBy(x => x.i).ToList(),
                items.OrderByDescending(x => x.w).ThenBy(x => x.i).ToList(),
                items.OrderByDescending(x => x.w * x.h).ThenBy(x => x.i).ToList(),
            };
            List<SheetPage> best = null;
            foreach (var order in orders)
            {
                var pages = new List<SheetPage>();
                SheetPage page = null;
                foreach (var it in order)
                {
                    Rect2? spot = page == null ? (Rect2?)null : FindSpot(page, area, it.w, it.h);
                    if (spot == null)
                    {
                        page = new SheetPage { Number = pages.Count + 1 };
                        pages.Add(page);
                        spot = new Rect2(area.X1, area.Y2 - it.h, area.X1 + it.w, area.Y2); // to hơn vùng vẽ thì tràn (đã cảnh báo)
                    }
                    page.Viewports.Add(new SheetViewport { Name = it.v.Name, ModelWindow = it.win, Paper = spot.Value, Order = it.i });
                }
                if (best == null || pages.Count < best.Count) best = pages;
            }
            foreach (var pg in best)
            {
                ReorderRows(pg);
                var sorted = pg.Viewports.OrderBy(v => v.Order).ToList();
                pg.Viewports.Clear();
                pg.Viewports.AddRange(sorted);
                double left = pg.Viewports.Min(v => v.Paper.X1), right = pg.Viewports.Max(v => v.Paper.X2);
                double dx = (area.X1 + area.X2) / 2 - (left + right) / 2;
                for (int i = 0; i < pg.Viewports.Count; i++)
                {
                    var r = pg.Viewports[i].Paper;
                    pg.Viewports[i].Paper = new Rect2(r.X1 + dx, r.Y1, r.X2 + dx, r.Y2);
                }
            }
            // tờ đầu tiên chứa hình chiếu đầu tiên (mặt bằng)
            best = best.OrderBy(pg => pg.Viewports.Min(v => v.Order)).ToList();
            for (int i = 0; i < best.Count; i++) best[i].Number = i + 1;
            plan.Pages.AddRange(best);
        }

        /// <summary>
        /// Các viewport cùng mép trên, nối tiếp nhau trên một hàng: sắp lại trái → phải theo thứ tự đọc (vd MẶT BẰNG trước
        /// MẶT CẮT), giữ khoảng cách; nếu sắp lại làm chồng lên viewport khác thì giữ nguyên.
        /// </summary>
        private static void ReorderRows(SheetPage pg)
        {
            foreach (var grp in pg.Viewports.GroupBy(v => Math.Round(v.Paper.Y2, 3)).Where(gr => gr.Count() > 1).ToList())
            {
                var row = grp.OrderBy(v => v.Paper.X1).ToList();
                bool contiguous = true;
                for (int i = 1; i < row.Count; i++)
                    if (Math.Abs(row[i].Paper.X1 - (row[i - 1].Paper.X2 + ViewGap)) > 1e-3) contiguous = false;
                if (!contiguous || row.Select(v => v.Order).SequenceEqual(row.Select(v => v.Order).OrderBy(o => o))) continue;
                var old = row.Select(v => v.Paper).ToList();
                double x = row[0].Paper.X1;
                foreach (var v in row.OrderBy(v => v.Order).ToList())
                {
                    var r = v.Paper;
                    v.Paper = new Rect2(x, r.Y1, x + r.Width, r.Y2);
                    x += r.Width + ViewGap;
                }
                bool clash = row.Any(a => pg.Viewports.Where(o => !row.Contains(o)).Any(o =>
                    a.Paper.X1 < o.Paper.X2 + ViewGap - 1e-6 && a.Paper.X2 > o.Paper.X1 - ViewGap + 1e-6 &&
                    a.Paper.Y1 < o.Paper.Y2 + ViewGap - 1e-6 && a.Paper.Y2 > o.Paper.Y1 - ViewGap + 1e-6));
                if (clash) for (int i = 0; i < row.Count; i++) row[i].Paper = old[i];
            }
        }

        private static Rect2? FindSpot(SheetPage page, Rect2 area, double w, double h)
        {
            var xs = new List<double> { area.X1 };
            var ys = new List<double> { area.Y2 };
            foreach (var vp in page.Viewports)
            {
                xs.Add(vp.Paper.X1); xs.Add(vp.Paper.X2 + ViewGap);
                ys.Add(vp.Paper.Y2); ys.Add(vp.Paper.Y1 - ViewGap);
            }
            foreach (var top in ys.Distinct().OrderByDescending(y => y))
                foreach (var x in xs.Distinct().OrderBy(x => x))
                {
                    var r = new Rect2(x, top - h, x + w, top);
                    if (r.X2 > area.X2 + 1e-6 || r.Y1 < area.Y1 - 1e-6 || r.Y2 > area.Y2 + 1e-6) continue;
                    bool clash = page.Viewports.Any(o => r.X1 < o.Paper.X2 + ViewGap - 1e-6 && r.X2 > o.Paper.X1 - ViewGap + 1e-6
                                                      && r.Y1 < o.Paper.Y2 + ViewGap - 1e-6 && r.Y2 > o.Paper.Y1 - ViewGap + 1e-6);
                    if (!clash) return r;
                }
            return null;
        }

        /// <summary>
        /// Hình các tờ giấy (mm giấy) để xem trước, xếp chồng từ trên xuống: khung tên + nội dung từng viewport thu theo tỉ lệ
        /// + khung viewport. Không dùng cho AutoCAD (ở đó chi tiết nằm trong model space, viewport nhìn vào).
        /// </summary>
        public static DrawingModel PaperPreview(SheetPlan plan)
        {
            var all = new DrawingModel();
            if (!plan.HasSheet || plan.Compose == null || !plan.Compose.Ok) return all;
            double s = plan.Scale;
            for (int k = 0; k < plan.Pages.Count; k++)
            {
                var page = plan.Pages[k];
                var m = new DrawingModel();
                m.Add(new PBlock { Layer = page.TitleBlock.Layer, Name = page.TitleBlock.Name, Position = Vec2.Zero, Scale = 1,
                    Attributes = new Dictionary<string, string>(page.TitleBlock.Attributes) });
                m.Add(PPolyline.Rect(new Rect2(0, 0, plan.Format.Width, plan.Format.Height), Layers.Viewport)); // mép tờ giấy
                foreach (var vp in page.Viewports)
                {
                    // dựng lại để không đụng model của plan; lấy primitive có tâm hộp bao nằm trong cửa sổ viewport
                    var q = plan.Compose.Geometry.P.Clone();
                    q.Scale = s;
                    var w = vp.ModelWindow;
                    var t = new Similarity(new Vec2(vp.Paper.X1 - w.X1 / s, vp.Paper.Y1 - w.Y1 / s), 1 / s, 0);
                    foreach (var it in DetailComposer.Compose(q, plan.MaxTableHeight).Model.Items)
                    {
                        var b = new Bounds(); it.AddBounds(b);
                        // kích thước: giữ số đo thật trước khi thu nhỏ
                        var dim = it as PDim;
                        if (dim != null && string.IsNullOrEmpty(dim.TextOverride)) dim.TextOverride = dim.Measured.ToString("0");
                        if (!b.IsValid || b.CenterX < w.X1 || b.CenterX > w.X2 || b.CenterY < w.Y1 || b.CenterY > w.Y2) continue;
                        it.Transform(t);
                        m.Add(it);
                    }
                    m.Add(PPolyline.Rect(vp.Paper, Layers.Viewport));
                }
                m.Translate(new Vec2(0, -k * (plan.Format.Height + 20)));
                all.AddRange(m);
            }
            return all;
        }
    }
}
