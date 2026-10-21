using System;
using System.Collections.Generic;
using LynhCad.Manhole.Core.Geometry;
using LynhCad.Manhole.Core.Model;
using LynhCad.Manhole.Core.Rebar;

namespace LynhCad.Manhole.Core.Drawing
{
    /// <summary>
    /// Sơ đồ bố trí thép để xem trong hộp thoại (tab Reinforcement) trước khi vẽ: mặt cắt A-A rút gọn
    /// (bê tông, ống, thép) + chú giải các bộ thép bên dưới. Không có kích thước / leader / cao độ cho đỡ rối.
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
            var sec = new SectionBuilder(g, rd, st).Build();
            var m = res.Model;
            foreach (var it in sec.Items)
                if (Array.IndexOf(Keep, it.Layer) >= 0) m.Add(it);

            var b = m.GetBounds();
            if (!b.IsValid) return res;
            double h = Math.Max(b.Width, b.Height) * 0.035;
            res.TextHeight = h;
            double y = b.MinY - 1.5 * h;
            foreach (var line in Legend(g, rd))
            {
                m.Add(new PText { Layer = PipeLayoutPreview.HintLayer, Position = new Vec2(b.MinX, y), Text = line, Height = h, Align = TextAlign.Left, VAlign = TextVAlign.Top });
                y -= 1.6 * h;
            }
            return res;
        }

        /// <summary>Các dòng chú giải: ký hiệu từng bộ thép như trên bản vẽ (thép tường chỉ khi tường đổ tại chỗ).</summary>
        public static List<string> Legend(ManholeGeometry g, RebarDesign rd)
        {
            var sch = rd.Schedule;
            var list = new List<string>();
            if (g.HasCastWalls)
            {
                list.Add(sch.Callout(BarSets.WallVert));
                list.Add(sch.Callout(BarSets.WallHoriz));
            }
            Add(list, sch.MatCallout(BarSets.BaseBot, false));
            if (g.P.BaseTopMat) Add(list, sch.MatCallout(BarSets.BaseTop, true));
            Add(list, sch.MatCallout(BarSets.RoofBot, false));
            if (g.P.RoofTopMat) Add(list, sch.MatCallout(BarSets.RoofTop, true));
            var roofTrim = sch.Set(BarSets.RoofTrim);
            if (roofTrim != null) list.Add(roofTrim.Callout);
            var pipeTrim = sch.Set(BarSets.PipeTrim);
            if (pipeTrim != null) list.Add(pipeTrim.Callout);
            return list;
        }

        private static void Add(List<string> list, string s)
        {
            if (!string.IsNullOrEmpty(s)) list.Add(s);
        }
    }
}
