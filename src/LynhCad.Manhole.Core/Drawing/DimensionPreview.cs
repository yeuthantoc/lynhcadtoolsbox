using System;
using System.Collections.Generic;
using LynhCad.Manhole.Core.Geometry;
using LynhCad.Manhole.Core.Model;

namespace LynhCad.Manhole.Core.Drawing
{
    /// <summary>
    /// Sơ đồ kích thước để xem trong hộp thoại (tab General): mặt bằng + mặt cắt rút gọn, mỗi thông số hình học
    /// là một kích thước có Tip = tên ô nhập (rê chuột lên kích thước biết nó chỉnh cho ô nào).
    /// Dựng được cả khi thông số lỗi (chưa có ống, lòng hố quá nhỏ...); chỉ bỏ trống khi không tính được hình.
    /// </summary>
    public static class DimensionPreview
    {
        public class Result
        {
            public DrawingModel Model = new DrawingModel();
            public ManholeGeometry Geometry;
            public List<string> Errors = new List<string>();
            public double TextHeight;
        }

        // Tên ô nhập trong hộp thoại (tab General) — giữ đúng chữ để người dùng đối chiếu
        public const string TipLength = "Internal length (X)", TipWidth = "Internal width (Y)", TipWall = "Wall thickness",
            TipPrecastWall = "Precast wall", TipDiameter = "Internal diameter", TipRing = "Ring height (joints)",
            TipOpeningDia = "Access opening Ø", TipBase = "Base thickness", TipBaseProj = "Base projection",
            TipRoof = "Roof thickness", TipRoofProj = "Roof projection", TipOpenX = "Opening size X", TipOpenY = "Opening size Y",
            TipOffX = "Opening offset X", TipOffY = "Opening offset Y", TipCL = "Cover level (CL)", TipAllowance = "CL to top of roof",
            TipSump = "Sump below pipes", TipRebateDepth = "Cover frame rebate depth", TipRebateWidth = "Rebate beyond opening",
            TipHeight = "Internal height (derived: CL − CL to top of roof − roof − pipes − sump)";

        public static Result Build(ManholeParams p)
        {
            var res = new Result();
            var g = new ManholeGeometry(p);
            res.Geometry = g;
            res.Errors.AddRange(g.Errors);
            if (g.OuterHalfX <= 0 || g.OuterHalfY <= 0 || g.Tb <= 0 || g.Ts <= 0) return res;

            double ext = Math.Max(g.BaseHalfX, g.BaseHalfY);
            double off = ext * 0.14;
            res.TextHeight = ext * 0.07;
            var m = res.Model;

            var plan = new DrawingModel();
            BuildPlan(plan, g, off);
            var sec = new DrawingModel();
            BuildSection(sec, g, off);
            var bp = plan.GetBounds();
            var bs = sec.GetBounds();
            sec.Translate(new Vec2(0, bp.MinY - 2 * off - bs.MaxY));
            m.AddRange(plan);
            m.AddRange(sec);
            Label(m, new Vec2(bp.MinX, bp.MaxY + off * 0.4), "PLAN", res.TextHeight);
            var bs2 = sec.GetBounds();
            Label(m, new Vec2(bs2.MinX, bs2.MaxY + off * 0.4), "SECTION", res.TextHeight);
            return res;
        }

        private static void Label(DrawingModel m, Vec2 at, string text, double h)
        {
            m.Add(new PText { Layer = PipeLayoutPreview.HintLayer, Position = at, Text = text, Height = h, Align = TextAlign.Left, VAlign = TextVAlign.Bottom });
        }

        private static string Mm(double v) { return v.ToString("0"); }

        /// <summary>Kích thước ngang có chú thích "tên ô = giá trị mm" và Key = tên thuộc tính ManholeParams của ô đó ("" = suy ra, không có ô).</summary>
        private static void DimH(DrawingModel m, double x1, double x2, double yRef, double yLine, string tip, string key)
        {
            double v = Math.Abs(x2 - x1);
            if (v < 0.5) return;
            var d = Annot.DimH(m, x1, x2, yRef, yLine, Mm(v));
            d.Tip = tip + " = " + Mm(v) + " mm"; d.Key = key;
        }

        private static void DimV(DrawingModel m, double y1, double y2, double xRef, double xLine, string tip, string key)
        {
            double v = Math.Abs(y2 - y1);
            if (v < 0.5) return;
            var d = Annot.DimV(m, y1, y2, xRef, xLine, Mm(v));
            d.Tip = tip + " = " + Mm(v) + " mm"; d.Key = key;
        }

        private static void Hatched(DrawingModel m, PPolyline outer, PPolyline inner)
        {
            var h = new PHatch { Layer = Layers.Hatch, Pattern = "SOLID" };
            h.Loops.Add(Copy(outer, Layers.Hatch));
            if (inner != null) h.Loops.Add(Copy(inner, Layers.Hatch));
            m.Add(h);
            m.Add(outer);
            if (inner != null) m.Add(inner);
        }

        private static PPolyline Copy(PPolyline p, string layer)
        {
            var c = new PPolyline { Layer = layer, Closed = p.Closed };
            for (int i = 0; i < p.Points.Count; i++) c.Add(p.Points[i], p.Bulges[i]);
            return c;
        }

        private static PPolyline Circle(double r, string layer)
        {
            var c = new PPolyline { Layer = layer, Closed = true };
            c.Add(new Vec2(-r, 0), 1).Add(new Vec2(r, 0), 1);
            return c;
        }

        private static void BuildPlan(DrawingModel m, ManholeGeometry g, double off)
        {
            var P = g.P;
            double ox = g.OpeningCenter.X, oy = g.OpeningCenter.Y, hx = g.OpeningHalfX, hy = g.OpeningHalfY;
            double bx = g.BaseHalfX, by = g.BaseHalfY;

            if (g.IsRect)
                Hatched(m, PPolyline.Rect(Rect2.Centered(0, 0, g.Lo, g.Wo), Layers.Concrete), PPolyline.Rect(Rect2.Centered(0, 0, g.Li, g.Wi), Layers.Concrete));
            else
                Hatched(m, Circle(g.Ro, Layers.Concrete), Circle(g.Ri, Layers.Concrete));
            if (P.BaseProjection > 0)
            {
                var basePl = g.IsRect ? PPolyline.Rect(Rect2.Centered(0, 0, 2 * bx, 2 * by), Layers.ConcreteHidden) : Circle(bx, Layers.ConcreteHidden);
                basePl.Tip = TipBaseProj + " = " + Mm(P.BaseProjection) + " mm (base slab edge)";
                basePl.Key = "BaseProjection";
                m.Add(basePl);
            }
            if (hx > 0 && hy > 0)
            {
                var op = g.OpeningIsCircle
                    ? (Prim)new PCircle { Layer = Layers.ConcreteHidden, Center = g.OpeningCenter, Radius = hx }
                    : PPolyline.Rect(Rect2.Centered(ox, oy, 2 * hx, 2 * hy), Layers.ConcreteHidden);
                op.Tip = "Access opening";
                m.Add(op);
                if (g.HasRebate)
                {
                    var rb = g.OpeningIsCircle
                        ? (Prim)new PCircle { Layer = Layers.ConcreteHidden, Center = g.OpeningCenter, Radius = g.RebateHalfX }
                        : PPolyline.Rect(Rect2.Centered(ox, oy, 2 * g.RebateHalfX, 2 * g.RebateHalfY), Layers.ConcreteHidden);
                    rb.Tip = TipRebateWidth + " = " + Mm(P.CoverRebateWidth) + " mm (cover frame rebate)";
                    rb.Key = "CoverRebateWidth";
                    m.Add(rb);
                }
            }
            // tim hố
            m.Add(PPolyline.Line(new Vec2(-bx - off * 0.5, 0), new Vec2(bx + off * 0.5, 0), Layers.Centre));
            m.Add(PPolyline.Line(new Vec2(0, -by - off * 0.5), new Vec2(0, by + off * 0.5), Layers.Centre));

            // ---- dưới: lòng hố + tường + phần bản đáy thò ra, cùng một hàng ----
            double yBot = -by - off;
            double innerHalf = g.IsRect ? g.Li / 2 : g.Ri;
            double outerHalf = g.OuterHalfX;
            string wallTip = g.IsPrecast ? TipPrecastWall : TipWall;
            DimH(m, -innerHalf, innerHalf, g.IsRect ? -g.Wi / 2 : 0, yBot, g.IsRect ? TipLength : TipDiameter, g.IsRect ? "InternalLength" : "InternalDiameter");
            DimH(m, -outerHalf, -innerHalf, g.IsRect ? -g.Wo / 2 : 0, yBot, wallTip, g.IsPrecast ? "PrecastWallThickness" : "WallThickness");
            if (P.BaseProjection > 0) DimH(m, -bx, -outerHalf, -by, yBot, TipBaseProj, "BaseProjection");

            // ---- phải: chiều rộng lòng hố (hố chữ nhật) ----
            if (g.IsRect) DimV(m, -g.Wi / 2, g.Wi / 2, g.Li / 2, bx + off, TipWidth, "InternalWidth");

            // ---- trên / trái: lỗ thăm và độ lệch ----
            if (hx > 0 && hy > 0)
            {
                double yTop = by + off;
                if (g.OpeningIsCircle) DimH(m, ox - hx, ox + hx, oy, yTop, TipOpeningDia, "OpeningDiameter");
                else
                {
                    DimH(m, ox - hx, ox + hx, oy + hy, yTop, TipOpenX, "OpeningX");
                    DimV(m, oy - hy, oy + hy, ox - hx, -bx - off, TipOpenY, "OpeningY");
                }
                if (Math.Abs(ox) > 0.5) DimH(m, 0, ox, oy, yTop + off, TipOffX, "OpeningOffsetX");
                if (Math.Abs(oy) > 0.5) DimV(m, 0, oy, ox, -bx - 2 * off, TipOffY, "OpeningOffsetY");
            }
        }

        private static void BuildSection(DrawingModel m, ManholeGeometry g, double off)
        {
            var P = g.P;
            double H = g.H > 100 ? g.H : 1000; // chưa có ống / cao độ lỗi → chiều cao tượng trưng
            double Tb = g.Tb, Ts = g.Ts;
            double zTop = H + Ts;
            double zCL = zTop + Math.Max(0, P.CoverAllowance);
            double innerHalf = g.IsRect ? g.Li / 2 : g.Ri;
            double outerHalf = g.OuterHalfX;
            double bx = g.BaseHalfX, rx = g.RoofHalfX;
            double ox = g.OpeningCenter.X, hx = Math.Min(g.OpeningHalfX, innerHalf);

            Hatched(m, PPolyline.Rect(new Rect2(-bx, -Tb, bx, 0), Layers.Concrete), null);
            foreach (int side in new[] { -1, 1 })
            {
                var wall = new Rect2(Math.Min(side * innerHalf, side * outerHalf), 0, Math.Max(side * innerHalf, side * outerHalf), H);
                Hatched(m, PPolyline.Rect(wall, Layers.Concrete), null);
                if (g.IsPrecast && P.RingHeight > 0)
                    for (double z = P.RingHeight; z < H - 1; z += P.RingHeight)
                    {
                        var joint = PPolyline.Line(new Vec2(wall.X1, z), new Vec2(wall.X2, z), Layers.ConcreteBeyond);
                        joint.Tip = "Precast ring joint"; joint.Key = "RingHeight";
                        m.Add(joint);
                    }
            }
            // rãnh khung nắp: mặt bản nắp hạ xuống quanh lỗ thăm
            double rd = g.HasRebate ? Math.Min(g.RebateDepth, Ts - 1) : 0;
            double rw = g.HasRebate ? Math.Max(0, P.CoverRebateWidth) : 0;
            if (hx > 0)
            {
                foreach (int side in new[] { -1, 1 })
                {
                    double xEdge = side < 0 ? -rx : rx;
                    double xOpen = ox + side * hx;
                    double xReb = ox + side * (hx + rw);
                    var piece = new PPolyline { Layer = Layers.Concrete, Closed = true };
                    piece.Add(new Vec2(xEdge, H)).Add(new Vec2(xOpen, H)).Add(new Vec2(xOpen, zTop - rd));
                    if (rd > 0 && Math.Abs(xReb - xEdge) > 1 && (side < 0 ? xReb > xEdge : xReb < xEdge))
                        piece.Add(new Vec2(xReb, zTop - rd)).Add(new Vec2(xReb, zTop));
                    piece.Add(new Vec2(xEdge, zTop));
                    Hatched(m, piece, null);
                }
                if (zCL - (zTop - rd) > 1)
                {
                    double fx = rd > 0 ? hx + rw - 15 : hx + 100;
                    var frame = PPolyline.Rect(new Rect2(ox - fx, zTop - rd, ox + fx, zCL), Layers.ConcreteBeyond);
                    frame.Tip = "Cover, frame and adjustment rings (CL to top of roof)";
                    frame.Key = "CoverAllowance";
                    m.Add(frame);
                }
            }
            else Hatched(m, PPolyline.Rect(new Rect2(-rx, H, rx, zTop), Layers.Concrete), null);

            // mặt đất tại CL
            var ground = PPolyline.Line(new Vec2(-bx - off * 0.5, zCL), new Vec2(bx + off * 0.5, zCL), Layers.ConcreteBeyond);
            ground.Tip = TipCL + " = RL " + P.CoverLevel.ToString("0.000");
            ground.Key = "CoverLevel";
            m.Add(ground);
            m.Add(new PText
            {
                // đặt đầu trái (bên phải còn kích thước Roof projection phía trên đường mặt đất)
                Layer = PipeLayoutPreview.HintLayer, Position = new Vec2(-bx - off * 0.5, zCL + off * 0.1), Height = off * 0.4,
                Text = "CL " + P.CoverLevel.ToString("0.000"), Align = TextAlign.Left, VAlign = TextVAlign.Bottom, Tip = ground.Tip, Key = "CoverLevel",
            });
            // đáy ngoài ống thấp nhất = mặt bản đáy + sump
            if (P.SumpDepth > 0)
            {
                var pipeBot = PPolyline.Line(new Vec2(innerHalf * 0.3, P.SumpDepth), new Vec2(innerHalf, P.SumpDepth), Layers.PipeHidden);
                pipeBot.Tip = "Outside bottom of the lowest pipe";
                pipeBot.Key = "SumpDepth";
                m.Add(pipeBot);
            }

            // ---- trái: chuỗi đứng bản đáy / lòng hố / bản nắp / nắp ----
            double xL = -bx - off;
            DimV(m, -Tb, 0, -bx, xL, TipBase, "BaseThickness");
            DimV(m, 0, H, -outerHalf, xL, TipHeight, "");
            DimV(m, H, zTop, -rx, xL, TipRoof, "RoofThickness");
            if (zCL - zTop > 0.5) DimV(m, zTop, zCL, ox - hx - 100, xL, TipAllowance, "CoverAllowance");

            // ---- phải: sump, đốt đúc sẵn ----
            double xR = bx + off;
            if (P.SumpDepth > 0) DimV(m, 0, P.SumpDepth, innerHalf, xR, TipSump, "SumpDepth");
            if (g.IsPrecast && P.RingHeight > 0 && P.RingHeight < H) DimV(m, 0, P.RingHeight, outerHalf, xR + off, TipRing, "RingHeight");

            // ---- dưới / trên: phần bản thò ra ----
            if (P.BaseProjection > 0) DimH(m, -bx, -outerHalf, -Tb, -Tb - off, TipBaseProj, "BaseProjection");
            if (P.RoofProjection > 0) DimH(m, outerHalf, rx, zTop, zCL + off, TipRoofProj, "RoofProjection");

            // ---- rãnh khung nắp: chiều sâu (bên phải, ngoài khung nắp) và bề rộng thêm mỗi phía (trên mặt đất) ----
            if (rd > 0 && hx > 0)
            {
                DimV(m, zTop - rd, zTop, ox + hx + rw, xR + (P.SumpDepth > 0 ? off : 0), TipRebateDepth, "CoverRebateDepth");
                if (rw > 0) DimH(m, ox + hx, ox + hx + rw, zTop, zCL + 2 * off, TipRebateWidth, "CoverRebateWidth");
            }
        }
    }
}
