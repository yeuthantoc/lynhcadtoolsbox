using System;
using System.Collections.Generic;
using System.Linq;
using LynhCad.Manhole.Core.Geometry;

namespace LynhCad.Manhole.Core.Model
{
    /// <summary>Ống đã quy về hệ toạ độ mặt bằng (tâm hố ở gốc, X = Đông, Y = Bắc).</summary>
    public class PipeFrame
    {
        public PipeSpec Spec;
        /// <summary>Hướng ống, chỉ RA khỏi hố.</summary>
        public Vec2 Dir;
        /// <summary>Điểm trên trục ống gần tâm hố nhất.</summary>
        public Vec2 AxisPoint;
        /// <summary>Khoảng cách dọc trục từ AxisPoint tới mặt trong / mặt ngoài tường.</summary>
        public double InnerDist, OuterDist;
        public double DN, OD, PipeWall;
        /// <summary>Nửa bề rộng lỗ xuyên tường (OD/2 + khe hở).</summary>
        public double HalfGap;
        /// <summary>Cao độ (mm, so với mặt bản đáy): đáy trong, đáy ngoài, đỉnh ngoài.</summary>
        public double ZInvert, ZOuterBottom, ZOuterTop;
        /// <summary>Lỗ xuyên tường theo chiều đứng (đã cộng khe hở).</summary>
        public Interval GapZ;

        public bool IsOutlet { get { return Spec.Role == PipeRole.Outlet; } }
        public Vec2 InnerPoint { get { return AxisPoint + Dir * InnerDist; } }
        public Vec2 OuterPoint { get { return AxisPoint + Dir * OuterDist; } }
    }

    /// <summary>
    /// Các kích thước / cao độ / hướng mặt cắt suy ra từ ManholeParams, kèm kiểm tra lỗi.
    /// Mọi builder (mặt bằng, mặt cắt, thép) đều đọc từ đây, không tự tính lại.
    /// </summary>
    public class ManholeGeometry
    {
        public readonly ManholeParams P;
        public readonly List<string> Errors = new List<string>();
        public readonly List<string> Warnings = new List<string>();

        public bool IsRect { get { return P.Type == ManholeType.RectangularCast; } }

        // Mặt bằng
        public double Li, Wi, Lo, Wo, Tw;      // chữ nhật
        public double Ri, Ro;                  // tròn
        public double OuterHalfX, OuterHalfY;
        public double BaseHalfX, BaseHalfY;
        public double RoofHalfX, RoofHalfY;
        public Vec2 OpeningCenter;
        public double OpeningHalfX, OpeningHalfY;
        public bool OpeningIsCircle;

        // Cao độ (mm so với mặt bản đáy = 0)
        public double FloorRL, RoofTopRL, SoffitRL, BaseBottomRL;
        public double H, Tb, Ts;

        // Ống & mặt cắt
        public readonly List<PipeFrame> Pipes = new List<PipeFrame>();
        public PipeFrame Outlet;
        /// <summary>Phương mặt cắt A-A (dọc trục ống ra) và hướng nhìn.</summary>
        public Vec2 SectionDir, ViewDir;
        /// <summary>Khoảng lệch của đường cắt so với tâm hố, đo theo ViewDir.</summary>
        public double SectionOffset;

        public bool IsValid { get { return Errors.Count == 0; } }

        public ManholeGeometry(ManholeParams p)
        {
            P = p;
            Compute();
        }

        public double Z(double rl) { return (rl - FloorRL) * 1000.0; }

        public double LapLength(int dia) { return GeomUtil.RoundUp(P.LapFactor * dia, 50); }

        private void Compute()
        {
            Tb = P.BaseThickness;
            Ts = P.RoofThickness;

            if (IsRect)
            {
                Li = P.InternalLength; Wi = P.InternalWidth; Tw = P.WallThickness;
                Lo = Li + 2 * Tw; Wo = Wi + 2 * Tw;
                OuterHalfX = Lo / 2; OuterHalfY = Wo / 2;
                Require(Li >= 600 && Wi >= 600, "Kích thước lòng hố phải ≥ 600 mm.");
                Require(Tw >= 150, "Chiều dày tường phải ≥ 150 mm.");
            }
            else
            {
                Ri = P.InternalDiameter / 2; Tw = P.PrecastWallThickness;
                Ro = Ri + Tw;
                OuterHalfX = OuterHalfY = Ro;
                Require(P.InternalDiameter >= 600, "Đường kính trong phải ≥ 600 mm.");
                Require(Tw >= 50, "Chiều dày vách đúc sẵn phải ≥ 50 mm.");
                Require(P.RingHeight >= 150, "Chiều cao đốt đúc sẵn phải ≥ 150 mm.");
            }
            Require(Tb >= 150 && Ts >= 150, "Bản đáy và bản nắp phải dày ≥ 150 mm.");
            Require(P.Cover >= 20 && P.Cover < Math.Min(Tb, Ts) / 2, "Lớp bê tông bảo vệ không hợp lý.");

            BaseHalfX = OuterHalfX + Math.Max(0, P.BaseProjection);
            BaseHalfY = OuterHalfY + Math.Max(0, P.BaseProjection);
            RoofHalfX = OuterHalfX + Math.Max(0, P.RoofProjection);
            RoofHalfY = OuterHalfY + Math.Max(0, P.RoofProjection);

            // Lỗ thăm
            OpeningCenter = new Vec2(P.OpeningOffsetX, P.OpeningOffsetY);
            OpeningIsCircle = !IsRect;
            if (OpeningIsCircle) OpeningHalfX = OpeningHalfY = P.OpeningDiameter / 2;
            else { OpeningHalfX = P.OpeningX / 2; OpeningHalfY = P.OpeningY / 2; }
            Require(OpeningHalfX >= 250 && OpeningHalfY >= 250, "Lỗ thăm phải ≥ 500 mm.");
            if (IsRect)
            {
                if (Math.Abs(OpeningCenter.X) + OpeningHalfX > Li / 2 + 1e-6 || Math.Abs(OpeningCenter.Y) + OpeningHalfY > Wi / 2 + 1e-6)
                    Warnings.Add("Lỗ thăm vượt ra ngoài lòng hố (đè lên tường).");
            }
            else if (OpeningCenter.Length + OpeningHalfX > Ri + 1e-6)
                Warnings.Add("Lỗ thăm vượt ra ngoài lòng hố (đè lên vách).");

            ComputeLevelsAndPipes();
        }

        private void ComputeLevelsAndPipes()
        {
            if (P.Pipes == null || P.Pipes.Count == 0)
            {
                Errors.Add("Cần ít nhất một ống (ống ra).");
                return;
            }

            // Mặt bản đáy = đáy ngoài thấp nhất của các ống − SumpDepth
            double lowestOuter = P.Pipes.Min(pp => pp.InvertLevel - (pp.EffectiveOD - pp.NominalDiameter) / 2000.0);
            FloorRL = lowestOuter - P.SumpDepth / 1000.0;
            RoofTopRL = P.CoverLevel - P.CoverAllowance / 1000.0;
            SoffitRL = RoofTopRL - Ts / 1000.0;
            BaseBottomRL = FloorRL - Tb / 1000.0;
            H = (SoffitRL - FloorRL) * 1000.0;
            Require(H >= 600, string.Format("Chiều cao lòng hố chỉ còn {0:0} mm — kiểm tra cao độ nắp và invert.", H));

            foreach (var spec in P.Pipes)
            {
                var f = new PipeFrame { Spec = spec, DN = spec.NominalDiameter, OD = spec.EffectiveOD };
                f.PipeWall = (f.OD - f.DN) / 2;
                f.HalfGap = f.OD / 2 + P.PipeClearance;

                if (IsRect)
                {
                    switch (spec.Wall)
                    {
                        case WallSide.North: f.Dir = new Vec2(0, 1); f.AxisPoint = new Vec2(spec.Offset, 0); f.InnerDist = Wi / 2; f.OuterDist = Wo / 2; break;
                        case WallSide.South: f.Dir = new Vec2(0, -1); f.AxisPoint = new Vec2(spec.Offset, 0); f.InnerDist = Wi / 2; f.OuterDist = Wo / 2; break;
                        case WallSide.East: f.Dir = new Vec2(1, 0); f.AxisPoint = new Vec2(0, spec.Offset); f.InnerDist = Li / 2; f.OuterDist = Lo / 2; break;
                        default: f.Dir = new Vec2(-1, 0); f.AxisPoint = new Vec2(0, spec.Offset); f.InnerDist = Li / 2; f.OuterDist = Lo / 2; break;
                    }
                    double halfFace = (spec.Wall == WallSide.North || spec.Wall == WallSide.South) ? Li / 2 : Wi / 2;
                    if (Math.Abs(spec.Offset) + f.HalfGap > halfFace + 1e-6)
                        Errors.Add(string.Format("Ống {0}: lỗ xuyên tường vượt quá chiều dài mặt trong tường {1}.", spec.Mark, spec.Wall));
                }
                else
                {
                    f.Dir = Vec2.FromAngle(spec.AngleDeg * GeomUtil.Deg);
                    f.AxisPoint = Vec2.Zero;
                    f.InnerDist = Ri; f.OuterDist = Ro;
                    if (f.HalfGap >= Ri * 0.9)
                        Errors.Add(string.Format("Ống {0} quá lớn so với hố.", spec.Mark));
                }

                f.ZInvert = Z(spec.InvertLevel);
                f.ZOuterBottom = f.ZInvert - f.PipeWall;
                f.ZOuterTop = f.ZOuterBottom + f.OD;
                f.GapZ = new Interval(f.ZOuterBottom - P.PipeClearance, f.ZOuterTop + P.PipeClearance);

                Require(spec.NominalDiameter >= 50, string.Format("Ống {0}: DN không hợp lệ.", spec.Mark));
                if (f.GapZ.Max > H - 50)
                    Errors.Add(string.Format("Ống {0}: đỉnh lỗ xuyên ({1:0} mm) chạm bản nắp — cao độ nắp quá thấp.", spec.Mark, f.GapZ.Max));
                if (f.GapZ.Min < -1e-6)
                    Warnings.Add(string.Format("Ống {0}: lỗ xuyên ăn xuống dưới mặt bản đáy, kiểm tra SumpDepth / khe hở.", spec.Mark));
                Pipes.Add(f);
            }

            CheckPenetrationClashes();

            // Ống ra & mặt cắt
            var outlets = Pipes.Where(f => f.IsOutlet).ToList();
            if (outlets.Count == 0)
            {
                Outlet = Pipes.OrderBy(f => f.Spec.InvertLevel).First();
                Warnings.Add(string.Format("Không khai báo ống ra — lấy {0} (invert thấp nhất) làm ống ra.", Outlet.Spec.Mark));
            }
            else
            {
                Outlet = outlets.OrderBy(f => f.Spec.InvertLevel).First();
                if (outlets.Count > 1) Warnings.Add("Có nhiều ống ra — mặt cắt A-A lấy theo ống ra có invert thấp nhất.");
            }
            foreach (var f in Pipes.Where(f => !f.IsOutlet && f.Spec.InvertLevel < Outlet.Spec.InvertLevel - 1e-6))
                Warnings.Add(string.Format("Ống vào {0} có invert thấp hơn ống ra {1}.", f.Spec.Mark, Outlet.Spec.Mark));

            SectionDir = Outlet.Dir;
            ViewDir = SectionDir.Perp();
            SectionOffset = Outlet.AxisPoint.Dot(ViewDir);
        }

        private void CheckPenetrationClashes()
        {
            for (int i = 0; i < Pipes.Count; i++)
                for (int j = i + 1; j < Pipes.Count; j++)
                {
                    var a = Pipes[i]; var b = Pipes[j];
                    if (!a.GapZ.Overlaps(b.GapZ)) continue;
                    bool clash;
                    if (IsRect)
                    {
                        clash = a.Spec.Wall == b.Spec.Wall &&
                                Math.Abs(a.Spec.Offset - b.Spec.Offset) < a.HalfGap + b.HalfGap;
                    }
                    else
                    {
                        double da = Math.Asin(Math.Min(1, a.HalfGap / Ri));
                        double db = Math.Asin(Math.Min(1, b.HalfGap / Ri));
                        double diff = Math.Abs(GeomUtil.NormAngle(a.Dir.Angle() - b.Dir.Angle() + Math.PI) - Math.PI);
                        clash = diff < da + db;
                    }
                    if (clash)
                        Errors.Add(string.Format("Lỗ xuyên của ống {0} và {1} chồng lên nhau.", a.Spec.Mark, b.Spec.Mark));
                }
        }

        /// <summary>
        /// Hố tròn: các khoảng góc bị lỗ xuyên chiếm trên đường tròn bán kính r (radian, CCW, tâm = góc ống).
        /// </summary>
        public List<Tuple<double, double>> AngularGaps(double r)
        {
            var list = new List<Tuple<double, double>>();
            foreach (var f in Pipes)
            {
                double a = GeomUtil.NormAngle(f.Dir.Angle());
                double half = Math.Asin(Math.Min(1, f.HalfGap / r));
                list.Add(Tuple.Create(a - half, a + half));
            }
            list.Sort((x, y) => x.Item1.CompareTo(y.Item1));
            return list;
        }

        private void Require(bool ok, string msg) { if (!ok) Errors.Add(msg); }
    }
}
