using System;
using System.Collections.Generic;
using System.Linq;
using LynhCad.Manhole.Core.Geometry;
using LynhCad.Manhole.Core.Model;
using LynhCad.Manhole.Core.Rebar;

namespace LynhCad.Manhole.Core.Drawing
{
    /// <summary>Bảng thống kê thép + ghi chú chung.</summary>
    public static class ScheduleBuilder
    {
        public static DrawingModel BuildTable(ManholeGeometry g, RebarSchedule sch, DrawStyle st)
        {
            var m = new DrawingModel();
            double s = st.Scale;
            var t = new PTable
            {
                Layer = Layers.Table,
                Title = "BAR SCHEDULE - " + (g.P.Id ?? "MH"),
                ColumnWidths = new[] { 14, 12, 18, 16, 16, 16, 16, 18, 12, 20, 18 }.Select(w => w * s).ToArray(),
                RowHeight = 6 * s,
                TextHeight = 2.5 * s,
            };
            t.Rows.Add(new[] { "MARK", "SIZE", "SHAPE", "A", "B", "C", "D", "COG", "NO.", "LENGTH", "MASS (kg)" });
            foreach (var bm in sch.Marks)
            {
                bool hasC = bm.Shape == BarShape.U || bm.Shape == BarShape.Cranked || bm.Shape == BarShape.Dropped;
                t.Rows.Add(new[]
                {
                    bm.Mark, bm.Size, ShapeName(bm.Shape),
                    bm.A.ToString("0"),
                    bm.Shape == BarShape.Straight ? "-" : bm.B.ToString("0"),
                    hasC ? bm.C.ToString("0") : "-",
                    bm.Shape == BarShape.Dropped ? bm.D.ToString("0") : "-",
                    CogText(bm),
                    bm.Count.ToString(), bm.Length.ToString("0"), bm.Mass.ToString("0.0"),
                });
            }
            t.Rows.Add(new[] { "TOTAL", "", "", "", "", "", "", "", sch.Marks.Sum(x => x.Count).ToString(), "", sch.TotalMass.ToString("0.0") });
            m.Add(t);
            return m;
        }

        /// <summary>Cột COG: "-" không móc, "2x170" hai đầu bằng nhau, "170+120" hai đầu khác nhau, "1x170" một đầu.</summary>
        private static string CogText(BarMark bm)
        {
            if (bm.CogStart <= 0 && bm.CogEnd <= 0) return "-";
            if (bm.CogStart > 0 && bm.CogEnd > 0)
                return Math.Abs(bm.CogStart - bm.CogEnd) < 0.5
                    ? string.Format("2x{0:0}", bm.CogStart)
                    : string.Format("{0:0}+{1:0}", bm.CogStart, bm.CogEnd);
            return string.Format("1x{0:0}", Math.Max(bm.CogStart, bm.CogEnd));
        }

        private static string ShapeName(BarShape s)
        {
            switch (s)
            {
                case BarShape.L: return "L";
                case BarShape.U: return "U";
                case BarShape.Ring: return "RING";
                case BarShape.Cranked: return "CRANKED";
                case BarShape.Dropped: return "DROPPED";
                default: return "STRAIGHT";
            }
        }

        /// <summary>Số ký tự tối đa mỗi dòng ghi chú chung.</summary>
        public const int NoteWrap = 95;

        /// <summary>Tách đoạn văn thành các dòng ≤ max ký tự theo khoảng trắng (từ dài hơn max để nguyên một dòng).</summary>
        public static List<string> Wrap(string text, int max)
        {
            var result = new List<string>();
            var line = "";
            foreach (var word in (text ?? "").Split(' '))
            {
                if (word.Length == 0) continue;
                if (line.Length > 0 && line.Length + 1 + word.Length > max) { result.Add(line); line = word; }
                else line = line.Length == 0 ? word : line + " " + word;
            }
            if (line.Length > 0 || result.Count == 0) result.Add(line);
            return result;
        }

        public static DrawingModel BuildNotes(ManholeGeometry g, DrawStyle st)
        {
            var m = new DrawingModel();
            var P = g.P;
            var notes = new List<string>
            {
                "ALL DIMENSIONS IN MILLIMETRES AND LEVELS IN METRES (AHD) UNLESS NOTED OTHERWISE.",
                string.Format("CONCRETE {0}. COVER TO REINFORCEMENT {1:0} UNLESS NOTED OTHERWISE.", P.ConcreteGrade, P.Cover),
                "REINFORCEMENT TO AS/NZS 4671 GRADE D500N.",
                string.Format("LAP LENGTHS {0:0}d MINIMUM UNLESS NOTED OTHERWISE.", P.LapFactor),
                string.Format("BAR DIMENSIONS ARE TO BAR CENTRELINE. MINIMUM INTERNAL BEND DIAMETER {0:0}d (AS 3600 TABLE 17.2.3.2); "
                    + "SCHEDULED LENGTHS ALLOW FOR BENDS.", P.BendFactor),
                "REINFORCEMENT INTERRUPTED BY OPENINGS SHALL BE CUT AND TRIMMER BARS PROVIDED AS SHOWN.",
                "PIPE PENETRATIONS SHALL BE SEALED WITH NON-SHRINK GROUT AND FLEXIBLE JOINT TO PIPE SUPPLIER'S DETAILS.",
            };
            if (P.Cogs)
                notes.Add("COG = STANDARD 90° COG TO AS 3600 CL 13.1.2.7, LENGTH AS SCHEDULED (SHORTENED WHERE LIMITED BY SLAB DEPTH). "
                    + "SLAB BARS COGGED AT BOTH ENDS (BOTTOM BARS UP, TOP BARS DOWN); WALL VERTICAL BARS COGGED INTO ROOF SLAB.");
            if (g.HasRebate)
                notes.Add(string.Format("ROOF SLAB REBATED {0:0} DEEP AROUND THE ACCESS OPENING FOR THE COVER FRAME. "
                    + "TOP REINFORCEMENT UNDER THE REBATE IS CRANKED DOWN {0:0} AT 1:{1:0} (CRANKED / DROPPED BARS: B = CRANK DEPTH).",
                    g.RebateDepth, RebarDesigner.CrankSlope));
            if (g.IsPrecast)
                notes.Add("PRECAST MANHOLE COMPONENTS TO WSA 02 / AUTHORITY STANDARD DRAWINGS AND MANUFACTURER'S DETAILS.");
            if (g.Steps != null)
                notes.Add(string.Format("STEP IRONS GALVANISED OR PLASTIC ENCAPSULATED TO AUTHORITY STANDARD, AT {0:0} CTS, {1:0} WIDE, {2:0} PROJECTION.",
                    g.Steps.Spacing, g.Steps.Width, g.Steps.Projection));
            notes.Add("DRAFT GENERATED BY LYNHCADTOOLSBOX - TO BE CHECKED BY THE DESIGN ENGINEER.");

            // đánh số tự động: thêm/bớt ghi chú không phải sửa số thứ tự
            // xuống dòng ~NoteWrap ký tự, dòng sau thụt lề thẳng sau số thứ tự (khối ghi chú vừa khổ A3)
            var lines = new List<string> { "NOTES:" };
            for (int i = 0; i < notes.Count; i++)
            {
                string head = (i + 1) + ". ";
                var parts = Wrap(notes[i], NoteWrap - head.Length);
                for (int j = 0; j < parts.Count; j++)
                    lines.Add((j == 0 ? head : new string(' ', head.Length + 1)) + parts[j]);
            }

            m.Add(new PText
            {
                Layer = Layers.Text, Position = Vec2.Zero, Height = st.TextSmall, VAlign = TextVAlign.Top,
                Align = TextAlign.Left, Text = string.Join("\n", lines),
            });
            return m;
        }
    }
}
