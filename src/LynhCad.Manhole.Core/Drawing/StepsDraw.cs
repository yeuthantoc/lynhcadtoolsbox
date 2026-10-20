using LynhCad.Manhole.Core.Geometry;
using LynhCad.Manhole.Core.Model;

namespace LynhCad.Manhole.Core.Drawing
{
    /// <summary>Phần vẽ bậc thang dùng chung cho mặt bằng và sơ đồ ống.</summary>
    public static class StepsDraw
    {
        /// <summary>Bậc thang nhìn từ trên: hình chữ U từ mặt tường nhô vào lòng hố.</summary>
        public static PPolyline PlanU(StepLayout s, string layer)
        {
            var t = s.Tangent * (s.Width / 2);
            var n = s.Normal * s.Projection;
            return new PPolyline { Layer = layer }
                .Add(s.WallPoint - t).Add(s.WallPoint - t + n).Add(s.WallPoint + t + n).Add(s.WallPoint + t);
        }

        public static string Callout(StepLayout s)
        {
            return string.Format("STEP IRONS AT {0:0} CTS, {1} No.\n(TO AUTHORITY STANDARD)", s.Spacing, s.Levels.Count);
        }
    }
}
