using System;
using System.Collections.Generic;
using System.Linq;
using LynhCad.Manhole.Core.Geometry;
using LynhCad.Manhole.Core.Model;
using LynhCad.Manhole.Core.Rebar;

namespace LynhCad.Manhole.Core.Drawing
{
    /// <summary>
    /// MẶT BẰNG: cắt ngang qua cao độ ống. Tường cắt (hatch), ống xuyên tường, thép tường (hố chữ nhật),
    /// lỗ thăm phía trên & bản đáy phía dưới (nét khuất), đường cắt A-A, kích thước.
    /// Tâm hố tại gốc toạ độ.
    /// </summary>
    public class PlanBuilder : ViewBuilder
    {
        public PlanBuilder(ManholeGeometry geometry, RebarDesign rebar, DrawStyle style) : base(geometry, rebar, style) { }

        public override DrawingModel Build()
        {
            if (g.IsRect) BuildRectWalls(); else BuildCircularWalls();
            BuildHiddenOutlines();
            BuildPipes();
            if (g.IsRect) BuildWallRebar();
            else if (g.HasCastWalls) BuildRingWallRebar();
            BuildSteps();
            BuildDims();
            BuildSectionMarker();
            PlaceCallouts();
            return m;
        }

        // Toạ độ biên ngoài của hình (gồm đoạn ống thò ra) theo từng phía
        private double ExtentX
        {
            get
            {
                double e = Math.Max(g.BaseHalfX, g.OuterHalfX);
                foreach (var f in g.Pipes) e = Math.Max(e, Math.Abs((f.OuterPoint + f.Dir * st.PipeStub).X) + f.OD / 2);
                return e;
            }
        }

        private double ExtentY
        {
            get
            {
                double e = Math.Max(g.BaseHalfY, g.OuterHalfY);
                foreach (var f in g.Pipes) e = Math.Max(e, Math.Abs((f.OuterPoint + f.Dir * st.PipeStub).Y) + f.OD / 2);
                return e;
            }
        }

        // ---------------------------------------------------------------- tường chữ nhật
        private void BuildRectWalls()
        {
            double li = g.Li / 2, wi = g.Wi / 2, lo = g.Lo / 2, wo = g.Wo / 2;
            var pieces = new List<Rect2>();
            pieces.AddRange(SplitWall(new Rect2(-lo, wi, lo, wo), WallSide.North, true));
            pieces.AddRange(SplitWall(new Rect2(-lo, -wo, lo, -wi), WallSide.South, true));
            pieces.AddRange(SplitWall(new Rect2(li, -wi, lo, wi), WallSide.East, false));
            pieces.AddRange(SplitWall(new Rect2(-lo, -wi, -li, wi), WallSide.West, false));

            foreach (var r in pieces) m.Add(ConcreteHatch(r));
            foreach (var e in GeomUtil.UnionOutline(pieces)) m.Add(PPolyline.Line(e.Item1, e.Item2, Layers.Concrete));
        }

        /// <summary>Tách một bức tường thành các mảnh, chừa lỗ cho ống xuyên qua tường đó.</summary>
        private IEnumerable<Rect2> SplitWall(Rect2 wall, WallSide side, bool alongX)
        {
            var gaps = g.Pipes.Where(f => f.Spec.Wall == side)
                              .Select(f => new Interval(f.Spec.Offset - f.HalfGap, f.Spec.Offset + f.HalfGap));
            var span = alongX ? new Interval(wall.X1, wall.X2) : new Interval(wall.Y1, wall.Y2);
            foreach (var seg in span.Subtract(gaps))
                yield return alongX ? new Rect2(seg.Min, wall.Y1, seg.Max, wall.Y2)
                                    : new Rect2(wall.X1, seg.Min, wall.X2, seg.Max);
        }

        // ---------------------------------------------------------------- vách tròn
        private void BuildCircularWalls()
        {
            var gapsO = g.AngularGaps(g.Ro);
            var gapsI = g.AngularGaps(g.Ri);
            int n = gapsO.Count;
            for (int k = 0; k < n; k++)
            {
                int k2 = (k + 1) % n;
                double o0 = gapsO[k].Max, o1 = gapsO[k2].Min;
                double i0 = gapsI[k].Max, i1 = gapsI[k2].Min;
                if (k2 <= k) { o1 += 2 * Math.PI; i1 += 2 * Math.PI; }
                if (o1 - o0 < 1e-6) continue;

                var loop = new PPolyline { Layer = Layers.Hatch, Closed = true };
                loop.AddArc(Vec2.Zero, g.Ro, o0, o1);
                loop.AddArc(Vec2.Zero, g.Ri, i1, i0);
                m.Add(ConcreteHatch(loop));

                m.Add(new PArc { Layer = Layers.Concrete, Center = Vec2.Zero, Radius = g.Ro, StartAngle = o0, EndAngle = o1 });
                m.Add(new PArc { Layer = Layers.Concrete, Center = Vec2.Zero, Radius = g.Ri, StartAngle = i0, EndAngle = i1 });
            }
            // cạnh lỗ xuyên (song song trục ống)
            for (int k = 0; k < n; k++)
            {
                m.Add(PPolyline.Line(Vec2.FromAngle(gapsO[k].Min) * g.Ro, Vec2.FromAngle(gapsI[k].Min) * g.Ri, Layers.Concrete));
                m.Add(PPolyline.Line(Vec2.FromAngle(gapsO[k].Max) * g.Ro, Vec2.FromAngle(gapsI[k].Max) * g.Ri, Layers.Concrete));
            }
            callouts.Add(new Annot.Callout(Vec2.FromAngle(1.25 * Math.PI) * (g.Ri + g.Tw / 2),
                string.Format("{0}\nID {1:0}, WALL {2:0}", g.IsPrecast ? "PRECAST MANHOLE SHAFT" : "CAST IN-SITU RC WALL", g.P.InternalDiameter, g.Tw), false));
        }

        // ---------------------------------------------------------------- nét khuất
        private void BuildHiddenOutlines()
        {
            // Bản đáy (phía dưới)
            if (g.BaseHalfX > g.OuterHalfX + 1e-6 || g.IsRect == false)
                m.Add(PPolyline.Rect(Rect2.Centered(0, 0, 2 * g.BaseHalfX, 2 * g.BaseHalfY), Layers.ConcreteHidden));

            // Lỗ thăm (phía trên)
            var o = g.OpeningCenter;
            if (g.OpeningIsCircle)
                m.Add(new PCircle { Layer = Layers.ConcreteHidden, Center = o, Radius = g.OpeningHalfX });
            else
                m.Add(PPolyline.Rect(Rect2.Centered(o.X, o.Y, 2 * g.OpeningHalfX, 2 * g.OpeningHalfY), Layers.ConcreteHidden));
            if (g.HasRebate)
            {
                if (g.OpeningIsCircle)
                    m.Add(new PCircle { Layer = Layers.ConcreteHidden, Center = o, Radius = g.RebateHalfX });
                else
                    m.Add(PPolyline.Rect(Rect2.Centered(o.X, o.Y, 2 * g.RebateHalfX, 2 * g.RebateHalfY), Layers.ConcreteHidden));
            }
            callouts.Add(new Annot.Callout(o + new Vec2(g.OpeningHalfX * 0.7071, g.OpeningHalfY * 0.7071) * (g.OpeningIsCircle ? 1 : 1.41),
                g.OpeningIsCircle ? string.Format("Ø{0:0} ACCESS OPENING OVER", 2 * g.OpeningHalfX)
                                  : string.Format("{0:0}x{1:0} ACCESS OPENING OVER", 2 * g.OpeningHalfX, 2 * g.OpeningHalfY), false));

            // Máng dẫn dòng: tim máng từ mỗi ống vào → tâm → ống ra
            foreach (var f in g.Pipes)
                m.Add(PPolyline.Line(f.InnerPoint, ChannelHub(), Layers.ConcreteBeyond));
            callouts.Add(new Annot.Callout(ChannelHub() + (g.Outlet.InnerPoint - ChannelHub()) * 0.5,
                "CHANNEL & BENCHING\n(FALL 1:12 TO CHANNEL)", false));
        }

        private Vec2 ChannelHub()
        {
            // điểm gặp nhau của các máng: hình chiếu tâm hố lên trục ống ra
            var o = g.Outlet;
            return o.AxisPoint;
        }

        // ---------------------------------------------------------------- ống
        private void BuildPipes()
        {
            foreach (var f in g.Pipes)
            {
                var n = f.Dir.Perp();
                var a = f.InnerPoint;
                var b = f.OuterPoint + f.Dir * st.PipeStub;
                foreach (var half in new[] { f.OD / 2, -f.OD / 2, f.DN / 2, -f.DN / 2 })
                    m.Add(PPolyline.Line(a + n * half, b + n * half, Layers.Pipe));
                m.Add(PPolyline.Line(a - f.Dir * 100, b + f.Dir * 150, Layers.Centre));
                Annot.BreakLine(m, b - n * (f.OD / 2), b + n * (f.OD / 2), Math.Min(f.OD * 0.5, 4 * st.Scale), Layers.Pipe);

                string role = f.IsOutlet ? "OUTLET" : "INLET";
                string txt = string.Format("{0} DN{1:0} {2} {3}\nIL {4:0.000}", f.Spec.Mark, f.DN, f.Spec.Material ?? "", role, f.Spec.InvertLevel).Replace("  ", " ");
                callouts.Add(new Annot.Callout(f.OuterPoint + f.Dir * (st.PipeStub * 0.6) + n * (f.OD / 2), txt, false));
            }
        }

        // ---------------------------------------------------------------- thép tường
        private void BuildWallRebar()
        {
            var L = rd.Layout;
            double c = L.Cover;

            // Thép ngang: vòng trong & vòng ngoài, đứt tại lỗ ống
            var horizLoops = RebarDesigner.HorizontalLoops(g, L);
            double rb = L.HorizBendRadius;
            foreach (var loop in horizLoops)
            {
                double bx = loop.X, by = loop.Y;
                // các cạnh lùi vào rb ở góc, góc vẽ cung bán kính tim uốn
                DrawLoopSide(new Vec2(-bx + rb, by), new Vec2(bx - rb, by), WallSide.North, true, c);
                DrawLoopSide(new Vec2(-bx + rb, -by), new Vec2(bx - rb, -by), WallSide.South, true, c);
                DrawLoopSide(new Vec2(bx, -by + rb), new Vec2(bx, by - rb), WallSide.East, false, c);
                DrawLoopSide(new Vec2(-bx, -by + rb), new Vec2(-bx, by - rb), WallSide.West, false, c);
                if (rb > 1e-9)
                    foreach (int sx in new[] { -1, 1 })
                        foreach (int sy in new[] { -1, 1 })
                        {
                            var centre = new Vec2(sx * (bx - rb), sy * (by - rb));
                            double a0 = sx > 0 ? (sy > 0 ? 0 : -Math.PI / 2) : (sy > 0 ? Math.PI / 2 : Math.PI);
                            var arc = new PPolyline { Layer = Layers.Rebar, Key = RebarKeys.WallHorizontal };
                            arc.AddArc(centre, rb, a0, a0 + Math.PI / 2);
                            m.Add(arc);
                        }
            }

            // Thép đứng: chấm, các mặt thẳng hàng với nhau (cùng vị trí dọc tường)
            Vec2? sampleDot = null;
            double xInner = RebarDesigner.VerticalLoops(g, L)[0].X;
            foreach (var p in RebarDesigner.VerticalPositions(g, L))
            {
                if (InPipeGap(p, c)) continue;
                m.Add(new PDot { Layer = Layers.Rebar, Center = p, Diameter = st.DotSize(L.DV), Key = RebarKeys.WallVertical });
                if (sampleDot == null && p.Y < 0 && Math.Abs(p.X + xInner) < 1e-6) sampleDot = p;
            }

            var outer = horizLoops[horizLoops.Count - 1];
            callouts.Add(new Annot.Callout(new Vec2(-outer.X, -g.Wi / 4), rd.Schedule.Callout(BarSets.WallHoriz)));
            if (sampleDot != null) callouts.Add(new Annot.Callout(sampleDot.Value, rd.Schedule.Callout(BarSets.WallVert)));
        }

        private void BuildSteps()
        {
            var s = g.Steps;
            if (s == null) return;
            m.Add(StepsDraw.PlanU(s, Layers.Steps));
            callouts.Add(new Annot.Callout(s.RungCentre, StepsDraw.Callout(s), false));
        }

        /// <summary>Hố tròn đổ tại chỗ: thép ngang là các vòng tròn (đứt tại lỗ ống), thép đứng là chấm trên hai vòng.</summary>
        private void BuildRingWallRebar()
        {
            var L = rd.Layout;
            double c = L.Cover;

            var horiz = RebarDesigner.HorizontalRingRadii(g, L);
            foreach (var r in horiz)
            {
                var gaps = g.AngularGaps(r, c);
                int n = gaps.Count;
                for (int k = 0; k < n; k++)
                {
                    int k2 = (k + 1) % n;
                    double a0 = gaps[k].Max, a1 = gaps[k2].Min;
                    if (k2 <= k) a1 += 2 * Math.PI;
                    if (a1 - a0 < 1e-6) continue;
                    m.Add(new PArc { Layer = Layers.Rebar, Center = Vec2.Zero, Radius = r, StartAngle = a0, EndAngle = a1, Key = RebarKeys.WallHorizontal });
                }
            }

            Vec2? sampleDot = null;
            double rInner = RebarDesigner.VerticalRingRadii(g, L)[0];
            foreach (var p in RebarDesigner.VerticalPositions(g, L))
            {
                if (InPipeGap(p, c)) continue;
                m.Add(new PDot { Layer = Layers.Rebar, Center = p, Diameter = st.DotSize(L.DV), Key = RebarKeys.WallVertical });
                if (sampleDot == null && Math.Abs(p.Length - rInner) < 1e-6 && p.X < 0 && p.Y < 0) sampleDot = p;
            }

            callouts.Add(new Annot.Callout(Vec2.FromAngle(0.8 * Math.PI) * horiz[horiz.Count - 1], rd.Schedule.Callout(BarSets.WallHoriz)));
            if (sampleDot != null) callouts.Add(new Annot.Callout(sampleDot.Value, rd.Schedule.Callout(BarSets.WallVert)));
        }

        private void DrawLoopSide(Vec2 a, Vec2 b, WallSide side, bool alongX, double c)
        {
            var span = alongX ? new Interval(a.X, b.X) : new Interval(a.Y, b.Y);
            var gaps = g.Pipes.Where(f => f.Spec.Wall == side)
                              .Select(f => new Interval(f.Spec.Offset - f.HalfGap - c, f.Spec.Offset + f.HalfGap + c));
            foreach (var seg in span.Subtract(gaps))
            {
                var p1 = alongX ? new Vec2(seg.Min, a.Y) : new Vec2(a.X, seg.Min);
                var p2 = alongX ? new Vec2(seg.Max, a.Y) : new Vec2(a.X, seg.Max);
                m.Add(PPolyline.Line(p1, p2, Layers.Rebar)).Key = RebarKeys.WallHorizontal;
            }
        }

        private bool InPipeGap(Vec2 p, double c)
        {
            foreach (var f in g.Pipes)
            {
                var rel = p - f.AxisPoint;
                double along = rel.Dot(f.Dir), across = Math.Abs(rel.Dot(f.Dir.Perp()));
                if (across < f.HalfGap + c && along > f.InnerDist - 1 && along < f.OuterDist + 1) return true;
            }
            return false;
        }

        // ---------------------------------------------------------------- kích thước
        private void BuildDims()
        {
            double yBase = -ExtentY - st.DimOffset;
            double xBase = -ExtentX - st.DimOffset;
            double t = st.DimStep;

            if (g.IsRect)
            {
                double li = g.Li / 2, lo = g.Lo / 2, wi = g.Wi / 2, wo = g.Wo / 2;
                Annot.DimH(m, -lo, -li, -wo, yBase);
                Annot.DimH(m, -li, li, -wo, yBase);
                Annot.DimH(m, li, lo, -wo, yBase);
                Annot.DimH(m, -lo, lo, -wo, yBase - t);
                Annot.DimV(m, -wo, -wi, -lo, xBase);
                Annot.DimV(m, -wi, wi, -lo, xBase);
                Annot.DimV(m, wi, wo, -lo, xBase);
                Annot.DimV(m, -wo, wo, -lo, xBase - t);
                if (g.BaseHalfX > lo + 1e-6)
                {
                    Annot.DimH(m, -g.BaseHalfX, g.BaseHalfX, -g.BaseHalfY, yBase - 2 * t);
                    Annot.DimV(m, -g.BaseHalfY, g.BaseHalfY, -g.BaseHalfX, xBase - 2 * t);
                }
            }
            else
            {
                Annot.DimH(m, -g.Ro, -g.Ri, 0, yBase);
                Annot.DimH(m, -g.Ri, g.Ri, 0, yBase);
                Annot.DimH(m, g.Ri, g.Ro, 0, yBase);
                Annot.DimH(m, -g.BaseHalfX, g.BaseHalfX, -g.BaseHalfY, yBase - t);
                Annot.DimV(m, -g.BaseHalfY, g.BaseHalfY, -g.BaseHalfX, xBase);
            }
        }

        // ---------------------------------------------------------------- đường cắt
        private void BuildSectionMarker()
        {
            var u = g.SectionDir; var d = g.ViewDir;
            var origin = d * g.SectionOffset;
            double ext = Math.Max(ExtentX, ExtentY) + 6 * st.Scale;
            Annot.SectionMarker(m, st, origin - u * ext, origin + u * ext, d, "A");
        }

        private void PlaceCallouts()
        {
            double ext = Math.Max(ExtentX, ExtentY) + 14 * st.Scale;
            PlaceCalloutColumns(0, -ext, ext);
        }
    }
}
