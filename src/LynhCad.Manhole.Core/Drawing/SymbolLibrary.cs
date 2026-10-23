using System;
using System.Collections.Generic;
using LynhCad.Manhole.Core.Geometry;

namespace LynhCad.Manhole.Core.Drawing
{
    /// <summary>
    /// Định nghĩa các block ký hiệu (vẽ theo mm giấy, gốc = điểm chèn, layer "0" để lấy màu theo layer của block chèn).
    /// PText có AttributeTag là attribute của block. Plugin tạo BlockTableRecord từ đây; SVG / preview khai triển bằng Expand.
    /// </summary>
    public static class SymbolLibrary
    {
        /// <summary>Ký hiệu cao độ: tam giác chỉ xuống tại gốc, gạch ngang sang phải, chữ "LABEL RL xx.xxx" phía trên.</summary>
        public const string LevelRight = "LCT_LEVEL";
        /// <summary>Như LevelRight nhưng gạch ngang và chữ sang trái.</summary>
        public const string LevelLeft = "LCT_LEVEL_L";
        /// <summary>Ký hiệu cắt: nét đậm từ gốc theo +X, mũi tên chỉ +Y, chữ cái phía -Y.</summary>
        public const string SectionRight = "LCT_SECTION";
        /// <summary>Như SectionRight nhưng nét đậm theo -X.</summary>
        public const string SectionLeft = "LCT_SECTION_L";

        public const string LevelTag = "LEVEL";
        public const string SectionTag = "LETTER";

        public const double LevelLineLength = 26;

        public static DrawingModel Get(string name)
        {
            switch (name)
            {
                case LevelRight: return Level(1);
                case LevelLeft: return Level(-1);
                case SectionRight: return Section(1);
                case SectionLeft: return Section(-1);
                default:
                    var sheet = SheetFormats.ForBlock(name);
                    if (sheet != null) return SheetFormats.Block(sheet);
                    throw new KeyNotFoundException("Không có block " + name);
            }
        }

        private static DrawingModel Level(int side)
        {
            var m = new DrawingModel();
            const double t = 1.8, h = 2.5;
            var tri = new PPolyline { Layer = "0", Closed = true };
            tri.Add(Vec2.Zero).Add(new Vec2(-t * 0.6, t)).Add(new Vec2(t * 0.6, t));
            m.Add(tri);
            m.Add(new PHatch { Layer = "0", Pattern = "SOLID", Loops = { Clone(tri) } });
            m.Add(PPolyline.Line(new Vec2(0, t), new Vec2(side * LevelLineLength, t), "0"));
            m.Add(new PText
            {
                Layer = "0", Height = h, VAlign = TextVAlign.Bottom,
                Position = new Vec2(side * t * 1.2, t * 1.4),
                Align = side > 0 ? TextAlign.Left : TextAlign.Right,
                Text = "RL 0.000", AttributeTag = LevelTag,
            });
            return m;
        }

        private static DrawingModel Section(int side)
        {
            var m = new DrawingModel();
            const double seg = 8, tip = 6, baseD = 3, half = 1.2, letter = 3.5, h = 5;
            m.Add(PPolyline.Line(Vec2.Zero, new Vec2(side * seg, 0), "0"));
            var arrow = new PPolyline { Layer = "0", Closed = true };
            arrow.Add(new Vec2(0, tip)).Add(new Vec2(half, baseD)).Add(new Vec2(-half, baseD));
            m.Add(arrow);
            m.Add(new PHatch { Layer = "0", Pattern = "SOLID", Loops = { Clone(arrow) } });
            m.Add(PPolyline.Line(Vec2.Zero, new Vec2(0, baseD), "0"));
            m.Add(new PText
            {
                Layer = "0", Height = h, Align = TextAlign.Center, Position = new Vec2(0, -letter),
                Text = "A", AttributeTag = SectionTag, KeepUpright = true,
            });
            return m;
        }

        private static PPolyline Clone(PPolyline p)
        {
            var c = new PPolyline { Layer = p.Layer, Closed = p.Closed };
            for (int i = 0; i < p.Points.Count; i++) c.Add(p.Points[i], p.Bulges[i]);
            return c;
        }

        /// <summary>
        /// Khai triển một block chèn thành primitive trong toạ độ model: định nghĩa × Scale, quay Rotation, dời tới Position;
        /// attribute thay bằng giá trị trong Attributes; mọi primitive lấy layer của block.
        /// </summary>
        public static DrawingModel Expand(PBlock b)
        {
            var def = Get(b.Name);
            var t = b.Placement;
            foreach (var p in def.Items)
            {
                p.Layer = b.Layer;
                var txt = p as PText;
                if (txt != null && txt.AttributeTag != null)
                {
                    string v;
                    if (b.Attributes.TryGetValue(txt.AttributeTag, out v)) txt.Text = v;
                }
                p.Transform(t);
            }
            return def;
        }
    }
}
