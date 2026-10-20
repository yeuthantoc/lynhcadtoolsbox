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
        /// <summary>Màu hiển thị (preview SVG / WPF) cho một màu ACI.</summary>
        public static string AciToHex(short aci)
        {
            switch (aci)
            {
                case 1: return "#d62828";
                case 2: return "#a07800";
                case 3: return "#2a8a2a";
                case 4: return "#0089a8";
                case 5: return "#1d4ed8";
                case 6: return "#b0179f";
                case 8: return "#888888";
                case 253: return "#bbbbbb";
                default: return "#111111";
            }
        }

        /// <summary>Định nghĩa layer theo tên, null nếu không phải layer chuẩn.</summary>
        public static LayerDef Find(string name)
        {
            foreach (var l in All) if (l.Name == name) return l;
            return null;
        }

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
}
