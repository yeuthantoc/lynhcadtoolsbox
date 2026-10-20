using System;
using System.Collections.Generic;
using System.Linq;
using LynhCad.Manhole.Core.Geometry;
using LynhCad.Manhole.Core.Model;
using LynhCad.Manhole.Core.Rebar;

namespace LynhCad.Manhole.Core.Drawing
{
    /// <summary>
    /// MẶT CẮT A-A: cắt dọc theo trục ống ra (ống ra nằm bên phải). Toạ độ vẽ: x = s (dọc phương cắt),
    /// y = z (mm, 0 = mặt bản đáy). Thể hiện bê tông cắt, ống bị cắt / ống phía sau, máng & benching,
    /// thép bản đáy / bản nắp / tường, cao độ, kích thước.
    /// </summary>
    public class SectionBuilder
    {
        private readonly ManholeGeometry g;
        private readonly RebarDesign rd;
        private readonly DrawStyle st;
        private readonly DrawingModel m = new DrawingModel();
        private readonly List<Annot.Callout> callouts = new List<Annot.Callout>();

        private Vec2 u, d, origin;
        private Interval baseS, roofS, outerS, innerS;
        private Interval? openingS;
        private readonly List<PipeFrame> cutLeft = new List<PipeFrame>(), cutRight = new List<PipeFrame>();
        private double zTopRoof, zCL;

        public SectionBuilder(ManholeGeometry geometry, RebarDesign rebar, DrawStyle style)
        {
            g = geometry; rd = rebar; st = style;
        }

        public DrawingModel Build()
        {
            Setup();
            BuildConcrete();
            BuildBenching();
            BuildPipes();
            BuildRebar();
            BuildCoverFrame();
            BuildLevels();
            BuildDims();
            PlaceCallouts();
            return m;
        }

        private double S(Vec2 p) { return (p - origin).Dot(u); }

        private void Setup()
        {
            u = g.SectionDir; d = g.ViewDir;
            origin = d * g.SectionOffset;
            zTopRoof = g.H + g.Ts;
            zCL = g.Z(g.P.CoverLevel);

            baseS = Clip(g.BaseHalfX, g.BaseHalfY);
            roofS = Clip(g.RoofHalfX, g.RoofHalfY);
            if (g.IsRect)
            {
                outerS = Clip(g.Lo / 2, g.Wo / 2);
                innerS = Clip(g.Li / 2, g.Wi / 2);
            }
            else
            {
                outerS = new Interval(-g.Ro, g.Ro);
                innerS = new Interval(-g.Ri, g.Ri);
            }

            // Lỗ thăm trên đường cắt
            var oc = g.OpeningCenter;
            if (g.OpeningIsCircle)
            {
                double delta = Math.Abs((oc - origin).Dot(d));
                double h = GeomUtil.HalfChord(g.OpeningHalfX, delta);
                if (h > 1) openingS = new Interval(S(oc) - h, S(oc) + h);
            }
            else
            {
                var clip = GeomUtil.LineRectClip(origin - oc, u, g.OpeningHalfX, g.OpeningHalfY);
                // clip là tham số t dọc u: điểm = origin + t·u, mà S(origin) = 0 → s = t
                if (clip != null) openingS = clip.Value;
            }

            foreach (var f in g.Pipes)
            {
                if (!IsCut(f)) continue;
                if (f.Dir.Dot(u) > 0) cutRight.Add(f); else cutLeft.Add(f);
            }
        }

        private Interval Clip(double hx, double hy)
        {
            var c = GeomUtil.LineRectClip(origin, u, hx, hy);
            return c ?? new Interval(-hx, hx);
        }

        /// <summary>Ống nằm đúng trên đường cắt (song song phương cắt và trục trùng đường cắt).</summary>
        private bool IsCut(PipeFrame f)
        {
            return Math.Abs(Math.Abs(f.Dir.Dot(u)) - 1) < 1e-3 && Math.Abs((f.AxisPoint - origin).Dot(d)) < 1.0;
        }

        // ---------------------------------------------------------------- bê tông
        private void BuildConcrete()
        {
            var baseR = new Rect2(baseS.Min, -g.Tb, baseS.Max, 0);
            var roofPieces = roofS.Subtract(openingS.HasValue ? new[] { openingS.Value } : new Interval[0])
                                  .Select(i => new Rect2(i.Min, g.H, i.Max, zTopRoof)).ToList();

            var leftWall = new Interval(outerS.Min, innerS.Min);
            var rightWall = new Interval(innerS.Max, outerS.Max);

            if (g.IsRect)
            {
                var all = new List<Rect2> { baseR };
                all.AddRange(WallPieces(leftWall, new Interval(0, g.H), cutLeft));
                all.AddRange(WallPieces(rightWall, new Interval(0, g.H), cutRight));
                all.AddRange(roofPieces);
                foreach (var r in all) m.Add(Hatch(r));
                foreach (var e in GeomUtil.UnionOutline(all)) m.Add(PPolyline.Line(e.Item1, e.Item2, Layers.Concrete));
            }
            else
            {
                // Đáy & nắp đổ tại chỗ: viền riêng. Thân đúc sẵn: từng đốt riêng → thấy mối nối.
                m.Add(Hatch(baseR)); m.Add(PPolyline.Rect(baseR, Layers.Concrete));
                foreach (var r in roofPieces) m.Add(Hatch(r));
                foreach (var e in GeomUtil.UnionOutline(roofPieces)) m.Add(PPolyline.Line(e.Item1, e.Item2, Layers.Concrete));

                double rh = g.P.RingHeight;
                int nRings = (int)Math.Ceiling(g.H / rh - 1e-6);
                for (int k = 0; k < nRings; k++)
                {
                    var zr = new Interval(k * rh, Math.Min(g.H, (k + 1) * rh));
                    foreach (var piece in WallPieces(leftWall, zr, cutLeft).Concat(WallPieces(rightWall, zr, cutRight)))
                    {
                        m.Add(Hatch(piece));
                        m.Add(PPolyline.Rect(piece, Layers.Concrete));
                    }
                }
                if (g.H % rh > 1 && g.H % rh < rh - 1)
                    callouts.Add(new Annot.Callout(new Vec2(outerS.Min, g.H - (g.H % rh) / 2), "MAKE-UP RING", false));
                callouts.Add(new Annot.Callout(new Vec2(outerS.Min, Math.Min(g.H * 0.75, g.H - 100)),
                    "PRECAST SHAFT UNITS TO\nMANUFACTURER'S DETAILS", false));
            }
        }

        private IEnumerable<Rect2> WallPieces(Interval sRange, Interval zRange, List<PipeFrame> cuts)
        {
            var gaps = cuts.Select(f => f.GapZ);
            foreach (var zi in zRange.Subtract(gaps))
                yield return new Rect2(sRange.Min, zi.Min, sRange.Max, zi.Max);
        }

        private PHatch Hatch(Rect2 r)
        {
            return new PHatch { Layer = Layers.Hatch, Pattern = "AR-CONC", PatternScale = st.ConcreteHatchScale, Loops = { PPolyline.Rect(r, Layers.Hatch) } };
        }

        // ---------------------------------------------------------------- benching
        private void BuildBenching()
        {
            var o = g.Outlet;
            double zOut = o.ZInvert;
            // invert máng phía trái: theo ống vào bị cắt (nếu có), nếu không thì cao hơn ống ra theo độ dốc 1%
            double zIn = cutLeft.Count > 0 ? cutLeft.Min(f => f.ZInvert) : zOut + innerS.Length * 0.01;
            double sL = innerS.Min, sR = innerS.Max;

            // phần benching bị cắt dưới đáy máng
            var poly = new PPolyline { Layer = Layers.Hatch, Closed = true };
            poly.Add(new Vec2(sL, 0)).Add(new Vec2(sR, 0)).Add(new Vec2(sR, zOut)).Add(new Vec2(sL, zIn));
            if (zIn > 1 || zOut > 1)
            {
                m.Add(new PHatch { Layer = Layers.Hatch, Pattern = "AR-SAND", PatternScale = st.ConcreteHatchScale, Loops = { poly } });
                m.Add(PPolyline.Line(new Vec2(sL, zIn), new Vec2(sR, zOut), Layers.Concrete));
            }
            // mặt benching phía sau (thấy được): ngang đỉnh ống ra
            double zBench = zOut + o.DN;
            m.Add(PPolyline.Line(new Vec2(sL, zBench), new Vec2(sR, zBench), Layers.ConcreteBeyond));
            callouts.Add(new Annot.Callout(new Vec2(sL + innerS.Length * 0.3, zBench), "BENCHING BEYOND, FALL 1:12\nTO CHANNEL, STEEL TROWEL", false));
            callouts.Add(new Annot.Callout(new Vec2(sL + innerS.Length * 0.35, (zIn + zOut) / 2 * 0.5),
                string.Format("MASS CONCRETE BENCHING\nCHANNEL INVERT {0:0.000} → {1:0.000}", g.FloorRL + zIn / 1000, g.FloorRL + zOut / 1000), false));
        }

        // ---------------------------------------------------------------- ống
        private void BuildPipes()
        {
            foreach (var f in g.Pipes)
            {
                string label = string.Format("{0} DN{1:0} {2}\nIL {3:0.000}", f.Spec.Mark, f.DN, f.IsOutlet ? "OUTLET" : "INLET", f.Spec.InvertLevel);
                if (IsCut(f))
                {
                    int side = f.Dir.Dot(u) > 0 ? 1 : -1;
                    double s0 = side > 0 ? innerS.Max : innerS.Min;
                    double s1 = (side > 0 ? outerS.Max : outerS.Min) + side * st.PipeStub;
                    foreach (var z in new[] { f.ZInvert, f.ZInvert + f.DN, f.ZOuterBottom, f.ZOuterTop })
                        m.Add(PPolyline.Line(new Vec2(s0, z), new Vec2(s1, z), Layers.Pipe));
                    Annot.BreakLine(m, new Vec2(s1, f.ZOuterBottom), new Vec2(s1, f.ZOuterTop), Math.Min(f.OD * 0.5, 4 * st.Scale), Layers.Pipe);
                    callouts.Add(new Annot.Callout(new Vec2(s1 - side * st.PipeStub * 0.4, f.ZOuterTop), label, false));
                    continue;
                }

                double beyond = (f.AxisPoint - origin).Dot(d) + f.Dir.Dot(d) * f.InnerDist;
                if (beyond <= 1) continue; // nằm phía trước mặt cắt → đã bị bỏ đi

                double sc = S(f.InnerPoint);
                double zc = f.ZInvert + f.DN / 2;
                double k = Math.Abs(f.Dir.Dot(d));
                if (g.IsRect && Math.Abs(f.Dir.Dot(u)) > 0.5)
                {
                    // ống song song mặt cắt nhưng nằm sau, xuyên qua tường bị cắt → nét khuất
                    double sIn = f.Dir.Dot(u) > 0 ? innerS.Max : innerS.Min;
                    double sOut = f.Dir.Dot(u) > 0 ? outerS.Max : outerS.Min;
                    m.Add(PPolyline.Rect(new Rect2(sIn, f.ZOuterBottom, sOut, f.ZOuterTop), Layers.PipeHidden));
                    callouts.Add(new Annot.Callout(new Vec2(sOut, f.ZOuterTop), label + " (BEYOND)", false));
                    continue;
                }
                if (k < 0.05) continue;
                m.Add(new PEllipse { Layer = Layers.Pipe, Center = new Vec2(sc, zc), RadiusX = f.DN / 2 * k, RadiusY = f.DN / 2 });
                m.Add(new PEllipse { Layer = Layers.Pipe, Center = new Vec2(sc, zc), RadiusX = f.OD / 2 * k, RadiusY = f.OD / 2 });
                callouts.Add(new Annot.Callout(new Vec2(sc, zc + f.OD / 2), label + " (BEYOND)", false));
            }
        }

        // ---------------------------------------------------------------- thép
        private void BuildRebar()
        {
            var L = rd.Layout;
            var sch = rd.Schedule;

            foreach (var layer in L.AllLayers) DrawMatLayer(layer);

            foreach (var t in L.RoofTrimmers)
            {
                var dir = t.AlongX ? new Vec2(1, 0) : new Vec2(0, 1);
                if (Math.Abs(dir.Dot(u)) > 0.99) continue; // song song mặt cắt: không nằm trên đường cắt
                double? sCross = Crossing(t.AlongX, t.Pos);
                if (sCross == null) continue;
                var p = origin + u * sCross.Value;
                double along = t.AlongX ? p.X : p.Y;
                if (!t.Run.Contains(along)) continue;
                m.Add(new PDot { Layer = Layers.Rebar, Center = new Vec2(sCross.Value, t.Z), Diameter = st.DotSize(t.Dia) });
            }

            // Ghi chú lưới (gộp 2 phương)
            AddMatCallout("BaseBot", false);
            if (g.P.BaseTopMat) AddMatCallout("BaseTop", false);
            AddMatCallout("RoofBot", true);
            if (g.P.RoofTopMat) AddMatCallout("RoofTop", true);
            if (L.RoofTrimmers.Count > 0 && openingS.HasValue)
            {
                var tz = L.RoofTrimmers[0].Z;
                callouts.Add(new Annot.Callout(new Vec2(openingS.Value.Max + g.P.Cover + 20, tz), sch.Callout("RoofTrim")));
            }

            if (g.IsRect) DrawWallRebar();
        }

        /// <summary>Tham số s tại chỗ thanh (song song X hoặc Y, ở toạ độ pos) cắt qua đường cắt.</summary>
        private double? Crossing(bool alongX, double pos)
        {
            if (alongX)
            {
                if (Math.Abs(u.Y) < 1e-9) return null;
                return (pos - origin.Y) / u.Y;
            }
            if (Math.Abs(u.X) < 1e-9) return null;
            return (pos - origin.X) / u.X;
        }

        private void DrawMatLayer(MatLayer layer)
        {
            var dir = layer.AlongX ? new Vec2(1, 0) : new Vec2(0, 1);
            if (Math.Abs(dir.Dot(u)) > 0.99)
            {
                // song song mặt cắt → vẽ thanh gần đường cắt nhất dạng đường
                double cutPerp = layer.AlongX ? origin.Y : origin.X;
                double pos = layer.Positions.OrderBy(p => Math.Abs(p - cutPerp)).First();
                foreach (var piece in layer.Pieces(pos))
                {
                    var a = layer.AlongX ? new Vec2(piece.Min, pos) : new Vec2(pos, piece.Min);
                    var b = layer.AlongX ? new Vec2(piece.Max, pos) : new Vec2(pos, piece.Max);
                    m.Add(PPolyline.Line(new Vec2(S(a), layer.Z), new Vec2(S(b), layer.Z), Layers.Rebar));
                }
                return;
            }
            // cắt ngang qua mặt cắt → chấm
            foreach (var pos in layer.Positions)
            {
                double? sc = Crossing(layer.AlongX, pos);
                if (sc == null) continue;
                var p = origin + u * sc.Value;
                double along = layer.AlongX ? p.X : p.Y;
                if (!layer.Pieces(pos).Any(i => i.Contains(along))) continue;
                m.Add(new PDot { Layer = Layers.Rebar, Center = new Vec2(sc.Value, layer.Z), Diameter = st.DotSize(layer.Dia) });
            }
        }

        private void AddMatCallout(string prefix, bool roof)
        {
            var sx = rd.Schedule.Set(prefix + "X");
            var sy = rd.Schedule.Set(prefix + "Y");
            if (sx == null || sy == null) return;
            var layer = rd.Layout.AllLayers.First(l => l.Key == prefix + "X");
            var marks = sx.Marks.Concat(sy.Marks).Distinct();
            string text = string.Format("{0} N{1}-{2:0} {3} EW", string.Join("/", marks), sx.Dia, sx.Spacing, layer.IsTop ? "TOP" : "BOT");
            var range = roof ? roofS : baseS;
            double sTarget = range.Min + Math.Min(250, range.Length * 0.15);
            callouts.Add(new Annot.Callout(new Vec2(sTarget, layer.Z), text));
        }

        private void DrawWallRebar()
        {
            var L = rd.Layout;
            double c = L.Cover;
            foreach (var side in new[] { -1, 1 })
            {
                double sInner = side < 0 ? innerS.Min : innerS.Max;
                double sOuter = side < 0 ? outerS.Min : outerS.Max;
                var cuts = side < 0 ? cutLeft : cutRight;
                var gaps = cuts.Select(f => new Interval(f.GapZ.Min - c, f.GapZ.Max + c)).ToList();

                // thép ngang (lớp ngoài) → chấm
                foreach (var face in new[] { sInner + side * L.HorizOffset, sOuter - side * L.HorizOffset })
                    foreach (var z in L.HorizLevels)
                        if (!gaps.Any(gp => gp.Contains(z)))
                            m.Add(new PDot { Layer = Layers.Rebar, Center = new Vec2(face, z), Diameter = st.DotSize(L.DH) });

                // thép đứng (lớp trong) → đường, chân L quay vào trong hố
                foreach (var face in new[] { sInner + side * L.VertOffset, sOuter - side * L.VertOffset })
                {
                    var zSpan = new Interval(L.VertZBot, L.VertZTop).Subtract(gaps);
                    for (int i = 0; i < zSpan.Count; i++)
                    {
                        var seg = zSpan[i];
                        var pl = new PPolyline { Layer = Layers.Rebar };
                        if (i == 0 && Math.Abs(seg.Min - L.VertZBot) < 1e-6)
                            pl.Add(new Vec2(face - side * L.VertLeg, L.VertZBot));
                        pl.Add(new Vec2(face, seg.Min)).Add(new Vec2(face, seg.Max));
                        m.Add(pl);
                    }
                }
            }
            double zv = g.H * 0.7;
            callouts.Add(new Annot.Callout(new Vec2(innerS.Min - L.VertOffset, zv), rd.Schedule.Callout("WallVert")));
            double zh = L.HorizLevels.OrderBy(z => Math.Abs(z - g.H * 0.45)).FirstOrDefault();
            callouts.Add(new Annot.Callout(new Vec2(outerS.Min + L.HorizOffset, zh), rd.Schedule.Callout("WallHoriz")));
            if (cutRight.Count + cutLeft.Count > 0 && rd.Schedule.Set("PipeTrim") != null)
            {
                var f = cutRight.Count > 0 ? cutRight[0] : cutLeft[0];
                double s = f.Dir.Dot(u) > 0 ? innerS.Max + g.Tw / 2 : innerS.Min - g.Tw / 2;
                callouts.Add(new Annot.Callout(new Vec2(s, f.GapZ.Min - c), rd.Schedule.Callout("PipeTrim").Replace(" EACH SIDE OF PENETRATION EF", "\nEACH SIDE OF PENETRATIONS EF")));
            }
        }

        // ---------------------------------------------------------------- nắp & khung
        private void BuildCoverFrame()
        {
            if (!openingS.HasValue) return;
            var o = openingS.Value;
            double h = zCL - zTopRoof;
            if (h < 10) return;
            var frame = new Rect2(o.Min - 100, zTopRoof, o.Max + 100, zCL);
            m.Add(PPolyline.Rect(frame, Layers.ConcreteBeyond));
            m.Add(PPolyline.Line(new Vec2(frame.X1, zCL - 40), new Vec2(frame.X2, zCL - 40), Layers.ConcreteBeyond));
            callouts.Add(new Annot.Callout(new Vec2(frame.X2, (zTopRoof + zCL) / 2),
                "ACCESS COVER & FRAME ON\nADJUSTMENT RINGS (BY OTHERS)", false));
        }

        // ---------------------------------------------------------------- cao độ
        private void BuildLevels()
        {
            double x = Math.Max(roofS.Max, baseS.Max) + 4 * st.Scale;
            double len = 26 * st.Scale;
            Annot.Level(m, st, x, zCL, "CL", g.P.CoverLevel, 1, len);
            Annot.Level(m, st, x, zTopRoof, "TOS", g.RoofTopRL, 1, len);
            Annot.Level(m, st, x, 0, "TOB", g.FloorRL, 1, len);
            Annot.Level(m, st, x, -g.Tb, "USB", g.BaseBottomRL, 1, len);
        }

        // ---------------------------------------------------------------- kích thước
        private double LeftExtent
        {
            get
            {
                double e = Math.Min(baseS.Min, roofS.Min);
                foreach (var f in cutLeft) e = Math.Min(e, outerS.Min - st.PipeStub);
                return e;
            }
        }

        private void BuildDims()
        {
            double x = LeftExtent - st.DimOffset;
            double xr = baseS.Min;
            Annot.DimV(m, -g.Tb, 0, xr, x);
            Annot.DimV(m, 0, g.H, outerS.Min, x);
            Annot.DimV(m, g.H, zTopRoof, roofS.Min, x);
            Annot.DimV(m, -g.Tb, zTopRoof, xr, x - st.DimStep);

            double y = -g.Tb - st.DimOffset;
            Annot.DimH(m, outerS.Min, innerS.Min, -g.Tb, y);
            Annot.DimH(m, innerS.Min, innerS.Max, -g.Tb, y);
            Annot.DimH(m, innerS.Max, outerS.Max, -g.Tb, y);
            if (baseS.Length > outerS.Length + 1)
                Annot.DimH(m, baseS.Min, baseS.Max, -g.Tb, y - st.DimStep);
        }

        private void PlaceCallouts()
        {
            double right = Math.Max(roofS.Max, baseS.Max);
            foreach (var f in cutRight) right = Math.Max(right, outerS.Max + st.PipeStub);
            double xL = LeftExtent - st.DimOffset - 2 * st.DimStep - 6 * st.Scale;
            double xR = right + 34 * st.Scale;
            double mid = innerS.Mid;
            Annot.CalloutColumn(m, st, callouts.Where(c => c.Target.X < mid), xL, -1);
            Annot.CalloutColumn(m, st, callouts.Where(c => c.Target.X >= mid), xR, +1);
        }
    }
}
