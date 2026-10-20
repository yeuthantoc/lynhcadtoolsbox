using System;
using System.Collections.Generic;
using System.Linq;
using LynhCad.Manhole.Core.Geometry;

namespace LynhCad.Manhole.Core.Model
{
    /// <summary>
    /// Bố trí bậc thang (step irons) trên mặt trong tường: một hàng bậc thẳng đứng.
    /// Toạ độ mặt bằng như ManholeGeometry (tâm hố ở gốc); cao độ z (mm, 0 = mặt bản đáy).
    /// </summary>
    public class StepLayout
    {
        /// <summary>Tâm hàng bậc trên mặt trong tường.</summary>
        public Vec2 WallPoint;
        /// <summary>Pháp tuyến đơn vị từ tường hướng VÀO lòng hố.</summary>
        public Vec2 Normal;
        /// <summary>Hướng dọc tường (vuông góc Normal).</summary>
        public Vec2 Tangent { get { return Normal.Perp(); } }
        public double Width, Projection, Spacing;
        /// <summary>Cao độ từng bậc, từ trên xuống.</summary>
        public List<double> Levels = new List<double>();
        /// <summary>Góc (độ) hướng từ tâm hố ra bức tường đặt bậc.</summary>
        public double AngleDeg;

        /// <summary>Điểm giữa thanh bậc (mép ngoài, xa tường nhất).</summary>
        public Vec2 RungCentre { get { return WallPoint + Normal * Projection; } }
    }

    internal static class StepPlanner
    {
        /// <summary>Khoảng hở tối thiểu theo phương ngang giữa mép bậc và mép lỗ xuyên ống (mm).</summary>
        private const double PipeClear = 75;
        /// <summary>Bậc cách góc tường tối thiểu (hố chữ nhật, mm).</summary>
        private const double CornerClear = 50;

        public static StepLayout Plan(ManholeGeometry g)
        {
            var P = g.P;
            if (P.StepSpacing < 150) g.Errors.Add("Khoảng cách bậc thang phải ≥ 150 mm.");
            if (P.StepWidth < 150) g.Errors.Add("Bề rộng bậc thang phải ≥ 150 mm.");
            if (P.StepProjection < 50) g.Errors.Add("Bậc thang phải nhô ra ≥ 50 mm.");
            if (P.StepSpacing < 150 || P.StepWidth < 150 || P.StepProjection < 50 || g.Outlet == null) return null;

            // cao độ: từ dưới bản nắp xuống, dừng trên mặt benching (ngang đỉnh ống ra) một khoảng
            double zTop = g.H - P.StepTopGap;
            double zMin = g.Outlet.ZInvert + g.Outlet.DN + P.StepBottomClear;
            var levels = new List<double>();
            for (double z = zTop; z >= zMin - 1e-6; z -= P.StepSpacing) levels.Add(z);
            if (levels.Count == 0)
            {
                g.Warnings.Add("Hố không đủ cao để đặt bậc thang — bỏ qua bậc thang.");
                return null;
            }

            StepLayout best = null;
            double bestScore = double.MaxValue;
            string bestClash = null;
            IEnumerable<double> angles = P.StepAuto
                ? Enumerable.Range(0, 72).Select(k => k * 5.0)
                : new[] { P.StepAngle };
            foreach (var deg in angles)
            {
                var s = Place(g, deg);
                if (s == null) continue;
                s.Levels = levels;
                string clash;
                PipeRoom(g, s, out clash);
                double toOpening = DistanceToOpening(g, s.RungCentre);
                // ưu tiên: không trùng lỗ ống → nằm dưới / gần lỗ thăm (theo bậc 10 mm) → gần tâm lỗ thăm nhất
                double score = (clash != null ? 1e9 : 0) + Math.Round(toOpening / 10) * 1e4 + (s.RungCentre - g.OpeningCenter).Length;
                if (score < bestScore) { bestScore = score; best = s; bestClash = clash; }
            }
            if (best == null)
            {
                g.Warnings.Add(P.StepAuto
                    ? "Không tìm được chỗ đặt bậc thang trên tường — bỏ qua bậc thang."
                    : string.Format("Góc {0:0}°: bậc thang không vừa mặt tường (quá sát góc) — bỏ qua bậc thang.", P.StepAngle));
                return null;
            }
            if (bestClash != null)
                g.Warnings.Add(string.Format("Bậc thang trùng vị trí lỗ ống {0} — chọn góc đặt khác.", bestClash));
            double d = DistanceToOpening(g, best.RungCentre);
            if (d > 150)
                g.Warnings.Add(string.Format("Bậc thang cách mép lỗ thăm {0:0} mm — kiểm tra lối lên xuống.", d));
            return best;
        }

        /// <summary>Đặt hàng bậc trên mặt trong tường theo tia từ tâm hố, góc deg. Null nếu không vừa mặt tường.</summary>
        private static StepLayout Place(ManholeGeometry g, double deg)
        {
            var P = g.P;
            var dir = Vec2.FromAngle(deg * GeomUtil.Deg);
            var s = new StepLayout { Width = P.StepWidth, Projection = P.StepProjection, Spacing = P.StepSpacing, AngleDeg = GeomUtil.NormAngle(deg * GeomUtil.Deg) / GeomUtil.Deg };
            if (g.IsCircular)
            {
                if (P.StepWidth / 2 >= g.Ri * 0.7) return null;
                s.WallPoint = dir * g.Ri;
                s.Normal = -dir;
                return s;
            }
            double hx = g.Li / 2, hy = g.Wi / 2;
            double tx = Math.Abs(dir.X) < 1e-9 ? double.MaxValue : hx / Math.Abs(dir.X);
            double ty = Math.Abs(dir.Y) < 1e-9 ? double.MaxValue : hy / Math.Abs(dir.Y);
            var hit = dir * Math.Min(tx, ty);
            if (tx <= ty)
            {
                if (Math.Abs(hit.Y) + P.StepWidth / 2 + CornerClear > hy) return null;
                s.WallPoint = new Vec2(Math.Sign(dir.X) * hx, hit.Y);
                s.Normal = new Vec2(-Math.Sign(dir.X), 0);
            }
            else
            {
                if (Math.Abs(hit.X) + P.StepWidth / 2 + CornerClear > hx) return null;
                s.WallPoint = new Vec2(hit.X, Math.Sign(dir.Y) * hy);
                s.Normal = new Vec2(0, -Math.Sign(dir.Y));
            }
            return s;
        }

        /// <summary>
        /// Khoảng hở ngang nhỏ nhất (mm) giữa mép hàng bậc và mép lỗ xuyên của các ống cùng tường.
        /// Xét trên mặt bằng, bất kể cao độ: bậc không được nằm thẳng trên ống / máng (leo xuống sẽ đặt chân vào lòng máng).
        /// clash = mã ống bị trùng (khoảng hở &lt; PipeClear), null nếu không trùng.
        /// </summary>
        private static double PipeRoom(ManholeGeometry g, StepLayout s, out string clash)
        {
            clash = null;
            double room = double.MaxValue;
            foreach (var f in g.Pipes)
            {
                double along;
                if (g.IsCircular) along = (f.InnerPoint - s.WallPoint).Length;
                else
                {
                    if ((f.Dir + s.Normal).Length > 1e-6) continue; // ống ở tường khác
                    along = Math.Abs((f.InnerPoint - s.WallPoint).Dot(s.Tangent));
                }
                double gap = along - f.HalfGap - s.Width / 2;
                room = Math.Min(room, gap);
                if (gap < PipeClear && clash == null) clash = f.Spec.Mark;
            }
            return room;
        }

        /// <summary>Khoảng cách mặt bằng từ điểm tới lỗ thăm (0 nếu nằm dưới lỗ).</summary>
        private static double DistanceToOpening(ManholeGeometry g, Vec2 p)
        {
            var rel = p - g.OpeningCenter;
            if (g.OpeningIsCircle) return Math.Max(0, rel.Length - g.OpeningHalfX);
            double dx = Math.Max(0, Math.Abs(rel.X) - g.OpeningHalfX);
            double dy = Math.Max(0, Math.Abs(rel.Y) - g.OpeningHalfY);
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
