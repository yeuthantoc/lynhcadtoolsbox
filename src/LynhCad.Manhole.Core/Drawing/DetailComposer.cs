using System.Collections.Generic;
using LynhCad.Manhole.Core.Geometry;
using LynhCad.Manhole.Core.Model;
using LynhCad.Manhole.Core.Rebar;

namespace LynhCad.Manhole.Core.Drawing
{
    public class ComposeResult
    {
        public ManholeGeometry Geometry;
        public RebarDesign Rebar;
        /// <summary>Null nếu thông số có lỗi.</summary>
        public DrawingModel Model;
        public List<string> Errors { get { return Geometry.Errors; } }
        public List<string> Warnings { get { return Geometry.Warnings; } }
        public bool Ok { get { return Model != null; } }
        /// <summary>Vùng model của từng hình chiếu / bảng (gồm tiêu đề), theo thứ tự đọc — để xếp viewport lên layout.</summary>
        public readonly List<ViewRegion> Views = new List<ViewRegion>();
    }

    /// <summary>Một hình chiếu trong chi tiết đã ghép: tên và hộp bao (toạ độ model).</summary>
    public class ViewRegion
    {
        public string Name;
        public Rect2 Bounds;
        public ViewRegion(string name, Bounds b) { Name = name; Bounds = new Rect2(b.MinX, b.MinY, b.MaxX, b.MaxY); }
    }

    /// <summary>
    /// Ghép các hình chiếu thành một chi tiết hoàn chỉnh:
    ///   hàng 1: MẶT BẰNG | MẶT CẮT A-A
    ///   hàng 2: MẶT BẰNG THÉP BẢN NẮP | BẢNG THỐNG KÊ THÉP + BẢNG HÌNH DẠNG THÉP, GHI CHÚ bên dưới
    ///   cột trái dưới bản nắp: CHI TIẾT KHUNG NẮP (tuỳ chọn, tỉ lệ lớn)
    /// Gốc (0,0) của kết quả = tâm hố trên mặt bằng. Renderer dời tới điểm người dùng chọn.
    /// </summary>
    public static class DetailComposer
    {
        public static ComposeResult Compose(ManholeParams p) { return Compose(p, 0); }

        /// <param name="maxTableHeight">Chiều cao tối đa của bảng hình dạng thép (mm giấy, 0 = không giới hạn) — khi xếp lên khổ giấy.</param>
        public static ComposeResult Compose(ManholeParams p, double maxTableHeight)
        {
            var g = new ManholeGeometry(p);
            var res = new ComposeResult { Geometry = g };
            if (!g.IsValid) return res;

            var st = new DrawStyle(p.Scale);
            var rd = RebarDesigner.Design(g);
            res.Rebar = rd;
            string scaleText = "SCALE 1:" + st.Scale.ToString("0");
            string id = string.IsNullOrEmpty(p.Id) ? "" : " - " + p.Id;
            double gap = st.ViewGap;

            // ---- Hàng 1 ----
            var plan = new PlanBuilder(g, rd, st).Build();
            AddTitleBelow(plan, st, 0, "PLAN" + id, scaleText);
            var bp = plan.GetBounds();

            var sec = new SectionBuilder(g, rd, st).Build();
            AddTitleBelow(sec, st, 0, "SECTION A-A" + id, scaleText);
            var bs = sec.GetBounds();
            sec.Translate(new Vec2(bp.MaxX + gap - bs.MinX, bp.MaxY - bs.MaxY));
            bs = sec.GetBounds();

            var all = new DrawingModel();
            all.AddRange(plan);
            all.AddRange(sec);
            res.Views.Add(new ViewRegion("PLAN", bp));
            res.Views.Add(new ViewRegion("SECTION A-A", bs));

            // ---- Hàng 2 ----
            double row2Top = System.Math.Min(bp.MinY, bs.MinY) - gap;
            double x = bp.MinX;
            double leftBottom = row2Top + gap; // đáy cột trái hàng 2 (dưới mặt bằng)
            if (p.DrawRoofPlan)
            {
                // bản nắp đặt thẳng dưới mặt bằng, cùng tim X (cả hai dựng quanh tâm hố (0,0)) để cạnh bản nắp thẳng hàng với tường
                var roof = new RoofPlanBuilder(g, rd, st).Build();
                AddTitleBelow(roof, st, 0, "ROOF SLAB REINFORCEMENT" + id, scaleText);
                var br = roof.GetBounds();
                roof.Translate(new Vec2(0, row2Top - br.MaxY));
                br = roof.GetBounds();
                all.AddRange(roof);
                res.Views.Add(new ViewRegion("ROOF SLAB REINFORCEMENT", br));
                x = br.MaxX + gap;
                leftBottom = br.MinY;
            }
            // chi tiết khung nắp (tỉ lệ lớn) ở cột trái, dưới mặt bằng thép bản nắp — chỗ còn trống của bản vẽ.
            // Dựng trước bảng / ghi chú để ghi chú chung tránh được nó.
            Bounds bcov = null;
            if (p.DrawCoverDetail)
            {
                var cb = new CoverFrameBuilder(g, rd, st);
                var cov = cb.Build();
                AddTitleBelow(cov, st, 0, "ACCESS COVER & FRAME DETAIL" + id, cb.ScaleText);
                var bc = cov.GetBounds();
                cov.Translate(new Vec2(bp.MinX - bc.MinX, leftBottom - gap - bc.MaxY));
                bcov = cov.GetBounds();
                all.AddRange(cov);
                res.Views.Add(new ViewRegion("ACCESS COVER & FRAME DETAIL", bcov));
            }
            if (p.DrawSchedule)
            {
                var table = ScheduleBuilder.BuildTable(g, rd.Schedule, st);
                var bt = table.GetBounds();
                table.Translate(new Vec2(x - bt.MinX, row2Top - bt.MaxY));
                bt = table.GetBounds();
                all.AddRange(table);
                res.Views.Add(new ViewRegion("BAR SCHEDULE", bt));

                // bảng hình dạng thép bên phải bảng thống kê, cùng mép trên
                var shapes = ShapeScheduleBuilder.Build(g, rd.Schedule, st, maxTableHeight);
                var bsh = shapes.GetBounds();
                shapes.Translate(new Vec2(bt.MaxX + gap / 2 - bsh.MinX, row2Top - bsh.MaxY));
                bsh = shapes.GetBounds();
                all.AddRange(shapes);
                res.Views.Add(new ViewRegion("BAR SHAPES", bsh));

                var notes = ScheduleBuilder.BuildNotes(g, st);
                var bn = notes.GetBounds();
                double nx = bt.MinX - bn.MinX, ny = System.Math.Min(bt.MinY, bsh.MinY) - 10 * st.Scale - bn.MaxY;
                // ghi chú chung không được chồng lên chi tiết khung nắp (cột ghi chú của nó có thể lấn sang cột giữa)
                if (bcov != null && bn.MinX + nx < bcov.MaxX && bn.MaxX + nx > bcov.MinX
                    && bn.MinY + ny < bcov.MaxY && bn.MaxY + ny > bcov.MinY)
                    ny = bcov.MinY - 10 * st.Scale - bn.MaxY;
                notes.Translate(new Vec2(nx, ny));
                all.AddRange(notes);
                res.Views.Add(new ViewRegion("NOTES", notes.GetBounds()));
            }

            all.Messages.AddRange(g.Warnings);
            res.Model = all;
            return res;
        }

        private static void AddTitleBelow(DrawingModel m, DrawStyle st, double cx, string name, string scale)
        {
            var b = m.GetBounds();
            Annot.Title(m, st, cx, b.MinY - 8 * st.Scale, name, scale);
        }
    }
}
