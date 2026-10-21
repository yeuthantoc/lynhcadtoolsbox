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
    public class RoofPlanBuilder : ViewBuilder
    {
        public RoofPlanBuilder(ManholeGeometry geometry, RebarDesign rebar, DrawStyle style) : base(geometry, rebar, style) { }

        public override DrawingModel Build()
        {
            double hx = g.RoofHalfX, hy = g.RoofHalfY;
            var o = g.OpeningCenter;

            m.Add(PPolyline.Rect(Rect2.Centered(0, 0, 2 * hx, 2 * hy), Layers.Concrete));
            if (g.OpeningIsCircle)
                m.Add(new PCircle { Layer = Layers.Concrete, Center = o, Radius = g.OpeningHalfX });
            else
                m.Add(PPolyline.Rect(Rect2.Centered(o.X, o.Y, 2 * g.OpeningHalfX, 2 * g.OpeningHalfY), Layers.Concrete));

            // rãnh khung nắp (mép rãnh nhìn thấy từ trên)
            if (g.HasRebate)
            {
                if (g.OpeningIsCircle)
                    m.Add(new PCircle { Layer = Layers.Concrete, Center = o, Radius = g.RebateHalfX });
                else
                    m.Add(PPolyline.Rect(Rect2.Centered(o.X, o.Y, 2 * g.RebateHalfX, 2 * g.RebateHalfY), Layers.Concrete));
                string size = g.OpeningIsCircle
                    ? string.Format("Ø{0:0}", 2 * g.RebateHalfX)
                    : string.Format("{0:0}x{1:0}", 2 * g.RebateHalfX, 2 * g.RebateHalfY);
                var corner = o + new Vec2(-g.RebateHalfX, g.RebateHalfY) * (g.OpeningIsCircle ? 0.7071 : 1);
                callouts.Add(new Annot.Callout(corner,
                    string.Format("{0} x {1:0} DEEP REBATE\nFOR COVER FRAME", size, g.RebateDepth)));
            }

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
                        var a = BarPoint(layer.AlongX, piece.Min, pos);
                        var b = BarPoint(layer.AlongX, piece.Max, pos);
                        m.Add(PPolyline.Line(a, b, Layers.RebarHidden));
                        // cog (móc đứng) thể hiện bằng vạch ngắn vuông góc ở đầu thanh
                        if (layer.CogStd > 0)
                        {
                            var tick = BarPoint(layer.AlongX, 0, 1) * (2.5 * st.Scale);
                            m.Add(PPolyline.Line(a, a + tick, Layers.RebarHidden));
                            m.Add(PPolyline.Line(b, b + tick, Layers.RebarHidden));
                        }
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
                var a = BarPoint(t.AlongX, t.Run.Min, t.Pos);
                var b = BarPoint(t.AlongX, t.Run.Max, t.Pos);
                m.Add(PPolyline.Line(a, b, Layers.Rebar));
                if (trimSample == null && t.AlongX && t.Pos > o.Y) trimSample = new Vec2(t.Run.Max - 50, t.Pos);
            }
            if (trimSample != null) callouts.Add(new Annot.Callout(trimSample.Value, rd.Schedule.Callout(BarSets.RoofTrim)));

            if (g.P.RoofTopMat)
            {
                string top = rd.Schedule.MatCallout(BarSets.RoofTop, true);
                if (top != null)
                    callouts.Add(new Annot.Callout(new Vec2(hx * 0.6, -hy * 0.6),
                        top + (g.HasRebate ? string.Format("\nCRANKED DOWN {0:0} UNDER REBATE", g.RebateDepth) : "") + "\n(NOT SHOWN FOR CLARITY)"));
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
            PlaceCalloutColumns(0, -ext - st.DimStep, ext);
            return m;
        }
    }
}
