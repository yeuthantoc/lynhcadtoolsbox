namespace LynhCad.Manhole.Core.Drawing
{
    /// <summary>Kích thước chữ / ký hiệu quy đổi theo tỉ lệ bản vẽ (paper mm × scale = model mm).</summary>
    public class DrawStyle
    {
        public readonly double Scale;
        public DrawStyle(double scale) { Scale = scale <= 0 ? 20 : scale; }

        public double TextSmall { get { return 2.5 * Scale; } }
        public double TextTitle { get { return 5.0 * Scale; } }
        public double TextSubtitle { get { return 2.5 * Scale; } }
        public double DimOffset { get { return 8.0 * Scale; } }
        public double DimStep { get { return 7.0 * Scale; } }
        public double LabelGap { get { return 4.5 * Scale; } }
        public double ViewGap { get { return 30.0 * Scale; } }
        public double PipeStub { get { return 600; } }

        /// <summary>Chấm thép: đúng đường kính thanh nhưng không nhỏ hơn 0.7 mm trên giấy.</summary>
        public double DotSize(int dia) { return System.Math.Max(dia, 0.7 * Scale); }

        /// <summary>Tỉ lệ hatch AR-CONC (acadiso.pat) cho bản vẽ mm. 1:20 → 1.0.</summary>
        public double ConcreteHatchScale { get { return 0.05 * Scale; } }
    }
}
