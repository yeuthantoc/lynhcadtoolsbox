using System;
using System.Collections.Generic;
using LynhCad.Manhole.Core.Geometry;
using LynhCad.Manhole.Core.Model;
using LynhCad.Manhole.Core.Rebar;

namespace LynhCad.Manhole.Core.Drawing
{
    /// <summary>
    /// Mặt cắt A-A rút gọn để xem trong hộp thoại (tab Pipes, dưới sơ đồ mặt bằng): bê tông, benching, ống bị cắt / ống phía sau,
    /// nhãn "P1 IL 12.450" cho từng ống; ống ra màu xanh, ống lỗi màu đỏ, mọi nét của ống mang Key = tên ống.
    /// Cần hình học hợp lệ (mặt cắt dựa vào ống ra và cao độ); lỗi → model rỗng + danh sách lỗi.
    /// </summary>
    public static class PipeSectionPreview
    {
        public class Result
        {
            public DrawingModel Model = new DrawingModel();
            public ManholeGeometry Geometry;
            public List<string> Errors = new List<string>();
            public double TextHeight;
        }

        private static readonly string[] Keep = { Layers.Concrete, Layers.ConcreteBeyond, Layers.Hatch, Layers.Pipe, Layers.PipeHidden };

        public static Result Build(ManholeParams p)
        {
            var res = new Result();
            var g = new ManholeGeometry(p);
            res.Geometry = g;
            res.Errors.AddRange(g.Errors);
            if (!g.IsValid) return res;

            var st = new DrawStyle(p.Scale);
            var sec = new SectionBuilder(g, RebarDesigner.Design(g), st).Build();
            var m = res.Model;
            foreach (var it in sec.Items)
            {
                if (Array.IndexOf(Keep, it.Layer) < 0) continue;
                // ống ra / ống lỗi đổi màu như sơ đồ mặt bằng
                if (!string.IsNullOrEmpty(it.Key) && (it.Layer == Layers.Pipe || it.Layer == Layers.PipeHidden))
                {
                    var f = g.Pipes.Find(x => x.Spec.Mark == it.Key);
                    if (f != null)
                    {
                        if (g.PipesWithErrors.Contains(f.Spec)) it.Layer = PipeLayoutPreview.ErrorLayer;
                        else if (f.IsOutlet) it.Layer = PipeLayoutPreview.OutletLayer;
                    }
                }
                m.Add(it);
            }
            var b = m.GetBounds();
            if (!b.IsValid) return res;
            double h = Math.Max(b.Width, b.Height) * 0.03;
            res.TextHeight = h;

            // nhãn ống: ống bị cắt → tại đầu đoạn ống thò ra, chữ chạy ra phía ngoài; ống phía sau → giữa, trên vòng tròn
            var u = g.SectionDir; var d = g.ViewDir;
            var origin = d * g.SectionOffset;
            foreach (var f in g.Pipes)
            {
                bool cut = Math.Abs(Math.Abs(f.Dir.Dot(u)) - 1) < 1e-3 && Math.Abs((f.AxisPoint - origin).Dot(d)) < 1.0;
                double beyond = (f.AxisPoint - origin).Dot(d) + f.Dir.Dot(d) * f.InnerDist;
                if (!cut && beyond <= 1) continue; // phía trước mặt cắt: không vẽ
                string layer = g.PipesWithErrors.Contains(f.Spec) ? PipeLayoutPreview.ErrorLayer : f.IsOutlet ? PipeLayoutPreview.OutletLayer : Layers.Pipe;
                string text = string.Format("{0} IL {1:0.000}", f.Spec.Mark, f.Spec.InvertLevel);
                PText label;
                if (cut)
                {
                    int side = f.Dir.Dot(u) > 0 ? 1 : -1;
                    double sEnd = (f.OuterPoint + f.Dir * st.PipeStub - origin).Dot(u);
                    label = new PText { Position = new Vec2(sEnd, f.ZOuterTop + 0.3 * h), Align = side > 0 ? TextAlign.Left : TextAlign.Right, VAlign = TextVAlign.Bottom };
                }
                else
                {
                    double s = (f.InnerPoint - origin).Dot(u);
                    label = new PText { Position = new Vec2(s, f.ZInvert + f.DN / 2 + f.OD / 2 + 0.3 * h), Align = TextAlign.Center, VAlign = TextVAlign.Bottom };
                }
                label.Layer = layer; label.Text = text; label.Height = h; label.Key = f.Spec.Mark;
                m.Add(label);
            }
            var b2 = m.GetBounds();
            m.Add(new PText { Layer = PipeLayoutPreview.HintLayer, Position = new Vec2(b2.MinX, b2.MaxY + 0.6 * h), Text = "SECTION A-A", Height = h, Align = TextAlign.Left, VAlign = TextVAlign.Bottom });
            return res;
        }
    }
}
