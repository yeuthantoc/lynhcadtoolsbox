using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LynhCad.Manhole.Core.Drawing;
using LynhCad.Manhole.Core.Export;
using LynhCad.Manhole.Core.Geometry;
using LynhCad.Manhole.Core.Model;
using LynhCad.Manhole.Core.Rebar;

namespace LynhCad.Manhole.Preview
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            string cmd = args.Length > 0 ? args[0].ToLowerInvariant() : "check";
            try
            {
                switch (cmd)
                {
                    case "check": return Checks.Run();
                    case "svg":
                        if (args.Length < 3) { Console.WriteLine("usage: svg <params.json> <out.svg>"); return 2; }
                        return WriteSvg(ParamsJson.Load(args[1]), args[2]);
                    case "sample":
                        string dir = args.Length > 1 ? args[1] : ".";
                        Directory.CreateDirectory(dir);
                        foreach (var p in new[] { ManholeParams.SampleRectangular(), ManholeParams.SampleCircular(), ManholeParams.SampleCircularCast() })
                        {
                            string name = p.Type == ManholeType.RectangularCast ? "rect-mh01"
                                        : p.Type == ManholeType.CircularPrecast ? "circ-mh02" : "circ-cast-mh04";
                            ParamsJson.Save(p, Path.Combine(dir, name + ".json"));
                            WriteSvg(p, Path.Combine(dir, name + ".svg"));
                        }
                        return 0;
                    case "pipes":
                        if (args.Length < 3) { Console.WriteLine("usage: pipes <params.json> <out.svg>"); return 2; }
                        var pv = PipeLayoutPreview.Build(ParamsJson.Load(args[1]));
                        foreach (var e in pv.Geometry.Errors) Console.WriteLine("ERROR: " + e);
                        File.WriteAllText(args[2], SvgExporter.Export(pv.Model, 20));
                        return 0;
                    case "dims":
                        if (args.Length < 3) { Console.WriteLine("usage: dims <params.json> <out.svg>"); return 2; }
                        var dv = DimensionPreview.Build(ParamsJson.Load(args[1]));
                        foreach (var e in dv.Errors) Console.WriteLine("ERROR: " + e);
                        File.WriteAllText(args[2], SvgExporter.Export(dv.Model, 20));
                        return 0;
                    case "snapshot":
                        string samples = Snapshots.FindSamplesDir();
                        if (samples == null) { Console.WriteLine("samples/ not found"); return 2; }
                        return Snapshots.Write(samples);
                    default:
                        Console.WriteLine("commands: check | svg <json> <svg> | pipes <json> <svg> | dims <json> <svg> | sample <folder> | snapshot");
                        return 2;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return 1;
            }
        }

        private static int WriteSvg(ManholeParams p, string outPath)
        {
            var res = DetailComposer.Compose(p);
            foreach (var w in res.Warnings) Console.WriteLine("WARNING: " + w);
            if (!res.Ok)
            {
                foreach (var e in res.Errors) Console.WriteLine("ERROR: " + e);
                return 1;
            }
            File.WriteAllText(outPath, SvgExporter.Export(res.Model, p.Scale));
            Console.WriteLine("{0}: {1} primitives, {2} bar marks, {3:0.0} kg -> {4}",
                p.Id, res.Model.Items.Count, res.Rebar.Schedule.Marks.Count, res.Rebar.Schedule.TotalMass, outPath);
            return 0;
        }
    }

    /// <summary>Bộ kiểm tra nhỏ, không cần framework test (môi trường build không có NuGet).</summary>
    internal static class Checks
    {
        private static int failed, passed;

        private static void Eq(string name, double expected, double actual, double tol = 1e-6)
        {
            if (Math.Abs(expected - actual) <= tol) { passed++; return; }
            failed++;
            Console.WriteLine("FAIL {0}: expected {1}, got {2}", name, expected, actual);
        }

        private static void True(string name, bool cond)
        {
            if (cond) { passed++; return; }
            failed++;
            Console.WriteLine("FAIL " + name);
        }

        public static int Run()
        {
            Geometry();
            RectangularSample();
            CircularSample();
            CircularCastSample();
            PipePreview();
            Steps();
            Validation();
            Json();
            Naming();
            Overlap();
            CalloutLayout();
            Blocks();
            Catalog();
            Snapshot();
            Console.WriteLine("{0} passed, {1} failed", passed, failed);
            return failed == 0 ? 0 : 1;
        }

        private static void Geometry()
        {
            var d = GeomUtil.Distribute(0, 1000, 200);
            Eq("distribute count", 6, d.Count);
            Eq("distribute last", 1000, d.Last());
            Eq("distribute uneven count", 7, GeomUtil.Distribute(0, 1001, 200).Count);
            Eq("count bars", 10, GeomUtil.CountBars(1636, 200));

            var rest = new Interval(0, 1000).Subtract(new[] { new Interval(200, 300), new Interval(900, 1200) });
            Eq("subtract pieces", 2, rest.Count);
            Eq("subtract piece 2", 600, rest[1].Length);

            // chữ L: 6 cạnh
            var edges = GeomUtil.UnionOutline(new[] { new Rect2(0, 0, 2, 1), new Rect2(0, 1, 1, 2) });
            Eq("union L edges", 6, edges.Count);
            // hai hình chữ nhật chạm nhau → 4 cạnh (mạch nối biến mất)
            Eq("union merge edges", 4, GeomUtil.UnionOutline(new[] { new Rect2(0, 0, 1, 1), new Rect2(1, 0, 2, 1) }).Count);

            var clip = GeomUtil.LineRectClip(new Vec2(0, 100), new Vec2(1, 0), 500, 400);
            True("clip hit", clip != null);
            Eq("clip length", 1000, clip.Value.Length);
            var diag = GeomUtil.LineRectClip(Vec2.Zero, Vec2.FromAngle(Math.PI / 4), 500, 500);
            Eq("clip diagonal", 2 * 500 * Math.Sqrt(2), diag.Value.Length, 1e-6);

            Eq("mass N12", 8.88, new BarMark { Dia = 12, Length = 1000, Count = 10 }.Mass, 1e-9);
            Eq("bulge 90", Math.Tan(Math.PI / 8), GeomUtil.Bulge(Math.PI / 2));

            // bảng thống kê làm tròn lên 10 mm nhưng không sửa BarItem đầu vào
            var item = new BarItem { SetKey = "S", Group = 'B', Dia = 12, Shape = BarShape.Straight, A = 1234, Count = 2 };
            var built = RebarSchedule.Build(new[] { item }, new BarSetInfo[0]);
            Eq("schedule rounds up", 1240, built.Marks[0].Length);
            Eq("schedule keeps input", 1234, item.A);
        }

        private static void RectangularSample()
        {
            var p = ManholeParams.SampleRectangular();
            var g = new ManholeGeometry(p);
            True("rect valid: " + string.Join("; ", g.Errors), g.IsValid);

            // mặt đáy = đáy ngoài thấp nhất (P3: 12.400 - 0.0375) - 0.050
            Eq("floor RL", 12.400 - 0.0375 - 0.050, g.FloorRL, 1e-9);
            Eq("roof top RL", 14.800, g.RoofTopRL, 1e-9);
            Eq("H", (14.600 - g.FloorRL) * 1000, g.H, 1e-6);
            True("outlet P3", g.Outlet.Spec.Mark == "P3");
            Eq("section dir x", 1, g.SectionDir.X);
            Eq("view dir y", 1, g.ViewDir.Y);

            var rd = RebarDesigner.Design(g);
            var sch = rd.Schedule;
            var w1 = sch.Marks.First(m => m.Mark == "W1");
            // vòng trong 1616×1316 → 10 + 8 vị trí mỗi phương = 32 thanh; vòng ngoài cùng vị trí + 4 góc = 40
            Eq("W1 count (72 EF)", 72, w1.Count);
            True("W1 is L", w1.Shape == BarShape.L);
            Eq("W1 leg", 300, w1.B);
            // hai mặt thẳng hàng: mỗi thanh mặt ngoài (trừ 4 góc) có thanh mặt trong cùng X (tường N/S) hoặc cùng Y (tường E/W)
            var loops = RebarDesigner.VerticalLoops(g, rd.Layout);
            var pts = RebarDesigner.VerticalPositions(g, rd.Layout);
            Eq("vertical positions = W1 count", w1.Count, pts.Count);
            var inner = pts.Where(q => Math.Abs(q.X) <= loops[0].X + 1e-6 && Math.Abs(q.Y) <= loops[0].Y + 1e-6).ToList();
            Eq("inner face bars", 32, inner.Count);
            var outerPts = pts.Where(q => !inner.Contains(q)).ToList();
            Eq("outer face bars", 40, outerPts.Count);
            True("outer face bars line up with inner face", outerPts.All(q =>
                (Math.Abs(Math.Abs(q.X) - loops[1].X) < 1e-6 && Math.Abs(Math.Abs(q.Y) - loops[1].Y) < 1e-6) ||
                inner.Any(r => Math.Abs(r.X - q.X) < 1e-6 || Math.Abs(r.Y - q.Y) < 1e-6)));
            True("rect EF suffix", sch.Callout(BarSets.WallVert).EndsWith(" EF VERT"));
            True("pipe trimmers EF", sch.Set(BarSets.PipeTrim).Suffix.EndsWith(" EF"));

            // thép tường 1 lớp giữa tường
            var p1 = p.Clone();
            p1.WallSingleLayer = true;
            var g1 = new ManholeGeometry(p1);
            var rd1 = RebarDesigner.Design(g1);
            Eq("single layer: 1 vertical loop", 1, RebarDesigner.VerticalLoops(g1, rd1.Layout).Count);
            Eq("single layer: vertical at wall centre", g1.Tw / 2, rd1.Layout.VertFromInner[0]);
            Eq("single layer: W1 count (one loop 1700x1400)", 32, rd1.Schedule.Marks.First(m => m.Mark == "W1").Count);
            True("single layer: CENTRAL suffix", rd1.Schedule.Callout(BarSets.WallVert).EndsWith(" CENTRAL VERT")
                && rd1.Schedule.Callout(BarSets.WallHoriz).EndsWith(" CENTRAL HORIZ"));
            True("single layer: pipe trimmers not EF", !rd1.Schedule.Set(BarSets.PipeTrim).Suffix.EndsWith(" EF"));
            Eq("single layer: half the U-bars", sch.Marks.Where(m => m.Shape == BarShape.U).Sum(m => m.Count) / 2,
                rd1.Schedule.Marks.Where(m => m.Shape == BarShape.U).Sum(m => m.Count));
            True("single layer: compose ok", DetailComposer.Compose(p1).Ok);
            var secOne = new SectionBuilder(g1, rd1, new DrawStyle(20)).Build();
            // mỗi tường một đường thép đứng (đoạn đứng ≥ 300 trên S-REIN, gộp theo X) thay vì hai
            var vertX = secOne.Items.OfType<PPolyline>().Where(l => l.Layer == Layers.Rebar)
                .SelectMany(l => l.Points.Zip(l.Points.Skip(1), (pa, pb) => new { pa, pb }))
                .Where(sg => Math.Abs(sg.pa.X - sg.pb.X) < 1e-6 && Math.Abs(sg.pa.Y - sg.pb.Y) >= 300)
                .Select(sg => Math.Round(sg.pa.X, 3)).Distinct().ToList();
            Eq("single layer: one vertical bar line per wall", 2, vertX.Count);

            // thép ngang phía trong thép đứng
            var pIn = p.Clone(); pIn.WallHorizontalInside = true;
            var gIn = new ManholeGeometry(pIn);
            var LIn = RebarDesigner.Design(gIn).Layout;
            Eq("horiz inside: vertical is outer layer", 50 + 6, LIn.VertFromInner[0]);
            Eq("horiz inside: horizontal inside vertical", 50 + 12 + 6, LIn.HorizFromInner[0]);
            True("horiz inside: symmetric outer face", Math.Abs(gIn.Tw - LIn.HorizFromInner[1] - LIn.HorizFromInner[0]) < 1e-9);
            True("horiz outside (default): horizontal is outer layer", rd.Layout.HorizFromInner[0] < rd.Layout.VertFromInner[0]);
            pIn.WallSingleLayer = true;
            var LIn1 = RebarDesigner.Design(new ManholeGeometry(pIn)).Layout;
            True("single layer + horiz inside: horizontal towards inner face", LIn1.HorizFromInner[0] < LIn1.VertFromInner[0]);
            True("single layer default: horizontal towards outer face", rd1.Layout.HorizFromInner[0] > rd1.Layout.VertFromInner[0]);
            True("horiz inside compose ok", DetailComposer.Compose(pIn).Ok);

            // preview kích thước (tab General): mỗi thông số hình học là một kích thước có Tip = tên ô nhập
            var dp = DimensionPreview.Build(p);
            True("dim preview builds", dp.Errors.Count == 0 && dp.Model.Items.Count > 0);
            var dims = dp.Model.Items.OfType<PDim>().ToList();
            True("dim preview: every dim has a tip", dims.Count > 0 && dims.All(d => !string.IsNullOrEmpty(d.Tip) && !string.IsNullOrEmpty(d.TextOverride)));
            foreach (var tip in new[] { DimensionPreview.TipLength, DimensionPreview.TipWidth, DimensionPreview.TipWall, DimensionPreview.TipBase,
                                        DimensionPreview.TipRoof, DimensionPreview.TipBaseProj, DimensionPreview.TipOpenX, DimensionPreview.TipOpenY,
                                        DimensionPreview.TipAllowance, DimensionPreview.TipSump, DimensionPreview.TipHeight })
                True("dim preview tip: " + tip, dims.Any(d => d.Tip.StartsWith(tip + " =")));
            True("dim preview wall tip value", dims.Any(d => d.Tip == DimensionPreview.TipWall + " = 200 mm"));
            True("dim preview no offset dims when centred", !dims.Any(d => d.Tip.StartsWith(DimensionPreview.TipOffX)));
            var pOff = p.Clone(); pOff.OpeningOffsetX = 150;
            True("dim preview offset dim appears", DimensionPreview.Build(pOff).Model.Items.OfType<PDim>().Any(d => d.Tip.StartsWith(DimensionPreview.TipOffX + " = 150")));
            True("dim preview CL text", dp.Model.Items.OfType<PText>().Any(t => t.Text.StartsWith("CL ") && t.Tip.StartsWith(DimensionPreview.TipCL)));
            var noPipes = p.Clone(); noPipes.Pipes.Clear();
            var dpNo = DimensionPreview.Build(noPipes);
            True("dim preview without pipes still draws", dpNo.Errors.Count > 0 && dpNo.Model.Items.OfType<PDim>().Any(d => d.Tip.StartsWith(DimensionPreview.TipBase)));

            // preview bố trí thép (tab Reinforcement)
            var rp = RebarLayoutPreview.Build(p);
            True("rebar preview builds", rp.Errors.Count == 0 && rp.Model.Items.Count > 0);
            True("rebar preview has rebar", rp.Model.Items.Any(i => i.Layer == Layers.Rebar));
            True("rebar preview has no dims/leaders", !rp.Model.Items.Any(i => i is PDim || i is PLeader || i is PBlock));
            var legend = RebarLayoutPreview.Legend(g, rd);
            True("rebar preview legend", legend.Count >= 4 && legend[0].StartsWith("W1 ") && legend.Any(s => s.EndsWith(" BOT EW")));
            var bad = p.Clone(); bad.WallThickness = 50;
            var rpBad = RebarLayoutPreview.Build(bad);
            True("rebar preview invalid params -> errors, no model", rpBad.Errors.Count > 0 && rpBad.Model.Items.Count == 0);
            True("has W2/W3 U-bars", sch.Marks.Count(m => m.Shape == BarShape.U) == 2);
            True("WallHoriz callout", sch.Callout(BarSets.WallHoriz).StartsWith("W2/W3 N12-200 EF HORIZ"));
            True("base mat callout", sch.MatCallout(BarSets.BaseBot, false).EndsWith(" N16-200 BOT EW"));
            True("missing roof top mat -> null", sch.MatCallout(BarSets.RoofTop, true) == null);
            bool threw = false;
            try { sch.Callout("NoSuchSet"); } catch (KeyNotFoundException) { threw = true; }
            True("unknown bar set throws", threw);

            // bản đáy: 1500+400+300 = 2200 x 1900 → thanh theo X dài 2100
            var bx = sch.Marks.First(m => m.Mark.StartsWith("B") && Math.Abs(m.Length - 2100) < 1e-6);
            Eq("base X count", GeomUtil.CountBars(1900 - 100 - 16, 200) * 2, bx.Count);

            // bản nắp: thanh bị lỗ cắt phải ngắn hơn thanh nguyên
            var roofX = rd.Layout.RoofLayers.First(l => l.Key == BarSets.X(BarSets.RoofBot));
            var cut = roofX.Positions.Where(y => roofX.Pieces(y).Count == 2).ToList();
            True("roof has cut bars", cut.Count > 0);
            foreach (var y in cut)
                foreach (var piece in roofX.Pieces(y))
                    True("cut piece clear of opening", piece.Max <= -450 - 50 + 1e-6 || piece.Min >= 450 + 50 - 1e-6);
            True("trimmers exist", rd.Layout.RoofTrimmers.Count == 8);

            True("total mass sensible", sch.TotalMass > 100 && sch.TotalMass < 2000);
            int sumCounts = sch.Marks.Sum(m => m.Count);
            True("schedule counts positive", sumCounts > 0 && sch.Marks.All(m => m.Count > 0 && m.Length > 0));

            var res = DetailComposer.Compose(p);
            True("rect compose ok", res.Ok);
            var b = res.Model.GetBounds();
            True("rect bounds sane", b.Width > 3000 && b.Width < 60000 && b.Height > 3000 && b.Height < 60000);
        }

        private static void CircularSample()
        {
            var p = ManholeParams.SampleCircular();
            var g = new ManholeGeometry(p);
            True("circ valid: " + string.Join("; ", g.Errors), g.IsValid);
            Eq("Ro", 700, g.Ro);
            Eq("base half", 850, g.BaseHalfX);
            var rd = RebarDesigner.Design(g);
            True("circ has no wall bars", rd.Schedule.Marks.All(m => !m.Mark.StartsWith("W")));
            True("circ has base & roof bars", rd.Schedule.Marks.Any(m => m.Mark.StartsWith("B")) && rd.Schedule.Marks.Any(m => m.Mark.StartsWith("R")));
            var gaps = g.AngularGaps(g.Ri);
            Eq("angular gaps", 3, gaps.Count);
            True("circ compose ok", DetailComposer.Compose(p).Ok);

            // ống ra chéo 45° → mặt cắt chéo, vẫn dựng được
            p.Pipes[2].AngleDeg = 45;
            p.Pipes[1].AngleDeg = 135;
            var r2 = DetailComposer.Compose(p);
            True("diagonal section ok", r2.Ok);
        }

        private static void CircularCastSample()
        {
            var p = ManholeParams.SampleCircularCast();
            var g = new ManholeGeometry(p);
            True("circ cast valid: " + string.Join("; ", g.Errors), g.IsValid);
            True("circ cast flags", g.IsCircular && !g.IsPrecast && g.HasCastWalls && !g.IsRect);
            Eq("circ cast Ro uses wallThickness", 950, g.Ro);

            var rd = RebarDesigner.Design(g);
            var sch = rd.Schedule;
            var vert = sch.Marks.First(m => m.Mark == "W1");
            True("circ cast W1 is L", vert.Shape == BarShape.L);
            // vòng ngoài r = 950-68 = 882 → ceil(2πr/200) = 28; vòng trong cùng số thanh, cùng góc (thẳng hàng)
            Eq("circ cast vertical count", 28 + 28, vert.Count);
            var ringPts = RebarDesigner.VerticalPositions(g, rd.Layout);
            True("circ cast faces share angles", ringPts.Take(28).Zip(ringPts.Skip(28), (a, b) => Math.Abs(a.Angle() - b.Angle()) < 1e-9).All(x => x));
            True("circ precast rebar preview", RebarLayoutPreview.Build(ManholeParams.SampleCircular()).Errors.Count == 0);
            var dpPre = DimensionPreview.Build(ManholeParams.SampleCircular()).Model.Items.OfType<PDim>().Select(d => d.Tip).ToList();
            foreach (var tip in new[] { DimensionPreview.TipDiameter, DimensionPreview.TipPrecastWall, DimensionPreview.TipRing, DimensionPreview.TipOpeningDia })
                True("circ precast dim preview tip: " + tip, dpPre.Any(t => t.StartsWith(tip + " =")));
            True("circ precast dim preview: no rect tips", !dpPre.Any(t => t.StartsWith(DimensionPreview.TipLength) || t.StartsWith(DimensionPreview.TipWall + " =")));
            var dpCast = DimensionPreview.Build(p).Model.Items.OfType<PDim>().Select(d => d.Tip).ToList();
            True("circ cast dim preview wall tip", dpCast.Any(t => t.StartsWith(DimensionPreview.TipWall + " =")) && !dpCast.Any(t => t.StartsWith(DimensionPreview.TipRing)));
            True("circ cast rebar preview", RebarLayoutPreview.Build(p).Errors.Count == 0);
            var rings = sch.Marks.Where(m => m.Shape == BarShape.Ring).ToList();
            Eq("circ cast 2 ring marks (inner/outer face)", 2, rings.Count);
            // vòng trong: A = 2·(750+56) = 1612, nối chồng 50·12 = 600 → dài π·1620 (A làm tròn lên 10) + 600
            Eq("circ cast inner ring dia", 1620, rings[0].A);
            Eq("circ cast inner ring lap", 600, rings[0].B);
            Eq("circ cast inner ring length", Math.PI * 1620 + 600, rings[0].Length, 1e-6);
            Eq("circ cast rings per face = levels", rd.Layout.HorizLevels.Count, rings[0].Count);
            True("circ cast WallHoriz callout", sch.Callout(BarSets.WallHoriz).StartsWith("W2/W3 N12-200 EF HORIZ"));
            True("circ cast pipe trimmers", sch.Set(BarSets.PipeTrim) != null);

            var res = DetailComposer.Compose(p);
            True("circ cast compose ok", res.Ok);
            True("circ cast plan has ring arcs", res.Model.Items.OfType<PArc>().Any(a => a.Layer == Layers.Rebar));
            True("circ cast schedule shows RING", res.Model.Items.OfType<PTable>().Any(t => t.Rows.Any(r => r[2] == "RING")));
            True("circ cast notes have no precast note", res.Model.Items.OfType<PText>().All(t => !t.Text.Contains("PRECAST MANHOLE COMPONENTS")));

            var thin = ManholeParams.SampleCircularCast();
            thin.WallThickness = 120;
            True("circ cast thin wall error", !new ManholeGeometry(thin).IsValid);
        }

        private static void PipePreview()
        {
            foreach (var p in new[] { ManholeParams.SampleRectangular(), ManholeParams.SampleCircular(), ManholeParams.SampleCircularCast() })
            {
                var pv = PipeLayoutPreview.Build(p);
                var texts = pv.Model.Items.OfType<PText>().Select(t => t.Text).ToList();
                True("pipe preview " + p.Id + " labels every pipe", p.Pipes.All(s => texts.Any(t => t.StartsWith(s.Mark + " "))));
                True("pipe preview " + p.Id + " outlet highlighted", pv.Model.Items.Any(i => i.Layer == PipeLayoutPreview.OutletLayer));
                True("pipe preview " + p.Id + " no error layer when valid", pv.Model.Items.All(i => i.Layer != PipeLayoutPreview.ErrorLayer));
                True("pipe preview " + p.Id + " has section line", texts.Count(t => t == "A") == 2);
            }

            // lỗ xuyên chồng nhau: vẫn có preview, hai ống bị tô đỏ
            var clash = ManholeParams.SampleRectangular();
            clash.Pipes[1].Wall = WallSide.West; clash.Pipes[1].Offset = 100; clash.Pipes[1].InvertLevel = 12.450;
            var cv = PipeLayoutPreview.Build(clash);
            True("pipe preview clash still drawn", !cv.Geometry.IsValid && cv.Model.Items.Count > 0);
            True("pipe preview clash marks P1+P2", cv.Geometry.PipesWithErrors.Count == 2
                && cv.Model.Items.OfType<PText>().Count(t => t.Layer == PipeLayoutPreview.ErrorLayer) == 2);

            // thông số vô lý: không được ném lỗi
            var junk = ManholeParams.SampleRectangular();
            junk.InternalLength = 0; junk.WallThickness = 0;
            bool threw = false;
            try { PipeLayoutPreview.Build(junk); } catch (Exception) { threw = true; }
            True("pipe preview tolerates zero size", !threw);
            junk = ManholeParams.SampleCircular();
            junk.Pipes.Clear();
            True("pipe preview with no pipes", PipeLayoutPreview.Build(junk).Model.Items.Count > 0);
        }

        private static void Steps()
        {
            // mặc định tắt: không có bậc, không có layer S-STEP
            var off = ManholeParams.SampleRectangular();
            True("steps off by default", !off.StepIrons && new ManholeGeometry(off).Steps == null);
            True("steps off draws nothing", DetailComposer.Compose(off).Model.Items.All(i => i.Layer != Layers.Steps));

            // hố chữ nhật: tự chọn tường Nam (không có ống), giữa lỗ thăm
            var p = ManholeParams.SampleRectangular();
            p.StepIrons = true;
            var g = new ManholeGeometry(p);
            var s = g.Steps;
            True("rect steps placed", s != null && g.IsValid);
            True("rect steps on south wall, facing in", Math.Abs(s.Normal.Y - 1) < 1e-9 && Math.Abs(s.WallPoint.Y + 600) < 1e-6);
            Eq("rect steps centred under opening", 0, s.WallPoint.X, 1e-6);
            Eq("rect top step = soffit - 300", g.H - 300, s.Levels[0], 1e-6);
            True("rect steps spaced 300", s.Levels.Zip(s.Levels.Skip(1), (a, b) => a - b).All(d => Math.Abs(d - 300) < 1e-6));
            True("rect bottom step >= benching + 300", s.Levels.Last() >= g.Outlet.ZInvert + g.Outlet.DN + 300 - 1e-6);
            True("rect steps no warning", g.Warnings.All(w => !w.Contains("Bậc thang")));
            var res = DetailComposer.Compose(p);
            True("rect steps in plan", res.Model.Items.Any(i => i.Layer == Layers.Steps));
            True("rect steps note", res.Model.Items.OfType<PText>().Any(t => t.Text.Contains("STEP IRONS GALVANISED")));

            // đặt tay lên tường Đông (có ống ra P3): vẫn vẽ nhưng cảnh báo trùng ống
            p.StepAuto = false; p.StepAngle = 0;
            g = new ManholeGeometry(p);
            True("manual east wall", g.Steps != null && Math.Abs(g.Steps.Normal.X + 1) < 1e-9);
            True("manual over outlet warns", g.Warnings.Any(w => w.Contains("trùng vị trí lỗ ống P3")));
            // góc vào sát góc tường: không vừa → bỏ, có cảnh báo
            p.StepAngle = 45;
            g = new ManholeGeometry(p);
            True("corner angle skipped", g.Steps == null && g.Warnings.Any(w => w.Contains("quá sát góc")));

            // hố tròn đổ tại chỗ (mẫu bật bậc thang): dưới lỗ thăm lệch Nam
            var c = new ManholeGeometry(ManholeParams.SampleCircularCast());
            True("circ cast steps at 270", c.Steps != null && Math.Abs(c.Steps.AngleDeg - 270) < 1e-6);
            True("circ cast steps no warning", c.Warnings.All(w => !w.Contains("Bậc thang")));

            // section: bậc trên tường bị cắt → nhìn nghiêng; xoay hố sao cho bậc nằm ở tường Tây (song song phương nhìn)
            var west = ManholeParams.SampleRectangular();
            west.StepIrons = true; west.StepAuto = false; west.StepAngle = 180; west.Pipes[0].Wall = WallSide.South; // P1 sang tường Nam
            var rw = DetailComposer.Compose(west);
            True("west steps compose ok: " + string.Join("; ", rw.Errors), rw.Ok);
            int stepPrims = rw.Model.Items.Count(i => i.Layer == Layers.Steps);
            Eq("west steps: 1 plan U + one profile per step", 1 + rw.Geometry.Steps.Levels.Count, stepPrims);

            // thông số sai
            var bad = ManholeParams.SampleRectangular();
            bad.StepIrons = true; bad.StepSpacing = 50;
            True("bad step spacing error", !new ManholeGeometry(bad).IsValid);

            // JSON thiếu trường bậc thang → mặc định
            var partial = ParamsJson.Deserialize("{\"id\":\"X\",\"pipes\":[{\"mark\":\"A\",\"il\":10.0,\"role\":\"outlet\"}]}");
            True("json default steps", !partial.StepIrons && partial.StepAuto && Math.Abs(partial.StepSpacing - 300) < 1e-9);
        }

        private static void Validation()
        {
            var p = ManholeParams.SampleRectangular();
            p.CoverLevel = 13.0; // nắp quá thấp → ống chạm bản nắp
            True("low cover gives error", !new ManholeGeometry(p).IsValid);

            p = ManholeParams.SampleRectangular();
            p.Pipes[1].Wall = WallSide.West; // P2 cùng tường với P1, chồng lỗ
            p.Pipes[1].Offset = 100;
            p.Pipes[1].InvertLevel = 12.450;
            True("clash gives error", new ManholeGeometry(p).Errors.Any(e => e.Contains("chồng")));

            p = ManholeParams.SampleRectangular();
            p.Pipes.ForEach(x => x.Role = PipeRole.Inlet);
            var g = new ManholeGeometry(p);
            True("no outlet -> warning + lowest", g.IsValid && g.Outlet.Spec.Mark == "P3" && g.Warnings.Count > 0);
        }

        private static void Naming()
        {
            True("group base name", DetailNaming.GroupBaseName("MH 01/a") == "LCT_MH_01_a");
            True("group base name empty id", DetailNaming.GroupBaseName("") == "LCT_MH");
            var taken = new HashSet<string> { "LCT_MH01", "LCT_MH01_2" };
            True("unique name skips taken", DetailNaming.UniqueName("LCT_MH01", taken.Contains) == "LCT_MH01_3");
            True("unique name free", DetailNaming.UniqueName("LCT_MH02", taken.Contains) == "LCT_MH02");
            True("belongs exact", DetailNaming.BelongsTo("LCT_MH01", "LCT_MH01"));
            True("belongs numbered", DetailNaming.BelongsTo("LCT_MH01_2", "LCT_MH01"));
            True("not belongs prefix", !DetailNaming.BelongsTo("LCT_MH010", "LCT_MH01"));

            string json = ParamsJson.Serialize(ManholeParams.SampleRectangular());
            var chunks = DetailNaming.Chunk(json, DetailNaming.XrecordChunkSize);
            True("chunks within limit", chunks.All(c => c.Length <= DetailNaming.XrecordChunkSize));
            True("chunks rejoin", string.Concat(chunks) == json);
            Eq("chunk empty", 0, DetailNaming.Chunk("", 250).Count);
        }

        /// <summary>
        /// Ghi chú không được đè kích thước: với mọi mẫu, chữ của leader không đè lên số kích thước / nhãn cao độ,
        /// và đường leader không cắt qua số kích thước.
        /// </summary>
        private static void Overlap()
        {
            var all = new List<ManholeParams> { ManholeParams.SampleRectangular(), ManholeParams.SampleCircular(), ManholeParams.SampleCircularCast() };
            string samples = Snapshots.FindSamplesDir();
            if (samples != null)
                foreach (var f in Directory.GetFiles(samples, "*.json")) all.AddRange(ParamsJson.LoadMany(f));
            foreach (var p in all)
            {
                var res = DetailComposer.Compose(p);
                if (!res.Ok) { True("overlap sample " + p.Id + " composes", false); continue; }
                var st = new DrawStyle(p.Scale);
                var dims = res.Model.Items.OfType<PDim>().Select(d => Annot.DimTextRect(d, st)).ToList();
                var texts = res.Model.Items.SelectMany(i => i is PBlock ? SymbolLibrary.Expand((PBlock)i).Items : new List<Prim> { i })
                    .OfType<PText>().Where(t => t.Layer == Layers.Text).Select(t => Annot.TextRect(t)).ToList();
                foreach (var l in res.Model.Items.OfType<PLeader>())
                {
                    // hộp chữ ghi chú sát chữ; vật cản đã nới ClearanceFactor → chữ ghi chú phải cách số kích thước / chữ khác ≥ khoảng trống đó
                    var box = Annot.TextRect(new PText { Position = l.TextPosition, Height = l.TextHeight, Text = l.Text, Align = l.Align }, 0);
                    string name = p.Id + " leader '" + l.Text.Replace('\n', ' ') + "'";
                    True(name + " text clear of dim text", !dims.Any(r => Overlaps(r, box)));
                    True(name + " text clear of other text", !texts.Any(r => Overlaps(r, box)));
                    for (int i = 0; i + 1 < l.Points.Count; i++)
                        True(name + " line clear of dim text", !dims.Any(r => GeomUtil.SegmentIntersectsRect(l.Points[i], l.Points[i + 1], r)));
                }
            }
        }

        /// <summary>
        /// Cách xếp ghi chú: khoảng hở giữa hai ghi chú liền nhau ≥ 1.2 chiều cao chữ; leader hoặc ngang, hoặc gãy khúc
        /// với đoạn xiên đúng 45° / đứng 90° từ điểm đặt và landing ngang tới chữ; bản nắp cùng tim X với mặt bằng.
        /// </summary>
        private static void CalloutLayout()
        {
            var st = new DrawStyle(20);
            double h = st.TextSmall;

            // leader thẳng / 45° / 90°
            var flat = Annot.LeaderPoints(new Vec2(0, 0), -500, 0, h);
            True("leader flat has 2 points", flat.Count == 2);
            var diag = Annot.LeaderPoints(new Vec2(0, 0), -500, -200, h);
            True("segments cross", GeomUtil.SegmentsCross(new Vec2(0, 0), new Vec2(10, 10), new Vec2(0, 10), new Vec2(10, 0)));
            True("segments touching at end do not cross", !GeomUtil.SegmentsCross(new Vec2(0, 0), new Vec2(10, 10), new Vec2(10, 10), new Vec2(20, 0)));
            True("parallel segments do not cross", !GeomUtil.SegmentsCross(new Vec2(0, 0), new Vec2(10, 10), new Vec2(0, 5), new Vec2(10, 15)));
            True("leader 45°: 3 points", diag.Count == 3);
            Eq("leader 45° knee x", -200, diag[1].X);
            Eq("leader 45° knee y", -200, diag[1].Y);
            var vert = Annot.LeaderPoints(new Vec2(0, 0), -500, -480, h);
            True("leader 90° when 45° leaves no landing", vert.Count == 3 && vert[1].X == 0 && vert[1].Y == -480);

            // cột ghi chú: nhiều điểm cùng cao độ → chữ xếp xuống, cách nhau ≥ 1.2h, leader gãy đúng góc
            var m = new DrawingModel();
            var items = Enumerable.Range(0, 4).Select(i => new Annot.Callout(new Vec2(0, -i * 10), "NOTE " + i + "\nLINE 2")).ToList();
            Annot.CalloutColumn(m, st, items, -800, -1);
            var leaders = m.Items.OfType<PLeader>().OrderByDescending(l => l.TextPosition.Y).ToList();
            Eq("column count", 4, leaders.Count);
            for (int i = 0; i + 1 < leaders.Count; i++)
            {
                double bottom = leaders[i].TextPosition.Y - 2 * h * PText.LineSpacing / 2;
                double top = leaders[i + 1].TextPosition.Y + 2 * h * PText.LineSpacing / 2;
                True("column gap " + i + " >= 1.2h", bottom - top >= Annot.CalloutGapFactor * h - 1e-6);
            }
            foreach (var l in leaders)
            {
                var pts = l.Points;
                True("leader last segment horizontal", Math.Abs(pts[pts.Count - 1].Y - pts[pts.Count - 2].Y) < 1e-9);
                if (pts.Count == 3)
                {
                    var d = pts[1] - pts[0];
                    True("leader first segment 45° or 90°", Math.Abs(Math.Abs(d.X) - Math.Abs(d.Y)) < 1e-9 || Math.Abs(d.X) < 1e-9);
                }
            }

            // mọi mẫu: leader đều ngang hoặc gãy đúng góc, cùng quy tắc
            foreach (var p in new[] { ManholeParams.SampleRectangular(), ManholeParams.SampleCircular(), ManholeParams.SampleCircularCast() })
            {
                var res = DetailComposer.Compose(p);
                foreach (var l in res.Model.Items.OfType<PLeader>())
                {
                    var pts = l.Points;
                    True(p.Id + " leader '" + l.Text.Split('\n')[0] + "' ends horizontal", Math.Abs(pts[pts.Count - 1].Y - pts[pts.Count - 2].Y) < 1e-9);
                    if (pts.Count == 3)
                    {
                        var d = pts[1] - pts[0];
                        True(p.Id + " leader '" + l.Text.Split('\n')[0] + "' 45°/90°", Math.Abs(Math.Abs(d.X) - Math.Abs(d.Y)) < 1e-9 || Math.Abs(d.X) < 1e-9);
                    }
                }
                // leader trong cùng một hình không cắt nhau
                var all = res.Model.Items.OfType<PLeader>().ToList();
                for (int a = 0; a < all.Count; a++)
                    for (int b = a + 1; b < all.Count; b++)
                    {
                        bool cross = false;
                        for (int i = 0; i + 1 < all[a].Points.Count; i++)
                            for (int j = 0; j + 1 < all[b].Points.Count; j++)
                                cross |= GeomUtil.SegmentsCross(all[a].Points[i], all[a].Points[i + 1], all[b].Points[j], all[b].Points[j + 1]);
                        True(p.Id + " leaders '" + all[a].Text.Split('\n')[0] + "' / '" + all[b].Text.Split('\n')[0] + "' do not cross", !cross);
                    }

                // bản nắp cùng tim X với mặt bằng (tiêu đề cả hai căn giữa tại tim)
                var titles = res.Model.Items.OfType<PText>().Where(t => t.Layer == Layers.Title).ToList();
                var plan = titles.First(t => t.Text.StartsWith("PLAN"));
                var roof = titles.First(t => t.Text.StartsWith("ROOF SLAB"));
                Eq(p.Id + " roof plan aligned with plan", plan.Position.X, roof.Position.X);
            }
        }

        /// <summary>Block ký hiệu (cao độ, ký hiệu cắt): khai triển đúng vị trí, attribute thay giá trị, chữ cái đứng thẳng.</summary>
        private static void Blocks()
        {
            var st = new DrawStyle(20);
            var m = new DrawingModel();
            var lv = Annot.Level(m, st, 100, 200, "TOB", 12.313, 1);
            True("level is a block", m.Items.Count == 1 && lv.Name == SymbolLibrary.LevelRight);
            var ex = SymbolLibrary.Expand(lv).Items;
            var tri = ex.OfType<PPolyline>().First(p => p.Closed);
            Eq("level apex x", 100, tri.Points[0].X);
            Eq("level apex y", 200, tri.Points[0].Y);
            var txt = ex.OfType<PText>().Single();
            True("level attribute substituted", txt.Text == "TOB RL 12.313" && txt.AttributeTag == SymbolLibrary.LevelTag);
            Eq("level text x", 100 + 1.8 * 1.2 * 20, txt.Position.X);
            Eq("level text y", 200 + 1.8 * 1.4 * 20, txt.Position.Y);
            Eq("level text height", st.TextSmall, txt.Height);
            True("expansion uses block layer", ex.All(p => p.Layer == Layers.Text));
            True("library definition untouched", SymbolLibrary.Get(SymbolLibrary.LevelRight).Items.OfType<PText>().Single().Text == "RL 0.000");

            // ký hiệu cắt: đường cắt dọc trục X, nhìn về +Y → không quay; đầu trái nét đậm về +X, đầu phải về -X
            var m2 = new DrawingModel();
            Annot.SectionMarker(m2, st, new Vec2(-1000, 0), new Vec2(1000, 0), new Vec2(0, 1), "A");
            var blocks = m2.Items.OfType<PBlock>().ToList();
            Eq("section marker: 2 blocks", 2, blocks.Count);
            True("section marker names", blocks[0].Name == SymbolLibrary.SectionRight && blocks[1].Name == SymbolLibrary.SectionLeft);
            Eq("section marker rotation", 0, blocks[0].Rotation);
            var letter = SymbolLibrary.Expand(blocks[1]).Items.OfType<PText>().Single();
            True("section letter", letter.Text == "A" && letter.KeepUpright);
            Eq("section letter y", -3.5 * 20, letter.Position.Y);

            // nhìn về -X (mặt cắt dọc trục Y): block quay 90°, mũi tên chỉ -X, chữ cái vẫn đứng thẳng
            var m3 = new DrawingModel();
            Annot.SectionMarker(m3, st, new Vec2(0, -1000), new Vec2(0, 1000), new Vec2(-1, 0), "A");
            var b3 = m3.Items.OfType<PBlock>().First();
            var e3 = SymbolLibrary.Expand(b3).Items;
            var arrow = e3.OfType<PPolyline>().First(p => p.Closed);
            Eq("rotated arrow tip x", -6 * 20, arrow.Points[0].X);
            Eq("rotated arrow tip y", -1000, arrow.Points[0].Y);
            var tick = e3.OfType<PPolyline>().First(p => !p.Closed && p.Points.Count == 2 && p.Points[0].X == 0 && p.Points[0].Y == -1000 && Math.Abs(p.Points[1].X) < 1e-6);
            Eq("rotated tick towards other end", -1000 + 8 * 20, tick.Points[1].Y);
            Eq("rotated letter upright", 0, e3.OfType<PText>().Single().Rotation);
            Eq("rotated letter x", 3.5 * 20, e3.OfType<PText>().Single().Position.X);
        }

        /// <summary>Danh mục ống cho hộp thoại (DN / OD / vật liệu dạng danh sách).</summary>
        private static void Catalog()
        {
            Eq("catalog PVC-U 225", 250, PipeCatalog.OutsideDiameter("PVC-U", 225));
            Eq("catalog pvc-u case-insensitive", 160, PipeCatalog.OutsideDiameter("pvc-u", 150));
            Eq("catalog RCP 225 = 1.2 DN", 270, PipeCatalog.OutsideDiameter("RCP", 225));
            Eq("catalog unknown material", 360, PipeCatalog.OutsideDiameter("XYZ", 300));
            Eq("catalog null material", 360, PipeCatalog.OutsideDiameter(null, 300));
            var ods = PipeCatalog.OutsideDiameters(225);
            True("catalog OD options sorted, > DN, distinct", ods.SequenceEqual(ods.Distinct().OrderBy(v => v)) && ods.All(v => v > 225));
            True("catalog OD options include default and table", ods.Contains(270) && ods.Contains(250) && ods.Contains(259));
            True("catalog sizes ascending", PipeCatalog.Sizes.SequenceEqual(PipeCatalog.Sizes.OrderBy(v => v)));
            True("catalog materials non-empty", PipeCatalog.Materials.Length > 0 && PipeCatalog.Materials.Contains("RCP"));
        }

        private static bool Overlaps(Rect2 a, Rect2 b)
        {
            return a.X1 < b.X2 && b.X1 < a.X2 && a.Y1 < b.Y2 && b.Y1 < a.Y2;
        }

        /// <summary>SVG của samples/*.json phải khớp samples/snapshots (ghi lại bằng lệnh "snapshot").</summary>
        private static void Snapshot()
        {
            string samples = Snapshots.FindSamplesDir();
            if (samples == null) { True("samples/ folder found", false); return; }
            string dir = Snapshots.SnapshotDir(samples);
            var rendered = Snapshots.Render(samples);
            var stored = Directory.Exists(dir) ? Directory.GetFiles(dir, "*.svg").Select(Path.GetFileName).ToList() : new List<string>();
            foreach (var name in stored.Where(n => !rendered.ContainsKey(n)))
                True("snapshot " + name + " has a matching sample", false);
            foreach (var kv in rendered)
            {
                string path = Path.Combine(dir, kv.Key);
                if (!File.Exists(path)) { True("snapshot " + kv.Key + " exists (run: snapshot)", false); continue; }
                string diff = Snapshots.Diff(File.ReadAllText(path), kv.Value);
                True("snapshot " + kv.Key + (diff == null ? "" : " differs (run: snapshot, then review the SVG diff)\n" + diff), diff == null);
            }
        }

        private static void Json()
        {
            var p = ManholeParams.SampleCircular();
            string json = ParamsJson.Serialize(p);
            True("json has enum text", json.Contains("\"CircularPrecast\"") && json.Contains("\"Outlet\""));
            var back = ParamsJson.Deserialize(json);
            True("json type", back.Type == ManholeType.CircularPrecast);
            Eq("json pipes", 3, back.Pipes.Count);
            Eq("json IL", 11.35, back.Pipes[2].InvertLevel);
            True("json role", back.Pipes[2].Role == PipeRole.Outlet);
            Eq("json bar", 16, back.BaseBottom.Diameter);
            True("json wallSingleLayer default false", !back.WallSingleLayer && !json.Contains("\"wallSingleLayer\":true"));
            p.WallSingleLayer = true;
            True("json wallSingleLayer roundtrip", ParamsJson.Deserialize(ParamsJson.Serialize(p)).WallSingleLayer);

            // file nhiều hố ga (vẽ hàng loạt)
            string tmp = Path.Combine(Path.GetTempPath(), "lct-batch-" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(tmp, "﻿[" + ParamsJson.Serialize(ManholeParams.SampleRectangular()) + "," + json + "]");
            var many = ParamsJson.LoadMany(tmp);
            File.Delete(tmp);
            Eq("batch count", 2, many.Count);
            True("batch types", many[0].Type == ManholeType.RectangularCast && many[1].Type == ManholeType.CircularPrecast);

            // JSON thiếu trường → lấy mặc định
            var partial = ParamsJson.Deserialize("{\"id\":\"X\",\"pipes\":[{\"mark\":\"A\",\"il\":10.0,\"role\":\"outlet\"}]}");
            Eq("partial default length", 1500, partial.InternalLength);
            Eq("partial pipe dn", 300, partial.Pipes[0].NominalDiameter);
            True("partial role case-insensitive", partial.Pipes[0].Role == PipeRole.Outlet);
        }
    }
}
