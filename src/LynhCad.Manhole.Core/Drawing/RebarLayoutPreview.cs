using System;
using System.Collections.Generic;
using LynhCad.Manhole.Core.Geometry;
using LynhCad.Manhole.Core.Model;
using LynhCad.Manhole.Core.Rebar;

namespace LynhCad.Manhole.Core.Drawing
{
    /// <summary>
    /// Sơ đồ bố trí thép để xem trong hộp thoại (tab Reinforcement) trước khi vẽ: mặt bằng (trên) và mặt cắt A-A (dưới)
    /// rút gọn (bê tông, ống, thép) + chú giải các bộ thép bên dưới. Không có kích thước / leader / cao độ cho đỡ rối.
    /// Thông số lỗi → model rỗng + danh sách lỗi (mặt cắt cần hình học hợp lệ).
    /// </summary>
    public static class RebarLayoutPreview
    {
        public class Result
        {
            public DrawingModel Model = new DrawingModel();
            public ManholeGeometry Geometry;
            public RebarDesign Rebar;
            public List<string> Errors = new List<string>();
            /// <summary>Chiều cao chữ chú giải (mm model).</summary>
            public double TextHeight;
        }

        /// <summary>Layer của mặt cắt được giữ lại trong preview.</summary>
        private static readonly string[] Keep =
        {
            Layers.Concrete, Layers.ConcreteBeyond, Layers.Hatch, Layers.Rebar, Layers.RebarHidden, Layers.Pipe, Layers.PipeHidden,
        };

        public static Result Build(ManholeParams p)
        {
            var res = new Result();
            var g = new ManholeGeometry(p);
            res.Geometry = g;
            res.Errors.AddRange(g.Errors);
            if (!g.IsValid) return res;

            var st = new DrawStyle(p.Scale);
            var rd = RebarDesigner.Design(g);
            res.Rebar = rd;
            var plan = Filter(new PlanBuilder(g, rd, st).Build());
            var sec = Filter(new SectionBuilder(g, rd, st).Build());
            var bp = plan.GetBounds();
            var bs = sec.GetBounds();
            if (!bp.IsValid || !bs.IsValid) return res;
            double h = Math.Max(bp.Width, bs.Width) * 0.035;
            res.TextHeight = h;

            // mặt cắt đặt dưới mặt bằng, cùng tim; nhãn hình chiếu phía trên mỗi hình
            sec.Translate(new Vec2(bp.CenterX - bs.CenterX, bp.MinY - 4 * h - bs.MaxY));
            bs = sec.GetBounds();
            var m = res.Model;
            m.AddRange(plan);
            m.AddRange(sec);
            Title(m, bp, "PLAN", h);
            Title(m, bs, "SECTION A-A", h);

            var b = m.GetBounds();
            double y = b.MinY - 1.5 * h;
            foreach (var line in LegendKeyed(g, rd))
            {
                m.Add(new PText { Layer = PipeLayoutPreview.HintLayer, Position = new Vec2(b.MinX, y), Text = line.Value, Height = h, Align = TextAlign.Left, VAlign = TextVAlign.Top, Key = line.Key });
                y -= 1.6 * h;
            }
            return res;
        }

        /// <summary>
        /// Các khoá (Prim.Key) cần nổi bật khi ô nhập có đường dẫn binding bắt đầu bằng field (phần trước dấu chấm) đang có focus;
        /// rỗng nếu ô đó không gắn với bộ thép nào (lớp bảo vệ, hệ số nối...).
        /// </summary>
        public static string[] KeysForField(string field)
        {
            switch (field ?? "")
            {
                case RebarKeys.WallVertical:
                case RebarKeys.WallHorizontal:
                case "BaseBottom": case "BaseTop": case "RoofBottom": case "RoofTop":
                    return new[] { field };
                case "BaseTopMat": return new[] { "BaseTop" };
                case "RoofTopMat": return new[] { "RoofTop" };
                case "CoverRebate": case "CoverRebateDepth": case "CoverRebateWidth":
                    return new[] { "RoofTop", RebarKeys.Trimmers };
                case "TrimmerDiameter": case "TrimmerCount": return new[] { RebarKeys.Trimmers };
                case "WallSingleLayer": case "WallHorizontalInside": case "VerticalBarLeg":
                    return new[] { RebarKeys.WallVertical, RebarKeys.WallHorizontal };
                case "Cogs": case "BendFactor":
                    return new[] { RebarKeys.WallVertical, RebarKeys.WallHorizontal, "BaseBottom", "BaseTop", "RoofBottom", "RoofTop" };
                default: return new string[0];
            }
        }

        /// <summary>Giữ lại bê tông, ống, thép của một hình chiếu; bỏ kích thước, ghi chú, cao độ, ký hiệu cắt, bậc thang.</summary>
        private static DrawingModel Filter(DrawingModel src)
        {
            var m = new DrawingModel();
            foreach (var it in src.Items)
                if (Array.IndexOf(Keep, it.Layer) >= 0) m.Add(it);
            return m;
        }

        private static void Title(DrawingModel m, Bounds b, string text, double h)
        {
            m.Add(new PText { Layer = PipeLayoutPreview.HintLayer, Position = new Vec2(b.MinX, b.MaxY + 0.6 * h), Text = text, Height = h, Align = TextAlign.Left, VAlign = TextVAlign.Bottom });
        }

        /// <summary>Các dòng chú giải: ký hiệu từng bộ thép như trên bản vẽ (thép tường chỉ khi tường đổ tại chỗ).</summary>
        public static List<string> Legend(ManholeGeometry g, RebarDesign rd)
        {
            var list = new List<string>();
            foreach (var kv in LegendKeyed(g, rd)) list.Add(kv.Value);
            return list;
        }

        /// <summary>Chú giải kèm khoá nổi bật của từng dòng.</summary>
        public static List<KeyValuePair<string, string>> LegendKeyed(ManholeGeometry g, RebarDesign rd)
        {
            var sch = rd.Schedule;
            var list = new List<KeyValuePair<string, string>>();
            if (g.HasCastWalls)
            {
                Add(list, RebarKeys.WallVertical, sch.Callout(BarSets.WallVert));
                Add(list, RebarKeys.WallHorizontal, sch.Callout(BarSets.WallHoriz));
            }
            Add(list, RebarKeys.ForMat(BarSets.BaseBot), sch.MatCallout(BarSets.BaseBot, false));
            if (g.P.BaseTopMat) Add(list, RebarKeys.ForMat(BarSets.BaseTop), sch.MatCallout(BarSets.BaseTop, true));
            Add(list, RebarKeys.ForMat(BarSets.RoofBot), sch.MatCallout(BarSets.RoofBot, false));
            if (g.P.RoofTopMat) Add(list, RebarKeys.ForMat(BarSets.RoofTop), sch.MatCallout(BarSets.RoofTop, true));
            var roofTrim = sch.Set(BarSets.RoofTrim);
            if (roofTrim != null) Add(list, RebarKeys.Trimmers, roofTrim.Callout);
            var pipeTrim = sch.Set(BarSets.PipeTrim);
            if (pipeTrim != null) Add(list, RebarKeys.Trimmers, pipeTrim.Callout);
            return list;
        }

        private static void Add(List<KeyValuePair<string, string>> list, string key, string s)
        {
            if (!string.IsNullOrEmpty(s)) list.Add(new KeyValuePair<string, string>(key, s));
        }
    }
}
