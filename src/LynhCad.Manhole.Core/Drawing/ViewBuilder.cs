using System.Collections.Generic;
using System.Linq;
using LynhCad.Manhole.Core.Geometry;
using LynhCad.Manhole.Core.Model;
using LynhCad.Manhole.Core.Rebar;

namespace LynhCad.Manhole.Core.Drawing
{
    /// <summary>
    /// Phần chung của các builder hình chiếu (mặt bằng, mặt cắt, thép bản nắp): dữ liệu đầu vào,
    /// model đang dựng, danh sách ghi chú chờ xếp cột, và vài tiện ích vẽ.
    /// </summary>
    public abstract class ViewBuilder
    {
        protected readonly ManholeGeometry g;
        protected readonly RebarDesign rd;
        protected readonly DrawStyle st;
        protected readonly DrawingModel m = new DrawingModel();
        protected readonly List<Annot.Callout> callouts = new List<Annot.Callout>();

        protected ViewBuilder(ManholeGeometry geometry, RebarDesign rebar, DrawStyle style)
        {
            g = geometry; rd = rebar; st = style;
        }

        public abstract DrawingModel Build();

        /// <summary>Hatch bê tông bị cắt (AR-CONC) theo một vòng biên.</summary>
        protected PHatch ConcreteHatch(PPolyline loop)
        {
            return new PHatch { Layer = Layers.Hatch, Pattern = "AR-CONC", PatternScale = st.ConcreteHatchScale, Loops = { loop } };
        }

        protected PHatch ConcreteHatch(Rect2 r) { return ConcreteHatch(PPolyline.Rect(r, Layers.Hatch)); }

        /// <summary>
        /// Xếp ghi chú thành 2 cột: điểm chỉ có X &lt; split sang cột trái (xLeft), còn lại sang cột phải (xRight).
        /// Chữ kích thước và chữ đã có trong hình (nhãn cao độ...) là vật cản: cột ghi chú được đẩy ra ngoài chúng
        /// để chữ ghi chú không đè lên, và leader được lách xuống để không cắt qua.
        /// </summary>
        protected void PlaceCalloutColumns(double split, double xLeft, double xRight)
        {
            var avoid = Obstacles();
            double gap = 1.5 * st.Scale;
            foreach (var r in avoid)
            {
                double cx = (r.X1 + r.X2) / 2;
                if (cx < split) xLeft = System.Math.Min(xLeft, r.X1 - gap);
                else xRight = System.Math.Max(xRight, r.X2 + gap);
            }
            Annot.CalloutColumn(m, st, callouts.Where(c => c.Target.X < split), xLeft, -1, avoid);
            Annot.CalloutColumn(m, st, callouts.Where(c => c.Target.X >= split), xRight, +1, avoid);
        }

        /// <summary>Hộp chữ kích thước và chữ rời hiện có trong hình (gọi sau khi đã vẽ kích thước, cao độ).</summary>
        private List<Rect2> Obstacles()
        {
            var list = new List<Rect2>();
            foreach (var it in m.Items)
            {
                var d = it as PDim;
                if (d != null) { list.Add(Annot.DimTextRect(d, st)); continue; }
                var blk = it as PBlock;
                var texts = blk != null ? SymbolLibrary.Expand(blk).Items.OfType<PText>() : new[] { it as PText };
                foreach (var t in texts)
                    if (t != null && !string.IsNullOrEmpty(t.Text)) list.Add(Annot.TextRect(t));
            }
            return list;
        }

        /// <summary>Điểm trên thanh thép chạy theo X (alongX) hoặc Y: along = toạ độ dọc thanh, perp = toạ độ vuông góc.</summary>
        protected static Vec2 BarPoint(bool alongX, double along, double perp)
        {
            return alongX ? new Vec2(along, perp) : new Vec2(perp, along);
        }
    }
}
