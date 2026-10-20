using System.Collections.Generic;

namespace LynhCad.Manhole.Core.Drawing
{
    public class LayerDef
    {
        public string Name;
        /// <summary>Màu ACI (AutoCAD Color Index).</summary>
        public short Color;
        public string Linetype;
        /// <summary>Nét in, đơn vị 1/100 mm (vd 35 = 0.35 mm).</summary>
        public int LineWeight;
        public string Description;
    }

    /// <summary>
    /// Bảng layer theo kiểu đặt tên phổ biến cho kết cấu (S-...). Đổi ở đây là đổi cho cả plugin lẫn preview.
    /// </summary>
    public static class Layers
    {
        public const string Concrete = "S-CONC";
        public const string ConcreteBeyond = "S-CONC-BYND";
        public const string ConcreteHidden = "S-CONC-HIDN";
        public const string Hatch = "S-CONC-HATC";
        public const string Rebar = "S-REIN";
        public const string RebarHidden = "S-REIN-HIDN";
        public const string Pipe = "S-PIPE";
        public const string PipeHidden = "S-PIPE-HIDN";
        public const string Centre = "S-CNTR";
        public const string Dim = "S-DIMS";
        public const string Text = "S-TEXT";
        public const string Title = "S-TITL";
        public const string SectionMark = "S-SECT";
        public const string Table = "S-SCHD";

        public static readonly List<LayerDef> All = new List<LayerDef>
        {
            new LayerDef { Name = Concrete,       Color = 7,   Linetype = "Continuous", LineWeight = 35, Description = "Concrete outline (cut)" },
            new LayerDef { Name = ConcreteBeyond, Color = 8,   Linetype = "Continuous", LineWeight = 18, Description = "Concrete visible beyond" },
            new LayerDef { Name = ConcreteHidden, Color = 8,   Linetype = "HIDDEN",     LineWeight = 18, Description = "Concrete hidden" },
            new LayerDef { Name = Hatch,          Color = 253, Linetype = "Continuous", LineWeight = 9,  Description = "Concrete hatch" },
            new LayerDef { Name = Rebar,          Color = 1,   Linetype = "Continuous", LineWeight = 50, Description = "Reinforcement" },
            new LayerDef { Name = RebarHidden,    Color = 1,   Linetype = "HIDDEN",     LineWeight = 35, Description = "Reinforcement (bottom bars in plan)" },
            new LayerDef { Name = Pipe,           Color = 4,   Linetype = "Continuous", LineWeight = 35, Description = "Pipes" },
            new LayerDef { Name = PipeHidden,     Color = 4,   Linetype = "HIDDEN",     LineWeight = 18, Description = "Pipes hidden" },
            new LayerDef { Name = Centre,         Color = 6,   Linetype = "CENTER",     LineWeight = 13, Description = "Centre lines" },
            new LayerDef { Name = Dim,            Color = 3,   Linetype = "Continuous", LineWeight = 18, Description = "Dimensions" },
            new LayerDef { Name = Text,           Color = 2,   Linetype = "Continuous", LineWeight = 18, Description = "Notes & labels" },
            new LayerDef { Name = Title,          Color = 7,   Linetype = "Continuous", LineWeight = 35, Description = "View titles" },
            new LayerDef { Name = SectionMark,    Color = 7,   Linetype = "Continuous", LineWeight = 35, Description = "Section markers" },
            new LayerDef { Name = Table,          Color = 7,   Linetype = "Continuous", LineWeight = 18, Description = "Bar schedule" },
        };
    }

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
