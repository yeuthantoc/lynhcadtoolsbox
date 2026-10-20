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

        /// <summary>Xếp ghi chú thành 2 cột: điểm chỉ có X &lt; split sang cột trái (xLeft), còn lại sang cột phải (xRight).</summary>
        protected void PlaceCalloutColumns(double split, double xLeft, double xRight)
        {
            Annot.CalloutColumn(m, st, callouts.Where(c => c.Target.X < split), xLeft, -1);
            Annot.CalloutColumn(m, st, callouts.Where(c => c.Target.X >= split), xRight, +1);
        }

        /// <summary>Điểm trên thanh thép chạy theo X (alongX) hoặc Y: along = toạ độ dọc thanh, perp = toạ độ vuông góc.</summary>
        protected static Vec2 BarPoint(bool alongX, double along, double perp)
        {
            return alongX ? new Vec2(along, perp) : new Vec2(perp, along);
        }
    }
}
