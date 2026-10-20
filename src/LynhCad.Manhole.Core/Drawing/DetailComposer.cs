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
    }

    /// <summary>
    /// Ghép các hình chiếu thành một chi tiết hoàn chỉnh:
    ///   hàng 1: MẶT BẰNG | MẶT CẮT A-A
    ///   hàng 2: MẶT BẰNG THÉP BẢN NẮP | BẢNG THỐNG KÊ THÉP + GHI CHÚ
    /// Gốc (0,0) của kết quả = tâm hố trên mặt bằng. Renderer dời tới điểm người dùng chọn.
    /// </summary>
    public static class DetailComposer
    {
        public static ComposeResult Compose(ManholeParams p)
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

            // ---- Hàng 2 ----
            double row2Top = System.Math.Min(bp.MinY, bs.MinY) - gap;
            double x = bp.MinX;
            if (p.DrawRoofPlan)
            {
                // bản nắp đặt thẳng dưới mặt bằng, cùng tim X (cả hai dựng quanh tâm hố (0,0)) để cạnh bản nắp thẳng hàng với tường
                var roof = new RoofPlanBuilder(g, rd, st).Build();
                AddTitleBelow(roof, st, 0, "ROOF SLAB REINFORCEMENT" + id, scaleText);
                var br = roof.GetBounds();
                roof.Translate(new Vec2(0, row2Top - br.MaxY));
                br = roof.GetBounds();
                all.AddRange(roof);
                x = br.MaxX + gap;
            }
            if (p.DrawSchedule)
            {
                var table = ScheduleBuilder.BuildTable(g, rd.Schedule, st);
                var bt = table.GetBounds();
                table.Translate(new Vec2(x - bt.MinX, row2Top - bt.MaxY));
                bt = table.GetBounds();
                all.AddRange(table);

                var notes = ScheduleBuilder.BuildNotes(g, st);
                var bn = notes.GetBounds();
                notes.Translate(new Vec2(bt.MinX - bn.MinX, bt.MinY - 10 * st.Scale - bn.MaxY));
                all.AddRange(notes);
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
