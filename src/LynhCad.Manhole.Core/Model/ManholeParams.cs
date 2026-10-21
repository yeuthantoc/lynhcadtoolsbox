using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace LynhCad.Manhole.Core.Model
{
    public enum ManholeType
    {
        /// <summary>Hố ga chữ nhật BTCT đổ tại chỗ (đáy + tường + nắp liền khối).</summary>
        RectangularCast = 0,
        /// <summary>Hố ga tròn: thân đúc sẵn (precast), đáy và nắp đổ tại chỗ.</summary>
        CircularPrecast = 1,
        /// <summary>Hố ga tròn BTCT đổ tại chỗ (đáy + tường tròn + nắp liền khối, tường có thép hai mặt).</summary>
        CircularCast = 2,
    }

    public enum PipeRole { Inlet = 0, Outlet = 1 }

    /// <summary>Tường mà ống xuyên qua (chỉ dùng cho hố ga chữ nhật). N = +Y, E = +X.</summary>
    public enum WallSide { North = 0, East = 1, South = 2, West = 3 }

    /// <summary>Đường kính + khoảng cách của một lớp thép, ví dụ N12-200.</summary>
    [DataContract]
    public class BarSpec
    {
        [DataMember(Name = "dia")] public int Diameter { get; set; }
        [DataMember(Name = "spacing")] public double Spacing { get; set; }

        public BarSpec() { }
        public BarSpec(int dia, double spacing) { Diameter = dia; Spacing = spacing; }

        /// <summary>Ký hiệu kiểu Úc: N12-200.</summary>
        public string Callout { get { return string.Format("N{0}-{1:0}", Diameter, Spacing); } }

        public BarSpec Clone() { return new BarSpec(Diameter, Spacing); }
    }

    /// <summary>Một ống vào/ra hố ga.</summary>
    [DataContract]
    public class PipeSpec
    {
        [DataMember(Name = "mark")] public string Mark { get; set; }

        public PipeRole Role { get; set; }
        [DataMember(Name = "role")]
        private string RoleText { get { return Role.ToString(); } set { Role = ParseEnum(value, PipeRole.Inlet); } }

        /// <summary>Đường kính trong danh định DN (mm).</summary>
        [DataMember(Name = "dn")] public double NominalDiameter { get; set; }

        /// <summary>Đường kính ngoài (mm). ≤ 0 → tự lấy ≈ 1.2·DN.</summary>
        [DataMember(Name = "od")] public double OutsideDiameter { get; set; }

        [DataMember(Name = "material")] public string Material { get; set; }

        /// <summary>Cao độ đáy trong ống (invert level), m AHD.</summary>
        [DataMember(Name = "il")] public double InvertLevel { get; set; }

        /// <summary>Hố tròn: góc phương vị trên mặt bằng, độ, 0 = +X, ngược chiều kim đồng hồ.</summary>
        [DataMember(Name = "angle")] public double AngleDeg { get; set; }

        /// <summary>Hố chữ nhật: tường ống đi qua.</summary>
        public WallSide Wall { get; set; }
        [DataMember(Name = "wall")]
        private string WallText { get { return Wall.ToString(); } set { Wall = ParseEnum(value, WallSide.East); } }

        /// <summary>Hố chữ nhật: độ lệch tâm ống dọc theo tường so với tim hố (mm, dương về +X hoặc +Y).</summary>
        [DataMember(Name = "offset")] public double Offset { get; set; }

        public PipeSpec() { SetDefaults(); }

        [OnDeserializing]
        private void OnDeserializing(StreamingContext c) { SetDefaults(); }

        private void SetDefaults()
        {
            Mark = "P1"; Role = PipeRole.Inlet; NominalDiameter = 300; Material = "RCP"; Wall = WallSide.West;
        }

        public double EffectiveOD
        {
            get { return OutsideDiameter > NominalDiameter ? OutsideDiameter : Math.Round(NominalDiameter * 1.2); }
        }

        public double WallThickness { get { return (EffectiveOD - NominalDiameter) / 2; } }

        public PipeSpec Clone() { return (PipeSpec)MemberwiseClone(); }

        internal static T ParseEnum<T>(string s, T fallback) where T : struct
        {
            T v;
            return Enum.TryParse(s, true, out v) ? v : fallback;
        }
    }

    /// <summary>
    /// Toàn bộ thông số một hố ga. Kích thước mm, cao độ m (AHD).
    /// Giá trị mặc định là ví dụ hợp lý để chạy thử, KHÔNG phải giá trị thiết kế.
    /// </summary>
    [DataContract]
    public class ManholeParams
    {
        [DataMember(Name = "id")] public string Id { get; set; }

        public ManholeType Type { get; set; }
        [DataMember(Name = "type")]
        private string TypeText { get { return Type.ToString(); } set { Type = PipeSpec.ParseEnum(value, ManholeType.RectangularCast); } }

        // ---------- Hố chữ nhật ----------
        /// <summary>Chiều dài lòng theo X (mm).</summary>
        [DataMember(Name = "internalLength")] public double InternalLength { get; set; }
        /// <summary>Chiều rộng lòng theo Y (mm).</summary>
        [DataMember(Name = "internalWidth")] public double InternalWidth { get; set; }
        /// <summary>Chiều dày tường BTCT đổ tại chỗ (mm): hố chữ nhật và hố tròn đổ tại chỗ.</summary>
        [DataMember(Name = "wallThickness")] public double WallThickness { get; set; }

        // ---------- Hố tròn (đúc sẵn và đổ tại chỗ) ----------
        [DataMember(Name = "internalDiameter")] public double InternalDiameter { get; set; }
        /// <summary>Hố tròn đúc sẵn: chiều dày vách đốt cống (mm).</summary>
        [DataMember(Name = "precastWallThickness")] public double PrecastWallThickness { get; set; }
        /// <summary>Chiều cao một đốt cống/vành đúc sẵn, để vẽ mối nối (mm).</summary>
        [DataMember(Name = "ringHeight")] public double RingHeight { get; set; }

        // ---------- Bản đáy / bản nắp ----------
        [DataMember(Name = "baseThickness")] public double BaseThickness { get; set; }
        /// <summary>Bản đáy nhô ra ngoài mặt ngoài tường (mm).</summary>
        [DataMember(Name = "baseProjection")] public double BaseProjection { get; set; }
        [DataMember(Name = "roofThickness")] public double RoofThickness { get; set; }
        /// <summary>Bản nắp nhô ra ngoài mặt ngoài tường (mm). Hố tròn: nắp vuông cạnh OD + 2×giá trị này.</summary>
        [DataMember(Name = "roofProjection")] public double RoofProjection { get; set; }

        // ---------- Lỗ thăm (access opening) ----------
        /// <summary>Hố chữ nhật: kích thước lỗ theo X × Y (mm).</summary>
        [DataMember(Name = "openingX")] public double OpeningX { get; set; }
        [DataMember(Name = "openingY")] public double OpeningY { get; set; }
        /// <summary>Hố tròn: đường kính lỗ thăm (mm).</summary>
        [DataMember(Name = "openingDiameter")] public double OpeningDiameter { get; set; }
        [DataMember(Name = "openingOffsetX")] public double OpeningOffsetX { get; set; }
        [DataMember(Name = "openingOffsetY")] public double OpeningOffsetY { get; set; }

        // ---------- Cao độ ----------
        /// <summary>Cao độ nắp (cover level), m.</summary>
        [DataMember(Name = "coverLevel")] public double CoverLevel { get; set; }
        /// <summary>Khoảng từ CL xuống mặt bản nắp (khung nắp + vành chỉnh cao độ), mm.</summary>
        [DataMember(Name = "coverAllowance")] public double CoverAllowance { get; set; }
        /// <summary>
        /// Khoảng từ đáy NGOÀI của ống thấp nhất xuống mặt bản đáy (mm).
        /// Phần này + phần trên được lấp bằng bê tông benching tạo máng.
        /// </summary>
        [DataMember(Name = "sumpDepth")] public double SumpDepth { get; set; }

        // ---------- Thép ----------
        [DataMember(Name = "cover")] public double Cover { get; set; }
        [DataMember(Name = "wallVertical")] public BarSpec WallVertical { get; set; }
        [DataMember(Name = "wallHorizontal")] public BarSpec WallHorizontal { get; set; }
        /// <summary>Thép tường 1 lớp đặt giữa tường (CENTRAL) thay vì 2 lớp hai mặt (EF).</summary>
        [DataMember(Name = "wallSingleLayer")] public bool WallSingleLayer { get; set; }
        [DataMember(Name = "baseBottom")] public BarSpec BaseBottom { get; set; }
        [DataMember(Name = "baseTopMat")] public bool BaseTopMat { get; set; }
        [DataMember(Name = "baseTop")] public BarSpec BaseTop { get; set; }
        [DataMember(Name = "roofBottom")] public BarSpec RoofBottom { get; set; }
        [DataMember(Name = "roofTopMat")] public bool RoofTopMat { get; set; }
        [DataMember(Name = "roofTop")] public BarSpec RoofTop { get; set; }
        [DataMember(Name = "trimmerDia")] public int TrimmerDiameter { get; set; }
        /// <summary>Số thanh gia cường mỗi cạnh lỗ (mỗi lưới).</summary>
        [DataMember(Name = "trimmerCount")] public int TrimmerCount { get; set; }
        /// <summary>Chiều dài nối chồng = LapFactor × d (làm tròn lên 50).</summary>
        [DataMember(Name = "lapFactor")] public double LapFactor { get; set; }
        /// <summary>Chân L của thép đứng tường neo vào bản đáy (mm).</summary>
        [DataMember(Name = "verticalBarLeg")] public double VerticalBarLeg { get; set; }

        // ---------- Ống ----------
        /// <summary>Khe hở quanh ống khi để lỗ xuyên tường (mỗi phía, mm).</summary>
        [DataMember(Name = "pipeClearance")] public double PipeClearance { get; set; }
        [DataMember(Name = "pipes")] public List<PipeSpec> Pipes { get; set; }

        // ---------- Bậc thang (step irons) ----------
        /// <summary>Vẽ bậc thang lên xuống trên mặt trong tường.</summary>
        [DataMember(Name = "stepIrons")] public bool StepIrons { get; set; }
        /// <summary>Khoảng cách đứng giữa các bậc (mm).</summary>
        [DataMember(Name = "stepSpacing")] public double StepSpacing { get; set; }
        /// <summary>Bề rộng bậc (mm).</summary>
        [DataMember(Name = "stepWidth")] public double StepWidth { get; set; }
        /// <summary>Bậc nhô ra khỏi mặt trong tường (mm).</summary>
        [DataMember(Name = "stepProjection")] public double StepProjection { get; set; }
        /// <summary>Khoảng từ đáy bản nắp xuống bậc trên cùng (mm).</summary>
        [DataMember(Name = "stepTopGap")] public double StepTopGap { get; set; }
        /// <summary>Bậc dưới cùng cao hơn mặt benching tối thiểu (mm).</summary>
        [DataMember(Name = "stepBottomClear")] public double StepBottomClear { get; set; }
        /// <summary>true: tự chọn vị trí gần lỗ thăm, tránh lỗ ống. false: dùng StepAngle.</summary>
        [DataMember(Name = "stepAuto")] public bool StepAuto { get; set; }
        /// <summary>Hướng từ tâm hố tới bức tường đặt bậc (độ, 0 = +X, ngược chiều kim đồng hồ) khi StepAuto = false.</summary>
        [DataMember(Name = "stepAngle")] public double StepAngle { get; set; }

        // ---------- Bản vẽ ----------
        /// <summary>Mẫu số tỉ lệ, 20 = 1:20.</summary>
        [DataMember(Name = "scale")] public double Scale { get; set; }
        [DataMember(Name = "concreteGrade")] public string ConcreteGrade { get; set; }
        [DataMember(Name = "drawRoofPlan")] public bool DrawRoofPlan { get; set; }
        [DataMember(Name = "drawSchedule")] public bool DrawSchedule { get; set; }

        public ManholeParams()
        {
            SetDefaults();
        }

        [OnDeserializing]
        private void OnDeserializing(StreamingContext c) { SetDefaults(); }

        private void SetDefaults()
        {
            Id = "MH01";
            Type = ManholeType.RectangularCast;
            InternalLength = 1500; InternalWidth = 1200; WallThickness = 200;
            InternalDiameter = 1200; PrecastWallThickness = 100; RingHeight = 600;
            BaseThickness = 250; BaseProjection = 150;
            RoofThickness = 200; RoofProjection = 0;
            OpeningX = 900; OpeningY = 600; OpeningDiameter = 600;
            OpeningOffsetX = 0; OpeningOffsetY = 0;
            CoverLevel = 15.000; CoverAllowance = 200; SumpDepth = 50;
            Cover = 50;
            WallVertical = new BarSpec(12, 200);
            WallHorizontal = new BarSpec(12, 200);
            WallSingleLayer = false;
            BaseBottom = new BarSpec(16, 200);
            BaseTopMat = true;
            BaseTop = new BarSpec(16, 200);
            RoofBottom = new BarSpec(12, 200);
            RoofTopMat = false;
            RoofTop = new BarSpec(12, 200);
            TrimmerDiameter = 16; TrimmerCount = 2;
            LapFactor = 50; VerticalBarLeg = 300;
            PipeClearance = 50;
            StepIrons = false; StepSpacing = 300; StepWidth = 350; StepProjection = 150;
            StepTopGap = 300; StepBottomClear = 300; StepAuto = true; StepAngle = 0;
            Pipes = new List<PipeSpec>();
            Scale = 20;
            ConcreteGrade = "N40";
            DrawRoofPlan = true;
            DrawSchedule = true;
        }

        public ManholeParams Clone()
        {
            var c = (ManholeParams)MemberwiseClone();
            c.WallVertical = WallVertical.Clone(); c.WallHorizontal = WallHorizontal.Clone();
            c.BaseBottom = BaseBottom.Clone(); c.BaseTop = BaseTop.Clone();
            c.RoofBottom = RoofBottom.Clone(); c.RoofTop = RoofTop.Clone();
            c.Pipes = new List<PipeSpec>();
            foreach (var p in Pipes) c.Pipes.Add(p.Clone());
            return c;
        }

        /// <summary>Ví dụ hố chữ nhật 1500×1200, 2 ống vào 1 ống ra.</summary>
        public static ManholeParams SampleRectangular()
        {
            var p = new ManholeParams { Id = "MH01", Type = ManholeType.RectangularCast, CoverLevel = 15.000 };
            p.Pipes.Add(new PipeSpec { Mark = "P1", Role = PipeRole.Inlet, NominalDiameter = 300, OutsideDiameter = 360, InvertLevel = 12.450, Wall = WallSide.West, Offset = 0 });
            p.Pipes.Add(new PipeSpec { Mark = "P2", Role = PipeRole.Inlet, NominalDiameter = 225, OutsideDiameter = 275, InvertLevel = 12.600, Wall = WallSide.North, Offset = -200 });
            p.Pipes.Add(new PipeSpec { Mark = "P3", Role = PipeRole.Outlet, NominalDiameter = 375, OutsideDiameter = 450, InvertLevel = 12.400, Wall = WallSide.East, Offset = 0 });
            return p;
        }

        /// <summary>Ví dụ hố tròn ID1500 BTCT đổ tại chỗ, 2 ống vào 1 ống ra.</summary>
        public static ManholeParams SampleCircularCast()
        {
            var p = new ManholeParams
            {
                Id = "MH04", Type = ManholeType.CircularCast, InternalDiameter = 1500, WallThickness = 200,
                RoofProjection = 0, BaseProjection = 150, OpeningDiameter = 600, CoverLevel = 15.000,
                // lỗ thăm lệch về phía Nam (không có ống) để bậc thang nằm ngay dưới lỗ
                OpeningOffsetY = -350, StepIrons = true,
            };
            p.Pipes.Add(new PipeSpec { Mark = "P1", Role = PipeRole.Inlet, NominalDiameter = 375, OutsideDiameter = 450, InvertLevel = 12.500, AngleDeg = 180 });
            p.Pipes.Add(new PipeSpec { Mark = "P2", Role = PipeRole.Inlet, NominalDiameter = 300, OutsideDiameter = 360, InvertLevel = 12.600, AngleDeg = 90 });
            p.Pipes.Add(new PipeSpec { Mark = "P3", Role = PipeRole.Outlet, NominalDiameter = 450, OutsideDiameter = 530, InvertLevel = 12.450, AngleDeg = 0 });
            return p;
        }

        /// <summary>Ví dụ hố tròn ID1200 đúc sẵn, 2 ống vào 1 ống ra.</summary>
        public static ManholeParams SampleCircular()
        {
            var p = new ManholeParams
            {
                Id = "MH02", Type = ManholeType.CircularPrecast, InternalDiameter = 1200, PrecastWallThickness = 100,
                RoofProjection = 150, BaseProjection = 150, CoverLevel = 14.200,
            };
            p.Pipes.Add(new PipeSpec { Mark = "P1", Role = PipeRole.Inlet, NominalDiameter = 225, OutsideDiameter = 250, Material = "PVC-U", InvertLevel = 11.380, AngleDeg = 180 });
            p.Pipes.Add(new PipeSpec { Mark = "P2", Role = PipeRole.Inlet, NominalDiameter = 150, OutsideDiameter = 170, Material = "PVC-U", InvertLevel = 11.450, AngleDeg = 120 });
            p.Pipes.Add(new PipeSpec { Mark = "P3", Role = PipeRole.Outlet, NominalDiameter = 225, OutsideDiameter = 250, Material = "PVC-U", InvertLevel = 11.350, AngleDeg = 0 });
            return p;
        }
    }
}
