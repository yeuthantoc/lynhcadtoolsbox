using System;
using System.Collections.Generic;
using LynhCad.Manhole.Core.Geometry;
using LynhCad.Manhole.Core.Model;

namespace LynhCad.Manhole.Core.Drawing
{
    /// <summary>Kích thước một khổ giấy và vị trí khung viền / ô tên / vùng vẽ (mm giấy, gốc = góc trái dưới tờ giấy).</summary>
    public class SheetFormat
    {
        public SheetSize Size;
        public string BlockName;
        public double Width, Height;
        /// <summary>Khung viền (lề trái rộng hơn để đóng gáy).</summary>
        public Rect2 Frame;
        /// <summary>Ô tên ở góc phải dưới trong khung.</summary>
        public Rect2 Panel;
        /// <summary>Vùng đặt viewport: trong khung, phía trên ô tên.</summary>
        public Rect2 DrawArea;
        /// <summary>Hệ số kích thước ô tên so với bản A1, và cỡ chữ (nhãn / giá trị / tên bản vẽ / số bản vẽ).</summary>
        public double F, LabelH, ValueH, TitleH, BigH;
        /// <summary>Tên khổ giấy chuẩn trong "DWG To PDF.pc3" (full bleed: vùng in = cả tờ, gốc (0,0) = góc trái dưới).</summary>
        public string CanonicalMedia;
    }

    /// <summary>
    /// Khổ giấy A1 / A3 và block khung tên tương ứng (LCT_TITLE_A1 / LCT_TITLE_A3) với các attribute.
    /// Người dùng có thể vẽ lại block theo mẫu công ty: plugin giữ block đã có trong bản vẽ, chỉ điền attribute theo tag.
    /// </summary>
    public static class SheetFormats
    {
        public const string BlockA1 = "LCT_TITLE_A1";
        public const string BlockA3 = "LCT_TITLE_A3";

        public const string TagCompany = "COMPANY", TagProject = "PROJECT", TagClient = "CLIENT",
            TagTitle1 = "TITLE1", TagTitle2 = "TITLE2", TagDwgNo = "DWGNO", TagRev = "REV", TagSheet = "SHEET",
            TagScale = "SCALE", TagDate = "DATE", TagDrawn = "DRAWN", TagDesigned = "DESIGNED",
            TagChecked = "CHECKED", TagApproved = "APPROVED";

        public static SheetFormat Get(SheetSize size)
        {
            switch (size)
            {
                case SheetSize.A1: return Make(size, BlockA1, 841, 594, 20, 10, 1.0, 1.8, 3.0, 3.5, 5.0, "ISO_full_bleed_A1_(841.00_x_594.00_MM)");
                case SheetSize.A3: return Make(size, BlockA3, 420, 297, 15, 7, 0.6, 1.3, 2.0, 2.5, 3.0, "ISO_full_bleed_A3_(420.00_x_297.00_MM)");
                default: return null;
            }
        }

        public static SheetFormat ForBlock(string blockName)
        {
            if (blockName == BlockA1) return Get(SheetSize.A1);
            if (blockName == BlockA3) return Get(SheetSize.A3);
            return null;
        }

        private static SheetFormat Make(SheetSize size, string block, double w, double h, double left, double margin,
            double f, double lab, double val, double title, double big, string media)
        {
            var frame = new Rect2(left, margin, w - margin, h - margin);
            double pw = 400 * f, ph = 50 * f, gap = 5 * f;
            var panel = new Rect2(frame.X2 - pw, frame.Y1, frame.X2, frame.Y1 + ph);
            return new SheetFormat
            {
                Size = size, BlockName = block, Width = w, Height = h, Frame = frame, Panel = panel,
                DrawArea = new Rect2(frame.X1 + gap, panel.Y2 + gap, frame.X2 - gap, frame.Y2 - gap),
                F = f, LabelH = lab, ValueH = val, TitleH = title, BigH = big, CanonicalMedia = media,
            };
        }

        /// <summary>Định nghĩa block khung tên (layer "0" để lấy màu / nét theo layer của block chèn).</summary>
        public static DrawingModel Block(SheetFormat s)
        {
            var m = new DrawingModel();
            double f = s.F;
            m.Add(PPolyline.Rect(s.Frame, "0"));
            var p = s.Panel;
            m.Add(PPolyline.Rect(p, "0"));

            // cột A: công ty + lưu ý; cột B: dự án / chủ đầu tư / tên bản vẽ; cột C: số bản vẽ và các ô nhỏ
            double xa = p.X1, xb = xa + 110 * f, xc = xb + 170 * f, x2 = p.X2, y1 = p.Y1, y2 = p.Y2;
            Line(m, xb, y1, xb, y2);
            Line(m, xc, y1, xc, y2);

            Value(m, s, (xa + xb) / 2, y1 + 30 * f, TagCompany, s.BigH);
            m.Add(new PText
            {
                Layer = "0", Height = s.LabelH, Align = TextAlign.Center, VAlign = TextVAlign.Middle,
                Position = new Vec2((xa + xb) / 2, y1 + 9 * f),
                Text = "DO NOT SCALE FROM THIS DRAWING.\nCHECK ALL DIMENSIONS ON SITE.",
            });

            // cột B (từ trên xuống): PROJECT 13, CLIENT 12, DRAWING TITLE 25
            double yb1 = y2 - 13 * f, yb2 = yb1 - 12 * f;
            Line(m, xb, yb1, xc, yb1);
            Line(m, xb, yb2, xc, yb2);
            Cell(m, s, xb, yb1, xc, y2, "PROJECT", TagProject, s.ValueH);
            Cell(m, s, xb, yb2, xc, yb1, "CLIENT", TagClient, s.ValueH);
            Label(m, s, xb, yb2, "DRAWING TITLE");
            Value(m, s, (xb + xc) / 2, yb2 - 10 * f, TagTitle1, s.TitleH);
            Value(m, s, (xb + xc) / 2, yb2 - 18 * f, TagTitle2, s.ValueH);

            // cột C (từ trên xuống): DRAWING No. 14, REV|SHEET|SCALE 12, DATE|DRAWN 12, DESIGNED|CHECKED|APPROVED 12
            double yc1 = y2 - 14 * f, yc2 = yc1 - 12 * f, yc3 = yc2 - 12 * f;
            foreach (var yy in new[] { yc1, yc2, yc3 }) Line(m, xc, yy, x2, yy);
            Cell(m, s, xc, yc1, x2, y2, "DRAWING No.", TagDwgNo, s.BigH);
            double w3 = (x2 - xc) / 3, w2 = (x2 - xc) / 2;
            Cells(m, s, xc, yc2, yc1, w3, new[] { "REV", "SHEET", "SCALE" }, new[] { TagRev, TagSheet, TagScale });
            Cells(m, s, xc, yc3, yc2, w2, new[] { "DATE", "DRAWN" }, new[] { TagDate, TagDrawn });
            Cells(m, s, xc, y1, yc3, w3, new[] { "DESIGNED", "CHECKED", "APPROVED" }, new[] { TagDesigned, TagChecked, TagApproved });
            return m;
        }

        private static void Cells(DrawingModel m, SheetFormat s, double x0, double yb, double yt, double w, string[] labels, string[] tags)
        {
            for (int i = 0; i < labels.Length; i++)
            {
                if (i > 0) Line(m, x0 + i * w, yb, x0 + i * w, yt);
                Cell(m, s, x0 + i * w, yb, x0 + (i + 1) * w, yt, labels[i], tags[i], s.ValueH);
            }
        }

        /// <summary>Ô: nhãn nhỏ góc trên trái, giá trị (attribute) căn giữa phần còn lại.</summary>
        private static void Cell(DrawingModel m, SheetFormat s, double x1, double y1, double x2, double y2, string label, string tag, double h)
        {
            Label(m, s, x1, y2, label);
            double top = y2 - s.LabelH - 1.5 * s.F;
            Value(m, s, (x1 + x2) / 2, (top + y1) / 2, tag, h);
        }

        private static void Label(DrawingModel m, SheetFormat s, double x, double yTop, string text)
        {
            m.Add(new PText { Layer = "0", Height = s.LabelH, Align = TextAlign.Left, VAlign = TextVAlign.Top, Position = new Vec2(x + 1.2 * s.F, yTop - 1.0 * s.F), Text = text });
        }

        private static void Value(DrawingModel m, SheetFormat s, double cx, double cy, string tag, double h)
        {
            m.Add(new PText { Layer = "0", Height = h, Align = TextAlign.Center, VAlign = TextVAlign.Middle, Position = new Vec2(cx, cy), Text = "", AttributeTag = tag });
        }

        private static void Line(DrawingModel m, double x1, double y1, double x2, double y2)
        {
            m.Add(PPolyline.Line(new Vec2(x1, y1), new Vec2(x2, y2), "0"));
        }

        /// <summary>Giá trị attribute khung tên cho một hố ga (ô trống điền "-").</summary>
        public static Dictionary<string, string> Attributes(ManholeParams p, SheetFormat s, double scale, string date)
        {
            Func<string, string> v = x => string.IsNullOrWhiteSpace(x) ? "-" : x.Trim().ToUpperInvariant();
            string id = string.IsNullOrWhiteSpace(p.Id) ? "MH" : p.Id.Trim().ToUpperInvariant();
            return new Dictionary<string, string>
            {
                { TagCompany, v(p.Company) },
                { TagProject, v(p.Project) },
                { TagClient, v(p.Client) },
                { TagTitle1, string.IsNullOrWhiteSpace(p.DrawingTitle) ? "MANHOLE " + id : p.DrawingTitle.Trim().ToUpperInvariant() },
                { TagTitle2, string.IsNullOrWhiteSpace(p.DrawingSubtitle) ? "GENERAL ARRANGEMENT & REINFORCEMENT" : p.DrawingSubtitle.Trim().ToUpperInvariant() },
                { TagDwgNo, v(p.DrawingNumber) },
                { TagRev, v(p.Revision) },
                { TagSheet, s.Size.ToString() },
                { TagScale, "1:" + scale.ToString("0") },
                { TagDate, string.IsNullOrWhiteSpace(date) ? "-" : date },
                { TagDrawn, v(p.DrawnBy) },
                { TagDesigned, v(p.DesignedBy) },
                { TagChecked, v(p.CheckedBy) },
                { TagApproved, v(p.ApprovedBy) },
            };
        }
    }
}
