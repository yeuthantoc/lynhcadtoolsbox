using System;
using LynhCad.Manhole.Core.Geometry;
using LynhCad.Manhole.Core.Model;
using LynhCad.Manhole.Core.Rebar;

namespace LynhCad.Manhole.Core.Drawing
{
    /// <summary>
    /// ACCESS COVER &amp; FRAME DETAIL: mặt cắt qua mép lỗ thăm, tỉ lệ lớn (mặc định 1:5) nằm trong bản vẽ tỉ lệ chính.
    /// Hình học dựng theo mm thật rồi phóng K = scale chính / scale chi tiết lần; chữ, ký hiệu theo DrawStyle chính;
    /// kích thước ghi giá trị thật (TextOverride).
    /// Toạ độ cục bộ: x = 0 tại mép lỗ thăm, dương vào trong bản nắp; z = 0 tại mặt bản nắp (TOS), dương lên trên.
    /// Thứ tự từ dưới lên: bản nắp (hoặc đáy rãnh) → vành chỉnh cao độ → vữa đệm → khung → nắp phẳng mặt hoàn thiện (CL).
    /// </summary>
    public class CoverFrameBuilder : ViewBuilder
    {
        /// <summary>Chiều dày nắp và thép khung (mm) — chỉ để vẽ minh hoạ, nắp/khung do nhà sản xuất cung cấp.</summary>
        private const double CoverThk = 40, Steel = 10, Lip = 30, CoverShown = 250, CoverGap = 3;

        private readonly double k;
        private readonly string detailScale;

        public CoverFrameBuilder(ManholeGeometry geometry, RebarDesign rebar, DrawStyle style) : base(geometry, rebar, style)
        {
            double ds = geometry.P.CoverDetailScale;
            if (ds <= 0 || ds >= style.Scale) ds = style.Scale; // không phóng to được thì vẽ cùng tỉ lệ
            k = style.Scale / ds;
            detailScale = "SCALE 1:" + ds.ToString("0");
        }

        public string ScaleText { get { return detailScale; } }

        private Vec2 V(double x, double z) { return new Vec2(x * k, z * k); }

        private static string Mm(double v) { return v.ToString("0"); }

        public override DrawingModel Build()
        {
            var P = g.P;
            double ts = g.Ts;
            double rd = g.HasRebate ? g.RebateDepth : 0;
            double rw = g.HasRebate ? Math.Max(0, P.CoverRebateWidth) : 0;
            double A = P.CoverAllowance;            // CL → mặt bản nắp
            double hf = Math.Max(Steel * 3 + CoverThk, P.FrameHeight);
            double tb = Math.Max(0, P.BeddingThickness);
            double F = Math.Max(Steel * 3, P.FrameFlange);

            double zSeat = -rd;
            double hr = A + rd - tb - hf;           // chiều cao vành chỉnh cao độ còn lại
            if (P.FrameHeight < Steel * 3 + CoverThk)
                g.Warnings.Add(string.Format("Chi tiết khung nắp: khung cao {0:0} mm quá thấp, vẽ với {1:0} mm.", P.FrameHeight, hf));
            if (hr < -1)
                g.Warnings.Add(string.Format("Chi tiết khung nắp: khung + vữa ({0:0} mm) cao hơn khoảng CL → đáy chỗ đặt khung ({1:0} mm). Tăng coverAllowance hoặc giảm chiều cao khung.", hf + tb, A + rd));
            else if (hr > 300)
                g.Warnings.Add(string.Format("Chi tiết khung nắp: vành chỉnh cao độ {0:0} mm > 300 mm — xem lại cao độ nắp / chiều dày bản nắp.", hr));
            if (g.HasRebate && rw < F)
                g.Warnings.Add(string.Format("Chi tiết khung nắp: cánh khung rộng {0:0} mm hơn rãnh ({1:0} mm).", F, rw));
            hr = Math.Max(0, hr);

            double zRingTop = zSeat + hr, zFrameBase = zRingTop + tb, zTop = zFrameBase + hf;
            double ls = Math.Max(Math.Max(F, rw) + 250, 500);   // phần bản nắp vẽ ra

            // ---- bản nắp (bê tông cắt) ----
            var slab = new PPolyline { Layer = Layers.Concrete, Closed = true };
            slab.Add(V(0, -ts)).Add(V(ls, -ts)).Add(V(ls, 0));
            if (rd > 0) slab.Add(V(rw, 0)).Add(V(rw, -rd)).Add(V(0, -rd));
            else slab.Add(V(0, 0));
            m.Add(ConcreteHatch(slab.Clone()));
            m.Add(slab);
            Annot.BreakLine(m, V(ls, -ts), V(ls, 0), Math.Min(ts * k * 0.4, 4 * st.Scale), Layers.Concrete);

            // ---- vành chỉnh cao độ (đúc sẵn) ----
            if (hr > 1)
            {
                var ring = PPolyline.Rect(new Rect2(0, zSeat * k, F * k, zRingTop * k), Layers.ConcreteBeyond);
                m.Add(ConcreteHatch(ring.Clone()));
                m.Add(ring);
                // mối nối giữa các vành (mỗi vành ≤ 100)
                int n = (int)Math.Ceiling(hr / 100 - 1e-6);
                for (int i = 1; i < n; i++)
                    m.Add(PPolyline.Line(V(0, zSeat + hr * i / n), V(F, zSeat + hr * i / n), Layers.ConcreteBeyond));
            }

            // ---- vữa đệm ----
            if (tb > 0.5)
            {
                var bed = PPolyline.Rect(new Rect2(0, zRingTop * k, F * k, zFrameBase * k), Layers.ConcreteBeyond);
                m.Add(new PHatch { Layer = Layers.Hatch, Pattern = "AR-SAND", PatternScale = st.ConcreteHatchScale * 0.5, Loops = { bed.Clone() } });
                m.Add(bed);
            }

            // ---- khung: thép góc + gờ đỡ nắp ----
            var frame = new PPolyline { Layer = Layers.Metal, Closed = true };
            frame.Add(V(0, zFrameBase)).Add(V(F, zFrameBase)).Add(V(F, zFrameBase + Steel)).Add(V(Steel, zFrameBase + Steel))
                 .Add(V(Steel, zTop)).Add(V(0, zTop)).Add(V(0, zTop - CoverThk)).Add(V(-Lip, zTop - CoverThk))
                 .Add(V(-Lip, zTop - CoverThk - Steel)).Add(V(0, zTop - CoverThk - Steel));
            m.Add(new PHatch { Layer = Layers.Metal, Pattern = "SOLID", Loops = { frame.Clone() } });
            m.Add(frame);

            // ---- nắp (cắt ngắn) ----
            var cover = PPolyline.Rect(new Rect2(-CoverShown * k, (zTop - CoverThk) * k, -CoverGap * k, zTop * k), Layers.Metal);
            m.Add(new PHatch { Layer = Layers.Metal, Pattern = "ANSI31", PatternScale = st.Scale * 0.5, Loops = { cover.Clone() } });
            m.Add(cover);
            Annot.BreakLine(m, V(-CoverShown, zTop - CoverThk), V(-CoverShown, zTop), Math.Min(CoverThk * k * 0.6, 3 * st.Scale), Layers.Metal);

            // ---- mặt hoàn thiện ----
            m.Add(PPolyline.Line(V(Steel, A), V(ls, A), Layers.ConcreteBeyond));

            // ---- cao độ ----
            double xLv = ls * k + 4 * st.Scale;
            Annot.Level(m, st, xLv, A * k, "CL", P.CoverLevel, 1);
            Annot.Level(m, st, xLv, 0, "TOS", g.RoofTopRL, 1);

            // ---- kích thước (giá trị thật), bên trái phía lòng hố để leader ghi chú bên phải không cắt qua ----
            double xd = -CoverShown * k - st.DimOffset;
            if (hr > 1) Annot.DimV(m, zSeat * k, zRingTop * k, 0, xd, Mm(hr));
            Annot.DimV(m, zFrameBase * k, zTop * k, 0, xd, Mm(hf));      // lớp vữa: ghi trong ghi chú, không đo (quá mỏng)
            Annot.DimV(m, 0, A * k, 0, xd - st.DimStep, Mm(A));
            if (rd > 0) Annot.DimV(m, -rd * k, 0, 0, xd - 2 * st.DimStep, Mm(rd));
            // bề rộng cánh khung: phía trên mặt hoàn thiện (đường gióng không cắt qua vành / bản bê tông)
            Annot.DimH(m, 0, F * k, (zFrameBase + Steel) * k, Math.Max(zTop, A) * k + st.DimOffset, Mm(F));
            // bề rộng rãnh: dưới bản nắp, đo từ mép lỗ (đường gióng dọc mép lỗ và mép rãnh)
            if (rd > 0 && Math.Abs(rw - F) > 1) Annot.DimH(m, 0, rw * k, -rd * k, -ts * k - st.DimOffset, Mm(rw));

            // ---- ghi chú ----
            string opening = g.OpeningIsCircle
                ? string.Format("Ø{0:0}", 2 * g.OpeningHalfX)
                : string.Format("{0:0}x{1:0}", 2 * g.OpeningHalfX, 2 * g.OpeningHalfY);
            string cls = string.IsNullOrWhiteSpace(P.CoverClass) ? "" : "CLASS " + P.CoverClass.Trim().ToUpperInvariant() + " ";
            callouts.Add(new Annot.Callout(V(-CoverShown * 0.5, zTop), string.Format("{0}ACCESS COVER TO AS 3996,\nCLEAR OPENING {1}, FLUSH WITH\nFINISHED SURFACE", cls, opening)) { Horizontal = true, Above = true, Side = -1 });
            callouts.Add(new Annot.Callout(V(F * 0.6, zFrameBase + Steel), "FRAME TO MANUFACTURER'S\nDETAILS (BY OTHERS)") { Horizontal = true, Side = 1 });
            if (tb > 0.5)
                callouts.Add(new Annot.Callout(V(F * 0.5, (zRingTop + zFrameBase) / 2), string.Format("{0:0} NON-SHRINK MORTAR BEDDING", tb), LeaderEnd.FilledCircle) { Side = 1 });
            if (hr > 1)
                callouts.Add(new Annot.Callout(V(F * 0.5, (zSeat + zRingTop) / 2), "PRECAST CONCRETE ADJUSTMENT\nRINGS (300 MAX), BEDDED IN MORTAR", LeaderEnd.FilledCircle) { Side = 1 });
            callouts.Add(new Annot.Callout(V(ls * 0.6, -ts / 2), rd > 0
                ? string.Format("ROOF SLAB, {0:0} DEEP REBATE\nAROUND ACCESS OPENING", rd)
                : "ROOF SLAB", LeaderEnd.FilledCircle) { Side = 1 });

            PlaceCalloutColumns(0, xd - 3 * st.DimStep - 4 * st.Scale, xLv + (SymbolLibrary.LevelLineLength + 4) * st.Scale);
            return m;
        }
    }
}
