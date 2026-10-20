using System;
using System.Collections.Generic;
using System.Linq;
using LynhCad.Manhole.Core.Geometry;
using LynhCad.Manhole.Core.Model;
using LynhCad.Manhole.Core.Rebar;

namespace LynhCad.Manhole.Core.Drawing
{
    /// <summary>
    /// MẶT BẰNG THÉP BẢN NẮP: viền bản, lỗ thăm, tường bên dưới (khuất), thép lưới dưới (nét đứt theo quy ước),
    /// thanh gia cường quanh lỗ, kích thước. Lưới trên (nếu có) chỉ ghi chú để bản vẽ không rối.
    /// </summary>
    public class RoofPlanBuilder
    {
        private readonly ManholeGeometry g;
        private readonly RebarDesign rd;
        private readonly DrawStyle st;
        private readonly DrawingModel m = new DrawingModel();
        private readonly List<Annot.Callout> callouts = new List<Annot.Callout>();

        public RoofPlanBuilder(ManholeGeometry geometry, RebarDesign rebar, DrawStyle style)
        {
            g = geometry; rd = rebar; st = style;
        }

        public DrawingModel Build()
        {
            double hx = g.RoofHalfX, hy = g.RoofHalfY;
            var o = g.OpeningCenter;

            m.Add(PPolyline.Rect(Rect2.Centered(0, 0, 2 * hx, 2 * hy), Layers.Concrete));
            if (g.OpeningIsCircle)
                m.Add(new PCircle { Layer = Layers.Concrete, Center = o, Radius = g.OpeningHalfX });
            else
                m.Add(PPolyline.Rect(Rect2.Centered(o.X, o.Y, 2 * g.OpeningHalfX, 2 * g.OpeningHalfY), Layers.Concrete));

            // tường bên dưới
            if (g.IsRect)
            {
                m.Add(PPolyline.Rect(Rect2.Centered(0, 0, g.Li, g.Wi), Layers.ConcreteHidden));
                if (g.RoofHalfX > g.Lo / 2 + 1e-6) m.Add(PPolyline.Rect(Rect2.Centered(0, 0, g.Lo, g.Wo), Layers.ConcreteHidden));
            }
            else
            {
                m.Add(new PCircle { Layer = Layers.ConcreteHidden, Center = Vec2.Zero, Radius = g.Ri });
                m.Add(new PCircle { Layer = Layers.ConcreteHidden, Center = Vec2.Zero, Radius = g.Ro });
            }

            // thép lưới dưới
            foreach (var layer in rd.Layout.RoofLayers.Where(l => !l.IsTop))
            {
                Vec2? sample = null;
                foreach (var pos in layer.Positions)
                    foreach (var piece in layer.Pieces(pos))
                    {
                        var a = layer.AlongX ? new Vec2(piece.Min, pos) : new Vec2(pos, piece.Min);
                        var b = layer.AlongX ? new Vec2(piece.Max, pos) : new Vec2(pos, piece.Max);
                        m.Add(PPolyline.Line(a, b, Layers.RebarHidden));
                        if (sample == null && pos < 0 && piece.Min < 0) sample = a + (b - a) * 0.15;
                    }
                if (sample != null) callouts.Add(new Annot.Callout(sample.Value, rd.Schedule.Callout(layer.Key)));
            }

            // thanh gia cường (mỗi vị trí chỉ vẽ 1 lần dù có cả lưới trên)
            var drawn = new HashSet<string>();
            Vec2? trimSample = null;
            foreach (var t in rd.Layout.RoofTrimmers)
            {
                string key = string.Format("{0}|{1:0}", t.AlongX, t.Pos);
                if (!drawn.Add(key)) continue;
                var a = t.AlongX ? new Vec2(t.Run.Min, t.Pos) : new Vec2(t.Pos, t.Run.Min);
                var b = t.AlongX ? new Vec2(t.Run.Max, t.Pos) : new Vec2(t.Pos, t.Run.Max);
                m.Add(PPolyline.Line(a, b, Layers.Rebar));
                if (trimSample == null && t.AlongX && t.Pos > o.Y) trimSample = new Vec2(t.Run.Max - 50, t.Pos);
            }
            if (trimSample != null) callouts.Add(new Annot.Callout(trimSample.Value, rd.Schedule.Callout(BarSets.RoofTrim)));

            if (g.P.RoofTopMat)
            {
                string top = rd.Schedule.MatCallout(BarSets.RoofTop, true);
                if (top != null)
                    callouts.Add(new Annot.Callout(new Vec2(hx * 0.6, -hy * 0.6), top + "\n(NOT SHOWN FOR CLARITY)", false));
            }

            // kích thước
            double y = -hy - st.DimOffset, x = -hx - st.DimOffset;
            double ox1 = o.X - g.OpeningHalfX, ox2 = o.X + g.OpeningHalfX;
            double oy1 = o.Y - g.OpeningHalfY, oy2 = o.Y + g.OpeningHalfY;
            Annot.DimH(m, -hx, ox1, -hy, y);
            Annot.DimH(m, ox1, ox2, oy1, y);
            Annot.DimH(m, ox2, hx, -hy, y);
            Annot.DimH(m, -hx, hx, -hy, y - st.DimStep);
            Annot.DimV(m, -hy, oy1, -hx, x);
            Annot.DimV(m, oy1, oy2, ox1, x);
            Annot.DimV(m, oy2, hy, -hx, x);
            Annot.DimV(m, -hy, hy, -hx, x - st.DimStep);

            double ext = Math.Max(hx, hy) + 12 * st.Scale;
            Annot.CalloutColumn(m, st, callouts.Where(c => c.Target.X < 0), -ext - st.DimStep, -1);
            Annot.CalloutColumn(m, st, callouts.Where(c => c.Target.X >= 0), ext, +1);
            return m;
        }
    }

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
                ColumnWidths = new[] { 14, 12, 18, 16, 16, 16, 12, 20, 18 }.Select(w => w * s).ToArray(),
                RowHeight = 6 * s,
                TextHeight = 2.5 * s,
            };
            t.Rows.Add(new[] { "MARK", "SIZE", "SHAPE", "A", "B", "C", "NO.", "LENGTH", "MASS (kg)" });
            foreach (var bm in sch.Marks)
            {
                t.Rows.Add(new[]
                {
                    bm.Mark, bm.Size, ShapeName(bm.Shape),
                    bm.A.ToString("0"),
                    bm.Shape == BarShape.Straight ? "-" : bm.B.ToString("0"),
                    bm.Shape == BarShape.U ? bm.C.ToString("0") : "-",
                    bm.Count.ToString(), bm.Length.ToString("0"), bm.Mass.ToString("0.0"),
                });
            }
            t.Rows.Add(new[] { "TOTAL", "", "", "", "", "", sch.Marks.Sum(x => x.Count).ToString(), "", sch.TotalMass.ToString("0.0") });
            m.Add(t);
            return m;
        }

        private static string ShapeName(BarShape s)
        {
            switch (s)
            {
                case BarShape.L: return "L";
                case BarShape.U: return "U";
                default: return "STRAIGHT";
            }
        }

        public static DrawingModel BuildNotes(ManholeGeometry g, DrawStyle st)
        {
            var m = new DrawingModel();
            var P = g.P;
            var lines = new List<string>
            {
                "NOTES:",
                "1. ALL DIMENSIONS IN MILLIMETRES AND LEVELS IN METRES (AHD) UNLESS NOTED OTHERWISE.",
                string.Format("2. CONCRETE {0}. COVER TO REINFORCEMENT {1:0} UNLESS NOTED OTHERWISE.", P.ConcreteGrade, P.Cover),
                "3. REINFORCEMENT TO AS/NZS 4671 GRADE D500N.",
                string.Format("4. LAP LENGTHS {0:0}d MINIMUM UNLESS NOTED OTHERWISE.", P.LapFactor),
                "5. REINFORCEMENT INTERRUPTED BY OPENINGS SHALL BE CUT AND TRIMMER BARS PROVIDED AS SHOWN.",
                "6. PIPE PENETRATIONS SHALL BE SEALED WITH NON-SHRINK GROUT AND FLEXIBLE JOINT TO PIPE SUPPLIER'S DETAILS.",
            };
            if (!g.IsRect)
                lines.Add("7. PRECAST MANHOLE COMPONENTS TO WSA 02 / AUTHORITY STANDARD DRAWINGS AND MANUFACTURER'S DETAILS.");
            lines.Add((g.IsRect ? "7" : "8") + ". DRAFT GENERATED BY LYNHCADTOOLSBOX - TO BE CHECKED BY THE DESIGN ENGINEER.");

            m.Add(new PText
            {
                Layer = Layers.Text, Position = Vec2.Zero, Height = st.TextSmall, VAlign = TextVAlign.Top,
                Align = TextAlign.Left, Text = string.Join("\n", lines),
            });
            return m;
        }
    }
}
