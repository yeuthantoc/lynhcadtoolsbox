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
                    case "rebar":
                        if (args.Length < 3) { Console.WriteLine("usage: rebar <params.json> <out.svg>"); return 2; }
                        var rv = RebarLayoutPreview.Build(ParamsJson.Load(args[1]));
                        foreach (var e in rv.Errors) Console.WriteLine("ERROR: " + e);
                        File.WriteAllText(args[2], SvgExporter.Export(rv.Model, 20));
                        return 0;
                    case "pipesec":
                        if (args.Length < 3) { Console.WriteLine("usage: pipesec <params.json> <out.svg>"); return 2; }
                        var sv = PipeSectionPreview.Build(ParamsJson.Load(args[1]));
                        foreach (var e in sv.Errors) Console.WriteLine("ERROR: " + e);
                        File.WriteAllText(args[2], SvgExporter.Export(sv.Model, 20));
                        return 0;
                    case "snapshot":
                        string samples = Snapshots.FindSamplesDir();
                        if (samples == null) { Console.WriteLine("samples/ not found"); return 2; }
                        return Snapshots.Write(samples);
                    default:
                        Console.WriteLine("commands: check | svg <json> <svg> | pipes <json> <svg> | dims <json> <svg> | rebar <json> <svg> | pipesec <json> <svg> | sample <folder> | snapshot");
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
            Rebate();
            CogsAndBends();
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
            // chân L vào đáy + cog vào bản nắp → chữ U: A = chân 300, B = đoạn đứng, C = cog chuẩn N12 (5d)
            True("W1 is U (leg + cog)", w1.Shape == BarShape.U);
            Eq("W1 leg", 300, w1.A);
            Eq("W1 cog", RebarCatalog.StandardCog(12, 5), w1.C);
            Eq("W1 bend dia 5d", 60, w1.BendDia);
            True("W1 length allows for bends", w1.Length < w1.A + w1.B + w1.C && w1.Length > w1.A + w1.B + w1.C - 4 * 12);
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
            Eq("single layer: half the U-bars", sch.Marks.Where(m => m.Shape == BarShape.U && m.Mark != "W1").Sum(m => m.Count) / 2,
                rd1.Schedule.Marks.Where(m => m.Shape == BarShape.U && m.Mark != "W1").Sum(m => m.Count));
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
            // Key = tên thuộc tính ManholeParams (hộp thoại nổi bật theo đường dẫn binding của ô nhập)
            var keyed = dp.Model.Items.Where(i => !string.IsNullOrEmpty(i.Key)).ToList();
            True("dim preview keys are ManholeParams properties", keyed.Count > 0
                && keyed.All(i => typeof(ManholeParams).GetProperty(i.Key) != null));
            True("dim preview: every field dim has a key", dims.All(d => !string.IsNullOrEmpty(d.Key) || d.Tip.StartsWith(DimensionPreview.TipHeight)));
            True("dim preview wall key", dims.Any(d => d.Key == "WallThickness"));
            True("dim preview CL key", dp.Model.Items.OfType<PText>().Any(t => t.Key == "CoverLevel"));
            True("dim preview no offset dims when centred", !dims.Any(d => d.Tip.StartsWith(DimensionPreview.TipOffX)));
            var pOff = p.Clone(); pOff.OpeningOffsetX = 150;
            True("dim preview offset dim appears", DimensionPreview.Build(pOff).Model.Items.OfType<PDim>().Any(d => d.Tip.StartsWith(DimensionPreview.TipOffX + " = 150")));
            True("dim preview CL text", dp.Model.Items.OfType<PText>().Any(t => t.Text.StartsWith("CL ") && t.Tip.StartsWith(DimensionPreview.TipCL)));
            var noPipes = p.Clone(); noPipes.Pipes.Clear();
            var dpNo = DimensionPreview.Build(noPipes);
            True("dim preview without pipes still draws", dpNo.Errors.Count > 0 && dpNo.Model.Items.OfType<PDim>().Any(d => d.Tip.StartsWith(DimensionPreview.TipBase)));

            // preview mặt cắt ống (tab Pipes): nét ống mang Key = tên ống, ống ra xanh, nhãn IL
            var ps = PipeSectionPreview.Build(p);
            True("pipe section preview builds", ps.Errors.Count == 0 && ps.Model.Items.Count > 0);
            True("pipe section preview: outlet on outlet layer", ps.Model.Items.Any(i => i.Layer == PipeLayoutPreview.OutletLayer && i.Key == "P3"));
            True("pipe section preview: inlet keyed", ps.Model.Items.Any(i => i.Key == "P1" && i.Layer == Layers.Pipe));
            True("pipe section preview: labels", ps.Model.Items.OfType<PText>().Any(t => t.Text == "P3 IL 12.400" && t.Key == "P3")
                && ps.Model.Items.OfType<PText>().Any(t => t.Text == "SECTION A-A"));
            True("pipe section preview: no dims/leaders", !ps.Model.Items.Any(i => i is PDim || i is PLeader || i is PBlock));
            var noPipeParams = p.Clone(); noPipeParams.Pipes.Clear();
            True("pipe section preview invalid -> empty", PipeSectionPreview.Build(noPipeParams).Model.Items.Count == 0);
            True("pipe plan preview pipes keyed", PipeLayoutPreview.Build(p).Model.Items.Count(i => i.Key == "P1") >= 3);
            True("section pipes keyed in drawing model", new SectionBuilder(g, rd, new DrawStyle(20)).Build().Items.Any(i => i.Key == "P3" && i.Layer == Layers.Pipe));

            // preview bố trí thép (tab Reinforcement)
            var rp = RebarLayoutPreview.Build(p);
            True("rebar preview builds", rp.Errors.Count == 0 && rp.Model.Items.Count > 0);
            True("rebar preview has rebar", rp.Model.Items.Any(i => i.Layer == Layers.Rebar));
            True("rebar preview has no dims/leaders", !rp.Model.Items.Any(i => i is PDim || i is PLeader || i is PBlock));
            True("rebar preview has plan and section", rp.Model.Items.OfType<PText>().Any(t => t.Text == "PLAN") && rp.Model.Items.OfType<PText>().Any(t => t.Text == "SECTION A-A"));
            // mặt bằng (thép tường = chấm + đường) nằm phía trên mặt cắt
            double planTitleY = rp.Model.Items.OfType<PText>().First(t => t.Text == "PLAN").Position.Y;
            double secTitleY = rp.Model.Items.OfType<PText>().First(t => t.Text == "SECTION A-A").Position.Y;
            True("rebar preview plan above section", planTitleY > secTitleY && rp.Model.Items.OfType<PDot>().Any(d => d.Center.Y > secTitleY));
            // thép ngang phía trong → đường thép ngang trên mặt bằng dịch vào trong (vòng ngoài hẹp hơn)
            var pHin = p.Clone(); pHin.WallHorizontalInside = true;
            double outerLineX(ManholeParams q) => RebarLayoutPreview.Build(q).Model.Items.OfType<PPolyline>()
                .Where(l => l.Layer == Layers.Rebar && l.Points.Count == 2 && Math.Abs(l.Points[0].X - l.Points[1].X) < 1e-6 && l.Points[0].Y > secTitleY)
                .Min(l => l.Points[0].X);
            True("rebar preview plan follows horizontal-inside option", outerLineX(pHin) > outerLineX(p) + 5);
            // mọi primitive thép mang Key = thuộc tính ManholeParams (hoặc "Trimmers"); chú giải cũng có Key
            var rebarItems = rp.Model.Items.Where(i => i.Layer == Layers.Rebar).ToList();
            True("rebar preview: all bars keyed", rebarItems.Count > 0 && rebarItems.All(i => !string.IsNullOrEmpty(i.Key)));
            True("rebar preview: keys valid", rp.Model.Items.Where(i => !string.IsNullOrEmpty(i.Key) && i.Layer != Layers.Pipe && i.Layer != Layers.PipeHidden)
                .Select(i => i.Key).Distinct().All(k => k == RebarKeys.Trimmers || typeof(ManholeParams).GetProperty(k) != null));
            foreach (var k in new[] { RebarKeys.WallVertical, RebarKeys.WallHorizontal, "BaseBottom", "BaseTop", "RoofBottom", RebarKeys.Trimmers })
                True("rebar preview key present: " + k, rebarItems.Any(i => i.Key == k) && rp.Model.Items.OfType<PText>().Any(t => t.Key == k));
            True("rebar preview: no RoofTop key when no roof top mat", !rebarItems.Any(i => i.Key == "RoofTop"));
            True("KeysForField spacing field", RebarLayoutPreview.KeysForField("WallVertical").SequenceEqual(new[] { RebarKeys.WallVertical }));
            True("KeysForField trimmer count", RebarLayoutPreview.KeysForField("TrimmerCount").SequenceEqual(new[] { RebarKeys.Trimmers }));
            True("KeysForField wall option -> both wall sets", RebarLayoutPreview.KeysForField("WallHorizontalInside").Length == 2);
            True("KeysForField top mat checkbox", RebarLayoutPreview.KeysForField("BaseTopMat").SequenceEqual(new[] { "BaseTop" }));
            True("KeysForField rebate -> roof top + trimmers", RebarLayoutPreview.KeysForField("CoverRebateDepth").SequenceEqual(new[] { "RoofTop", RebarKeys.Trimmers }));
            True("KeysForField unrelated -> empty", RebarLayoutPreview.KeysForField("Cover").Length == 0 && RebarLayoutPreview.KeysForField(null).Length == 0);
            var legend = RebarLayoutPreview.Legend(g, rd);
            True("rebar preview legend", legend.Count >= 4 && legend[0].StartsWith("W1 ") && legend.Any(s => s.EndsWith(" BOT EW")));
            var bad = p.Clone(); bad.WallThickness = 50;
            var rpBad = RebarLayoutPreview.Build(bad);
            True("rebar preview invalid params -> errors, no model", rpBad.Errors.Count > 0 && rpBad.Model.Items.Count == 0);
            True("has W2/W3 U-bars", sch.Marks.Count(m => m.Shape == BarShape.U && m.Mark != "W1") == 2);
            True("WallHoriz callout", sch.Callout(BarSets.WallHoriz).StartsWith("W2/W3 N12-200 EF HORIZ"));
            True("base mat callout", sch.MatCallout(BarSets.BaseBot, false).EndsWith(" N16-200 BOT EW"));
            True("missing roof top mat -> null", sch.MatCallout(BarSets.RoofTop, true) == null);
            bool threw = false;
            try { sch.Callout("NoSuchSet"); } catch (KeyNotFoundException) { threw = true; }
            True("unknown bar set throws", threw);

            // bản đáy: 1500+400+300 = 2200 x 1900 → thanh theo X dài 2100
            var bx = sch.Marks.First(m => m.Mark.StartsWith("B") && Math.Abs(m.A - 2100) < 1e-6);
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
            True("circ cast W1 is U (leg + cog)", vert.Shape == BarShape.U);
            // vòng ngoài r = 950-68 = 882 → ceil(2πr/200) = 28; vòng trong cùng số thanh, cùng góc (thẳng hàng)
            Eq("circ cast vertical count", 28 + 28, vert.Count);
            var ringPts = RebarDesigner.VerticalPositions(g, rd.Layout);
            True("circ cast faces share angles", ringPts.Take(28).Zip(ringPts.Skip(28), (a, b) => Math.Abs(a.Angle() - b.Angle()) < 1e-9).All(x => x));
            True("circ precast rebar preview", RebarLayoutPreview.Build(ManholeParams.SampleCircular()).Errors.Count == 0);
            var dpPre = DimensionPreview.Build(ManholeParams.SampleCircular()).Model.Items.OfType<PDim>().Select(d => d.Tip).ToList();
            foreach (var tip in new[] { DimensionPreview.TipDiameter, DimensionPreview.TipPrecastWall, DimensionPreview.TipRing, DimensionPreview.TipOpeningDia })
                True("circ precast dim preview tip: " + tip, dpPre.Any(t => t.StartsWith(tip + " =")));
            True("circ precast dim preview: no rect tips", !dpPre.Any(t => t.StartsWith(DimensionPreview.TipLength) || t.StartsWith(DimensionPreview.TipWall + " =")));
            var preKeys = DimensionPreview.Build(ManholeParams.SampleCircular()).Model.Items.Select(i => i.Key).Where(k => !string.IsNullOrEmpty(k)).Distinct().ToList();
            True("circ precast keys", preKeys.Contains("PrecastWallThickness") && preKeys.Contains("RingHeight") && preKeys.Contains("InternalDiameter")
                && preKeys.All(k => typeof(ManholeParams).GetProperty(k) != null));
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

        /// <summary>
        /// Rãnh khung nắp (cover rebate): bản nắp hạ xuống quanh lỗ thăm, lưới trên + thanh gia cường lớp trên
        /// trong phạm vi rãnh nhấn xuống (thanh gãy CRANKED / DROPPED, dốc 1:2), khung nắp đặt trong rãnh.
        /// </summary>
        private static void Rebate()
        {
            // mặc định tắt: không có vùng nhấn, không có thanh gãy, bảng không có cột D
            var off = ManholeParams.SampleRectangular();
            off.RoofTopMat = true;
            var gOff = new ManholeGeometry(off);
            True("rebate off by default", !off.CoverRebate && !gOff.HasRebate);
            var rdOff = RebarDesigner.Design(gOff);
            True("rebate off: no drop zones", rdOff.Layout.RoofLayers.All(l => l.Drop == null) && rdOff.Layout.RoofTrimmers.All(t => t.Drop == null));
            True("rebate off: no cranked bars", rdOff.Schedule.Marks.All(mk => mk.Shape != BarShape.Cranked && mk.Shape != BarShape.Dropped));
            var tblOff = DetailComposer.Compose(off).Model.Items.OfType<PTable>().First();
            True("rebate off: no DROPPED row, D column empty", tblOff.Rows.Skip(1).All(r => r[6] == "-" || r[6] == ""));

            // trắc dọc thanh qua vùng nhấn
            var dz = new DropZone { Along = new Interval(-600, 600), Perp = new Interval(-450, 450), Depth = 50, Run = 100 };
            Eq("drop z outside", 100, dz.ZAt(-800, 100));
            Eq("drop z inside", 50, dz.ZAt(0, 100));
            Eq("drop z mid ramp", 75, dz.ZAt(-650, 100));
            var full = dz.Profile(new Interval(-1000, 1000), 100);
            Eq("double crank profile: 6 vertices", 6, full.Count);
            var itFull = DropZone.Item(full, "S", 'R', 12);
            True("double crank -> DROPPED", itFull.Shape == BarShape.Dropped);
            Eq("dropped A", 300, itFull.A); Eq("dropped B", 50, itFull.B); Eq("dropped C", 1200, itFull.C); Eq("dropped D", 300, itFull.D);
            Eq("dropped length", 300 + 1200 + 300 + 2 * 50 * Math.Sqrt(5), itFull.Length, 1e-6);
            var half = dz.Profile(new Interval(-1000, -500), 100);
            Eq("single crank profile: 4 vertices", 4, half.Count);
            var itHalf = DropZone.Item(half, "S", 'R', 12);
            True("single crank -> CRANKED", itHalf.Shape == BarShape.Cranked);
            Eq("cranked A (longer leg first)", 300, itHalf.A); Eq("cranked C", 100, itHalf.C); Eq("cranked B", 50, itHalf.B);
            var flat = dz.Profile(new Interval(-1000, -800), 100);
            True("piece clear of rebate stays straight", flat.Count == 2 && DropZone.Item(flat, "S", 'R', 12).Shape == BarShape.Straight);
            True("DROPPED signature differs from STRAIGHT", itFull.Signature != new BarItem { Group = 'R', Dia = 12, Shape = BarShape.Straight, A = itFull.A }.Signature);

            // hố chữ nhật mẫu + lưới trên + rãnh 50 sâu, rộng hơn lỗ 150 mỗi phía
            var p = ParamsJson.LoadMany(Path.Combine(Snapshots.FindSamplesDir(), "rect-mh05-rebate.json"))[0];
            var g = new ManholeGeometry(p);
            True("rebate rect valid: " + string.Join("; ", g.Errors), g.IsValid && g.HasRebate);
            Eq("rebate half X", 450 + 150, g.RebateHalfX);
            Eq("rebate half Y", 300 + 150, g.RebateHalfY);
            Eq("rebate depth", 50, g.RebateDepth);
            var rd = RebarDesigner.Design(g);
            var L = rd.Layout;
            True("bottom mat not dropped", L.RoofLayers.Where(l => !l.IsTop).All(l => l.Drop == null));
            var topX = L.RoofLayers.First(l => l.Key == BarSets.X(BarSets.RoofTop));
            True("top mat X has drop zone", topX.Drop != null && Math.Abs(topX.Drop.Along.Min + 600) < 1e-6 && Math.Abs(topX.Drop.Perp.Max - 450) < 1e-6);
            Eq("crank run = 2 x depth", 100, topX.Drop.Run);
            // thanh ngoài dải rãnh: thẳng; thanh trong dải nhưng không bị lỗ cắt: nhấn 2 chỗ; thanh bị lỗ cắt: mỗi đoạn gãy 1 chỗ
            double posOut = topX.Positions.First(y => Math.Abs(y) > 450 + 1e-6);
            double posDrop = topX.Positions.First(y => Math.Abs(y) <= 450 && Math.Abs(y) > 300 + 50);
            double posCut = topX.Positions.First(y => Math.Abs(y) <= 300 + 50);
            True("top bar outside rebate band is straight", topX.Profile(posOut, topX.Run).Count == 2);
            var dropped = topX.Profile(posDrop, topX.Run);
            True("top bar in rebate band is dropped", dropped.Count == 6 && Math.Abs(dropped[2].Y - (topX.Z - 50)) < 1e-9 && Math.Abs(dropped[0].Y - topX.Z) < 1e-9);
            var cutPieces = topX.Pieces(posCut);
            Eq("cut top bar: 2 pieces", 2, cutPieces.Count);
            var cutProfile = topX.Profile(posCut, cutPieces[0]);
            True("cut top bar piece cranked once, ends low", cutProfile.Count == 4 && Math.Abs(cutProfile[3].Y - (topX.Z - 50)) < 1e-9);
            Eq("cut top bar low leg = width - cover", 150 - 50, cutProfile[3].X - cutProfile[2].X, 1e-6);
            Eq("top bar z outside", topX.Z, topX.ZAt(posDrop, -1000));
            Eq("top bar z under rebate", topX.Z - 50, topX.ZAt(posDrop, 0));
            var sch = rd.Schedule;
            True("schedule has DROPPED and CRANKED marks", sch.Marks.Any(mk => mk.Shape == BarShape.Dropped) && sch.Marks.Any(mk => mk.Shape == BarShape.Cranked));
            True("dropped marks only in R / T groups", sch.Marks.Where(mk => mk.Shape == BarShape.Dropped || mk.Shape == BarShape.Cranked).All(mk => mk.Mark[0] == 'R' || mk.Mark[0] == 'T'));
            var rTop = sch.Set(BarSets.X(BarSets.RoofTop));
            True("roof top set lists several marks (straight + dropped + cranked)", rTop.Marks.Count >= 3);
            // thanh gia cường lớp trên nằm trong dải rãnh (c + 8 + k·75 < 150) cũng nhấn xuống
            var topTrims = L.RoofTrimmers.Where(t => t.Drop != null).ToList();
            True("top trimmers carry drop zone", topTrims.Count == 8 && topTrims.All(t => t.Profile().Count == 6));
            True("bottom trimmers straight", L.RoofTrimmers.Where(t => t.Drop == null).All(t => t.Profile().Count == 2));
            True("dropped bar mass counted", sch.Marks.Where(mk => mk.Shape == BarShape.Dropped).All(mk => mk.Length > mk.A + mk.C + mk.D + 2 * mk.B));

            var res = DetailComposer.Compose(p);
            True("rebate compose ok: " + string.Join("; ", res.Errors), res.Ok);
            var tbl = res.Model.Items.OfType<PTable>().First();
            Eq("schedule columns (MARK..MASS with D and COG)", 11, tbl.ColumnCount);
            True("schedule D / COG headers", tbl.Rows[0][6] == "D" && tbl.Rows[0][7] == "COG");
            True("rebate schedule DROPPED row has D", tbl.Rows.Any(r => r[2] == "DROPPED" && r[6] != "-" && r[5] != "-"));
            True("rebate schedule CRANKED row has C, no D", tbl.Rows.Any(r => r[2] == "CRANKED" && r[5] != "-" && r[6] == "-"));
            True("rebate note", res.Model.Items.OfType<PText>().Any(t => t.Text.Contains("REBATED 50 DEEP")));
            True("rebate callout in section", res.Model.Items.OfType<PLeader>().Any(l => l.Text.StartsWith("50 DEEP x 1200x900 REBATE")));
            True("roof plan rebate callout", res.Model.Items.OfType<PLeader>().Any(l => l.Text.StartsWith("1200x900 x 50 DEEP REBATE")));
            True("roof plan top mat note mentions crank", res.Model.Items.OfType<PLeader>().Any(l => l.Text.Contains("CRANKED DOWN 50 UNDER REBATE")));
            // mặt cắt: mặt bản nắp có bậc tại đáy rãnh; thép lớp trên song song mặt cắt vẽ gãy (polyline ≥ 4 đỉnh) và có đỉnh ở cao độ hạ
            var sec = new SectionBuilder(g, rd, new DrawStyle(20)).Build();
            double zTop = g.H + g.Ts;
            True("section concrete has rebate floor line", sec.Items.OfType<PPolyline>().Any(l => l.Layer == Layers.Concrete && l.Points.Count == 2
                && Math.Abs(l.Points[0].Y - (zTop - 50)) < 1e-6 && Math.Abs(l.Points[1].Y - (zTop - 50)) < 1e-6 && Math.Abs(l.Points[0].X - l.Points[1].X) > 50));
            var cranked = sec.Items.OfType<PPolyline>().Where(l => l.Layer == Layers.Rebar && l.Key == "RoofTop" && l.Points.Count >= 4).ToList();
            True("section top bar drawn cranked", cranked.Count == 2 && cranked.All(l => l.Points.Any(q => Math.Abs(q.Y - (topX.Z - 50)) < 1e-6) && l.Points.Any(q => Math.Abs(q.Y - topX.Z) < 1e-6)));
            True("section frame sits in rebate", sec.Items.OfType<PPolyline>().Any(l => l.Layer == Layers.ConcreteBeyond && l.Closed && Math.Abs(l.Points.Min(q => q.Y) - (zTop - 50)) < 1e-6));
            // chấm thép lớp trên theo Y (cắt ngang) nằm trong rãnh phải ở cao độ hạ
            var topY = L.RoofLayers.First(l => l.Key == BarSets.Y(BarSets.RoofTop));
            var topDots = sec.Items.OfType<PDot>().Where(dd => dd.Key == "RoofTop" && Math.Abs(dd.Diameter - new DrawStyle(20).DotSize(topY.Dia)) < 1e-9).ToList();
            True("section top Y dots follow the drop zone", topDots.Count > 0 && topDots.All(dd =>
                Math.Abs(dd.Center.Y - (Math.Abs(dd.Center.X) <= 600 + 1e-6 ? topY.Z - 50 : topY.Z)) < 1e-6));
            // lỗ thăm lệch để có thanh Y nằm trong dải rãnh mà không bị lỗ cắt → chấm ở cao độ hạ
            // (lỗ lệch 400: đường cắt y = 0 ngoài vùng lỗ cắt thép 50..750 nhưng trong rãnh −50..850)
            var shifted = p.Clone(); shifted.InternalWidth = 1600; shifted.OpeningOffsetY = 400;
            var gS = new ManholeGeometry(shifted);
            True("shifted rebate valid: " + string.Join("; ", gS.Errors), gS.IsValid);
            var rdS = RebarDesigner.Design(gS);
            var secS = new SectionBuilder(gS, rdS, new DrawStyle(20)).Build();
            var topYS = rdS.Layout.RoofLayers.First(l => l.Key == BarSets.Y(BarSets.RoofTop));
            var lowDots = secS.Items.OfType<PDot>().Where(dd => dd.Key == "RoofTop" && Math.Abs(dd.Center.Y - (topYS.Z - 50)) < 1e-6).ToList();
            True("section shows dropped top Y dots under rebate", lowDots.Count > 0 && lowDots.All(dd => Math.Abs(dd.Center.X) <= 600 + 1e-6));

            // preview tab General: kích thước rãnh có Tip và Key
            var dp = DimensionPreview.Build(p);
            True("dim preview rebate depth", dp.Model.Items.OfType<PDim>().Any(dd => dd.Tip.StartsWith(DimensionPreview.TipRebateDepth + " = 50") && dd.Key == "CoverRebateDepth"));
            True("dim preview rebate width", dp.Model.Items.OfType<PDim>().Any(dd => dd.Tip.StartsWith(DimensionPreview.TipRebateWidth + " = 150") && dd.Key == "CoverRebateWidth"));
            True("dim preview without rebate has no rebate dims", !DimensionPreview.Build(off).Model.Items.OfType<PDim>().Any(dd => dd.Tip.StartsWith(DimensionPreview.TipRebateDepth)));
            True("rebar preview with rebate", RebarLayoutPreview.Build(p).Errors.Count == 0);

            // hố tròn đổ tại chỗ có rãnh tròn
            var c = ParamsJson.LoadMany(Path.Combine(Snapshots.FindSamplesDir(), "circ-cast-mh06-rebate.json"))[0];
            var gc = new ManholeGeometry(c);
            True("rebate circ valid: " + string.Join("; ", gc.Errors), gc.IsValid && gc.HasRebate);
            Eq("rebate circ radius", 300 + 125, gc.RebateHalfX);
            var rc = DetailComposer.Compose(c);
            True("rebate circ compose ok", rc.Ok);
            True("rebate circ roof plan has rebate circle", rc.Model.Items.OfType<PCircle>().Any(ci => ci.Layer == Layers.Concrete && Math.Abs(ci.Radius - 425) < 1e-6));
            True("rebate circ callout", rc.Model.Items.OfType<PLeader>().Any(l => l.Text.StartsWith("40 DEEP x Ø850 REBATE")));

            // rãnh không có lưới trên: chỉ bê tông + khung nắp đổi, thép không đổi
            var noTop = p.Clone(); noTop.RoofTopMat = false;
            var rdNoTop = RebarDesigner.Design(new ManholeGeometry(noTop));
            True("rebate without top mat: no cranked bars", rdNoTop.Schedule.Marks.All(mk => mk.Shape != BarShape.Cranked && mk.Shape != BarShape.Dropped));
            True("rebate without top mat composes", DetailComposer.Compose(noTop).Ok);

            // kiểm tra thông số
            var deep = p.Clone(); deep.CoverRebateDepth = 120;
            True("rebate too deep (top mat touches bottom mat) -> error", new ManholeGeometry(deep).Errors.Any(e => e.Contains("quá sâu")));
            var thin = p.Clone(); thin.RoofTopMat = false; thin.CoverRebateDepth = 160;
            True("rebate leaves < 100 -> error", new ManholeGeometry(thin).Errors.Any(e => e.Contains("100 mm")));
            var narrow = p.Clone(); narrow.CoverRebateWidth = 40;
            True("rebate narrower than cover -> error", new ManholeGeometry(narrow).Errors.Any(e => e.Contains("rộng hơn lỗ thăm")));
            var wide = p.Clone(); wide.CoverRebateWidth = 2000;
            True("rebate beyond roof edge -> error", !new ManholeGeometry(wide).IsValid);
            var onWall = p.Clone(); onWall.CoverRebateWidth = 400;
            var gWall = new ManholeGeometry(onWall);
            True("rebate over wall -> warning", gWall.IsValid && gWall.Warnings.Any(w => w.Contains("đè lên tường")));

            // JSON: thiếu trường → mặc định (tắt, 50, 150); roundtrip
            var partial = ParamsJson.Deserialize("{\"id\":\"X\",\"pipes\":[{\"mark\":\"A\",\"il\":10.0,\"role\":\"outlet\"}]}");
            True("json default rebate", !partial.CoverRebate && Math.Abs(partial.CoverRebateDepth - 50) < 1e-9 && Math.Abs(partial.CoverRebateWidth - 150) < 1e-9);
            var back = ParamsJson.Deserialize(ParamsJson.Serialize(p));
            True("json rebate roundtrip", back.CoverRebate && Math.Abs(back.CoverRebateDepth - 50) < 1e-9 && Math.Abs(back.CoverRebateWidth - 150) < 1e-9);
        }

        /// <summary>
        /// Cog chuẩn ở đầu thanh (AS 3600 13.1.2.7) và đường kính uốn (Bảng 17.2.3.2): lưới bản móc hai đầu không vượt lớp đối diện,
        /// thép đứng tường móc vào bản nắp, chỗ uốn vẽ cung bán kính tim, chiều dài cắt trừ phần uốn.
        /// </summary>
        private static void CogsAndBends()
        {
            // cog chuẩn: N12 5d → bán kính tim 36, đoạn thẳng = π·36/2 + 70 = 126.5, chân = 162.5 → 170
            Eq("bend radius N12 5d", 36, RebarCatalog.BendRadius(12, 5));
            Eq("standard cog N12", 170, RebarCatalog.StandardCog(12, 5));
            Eq("standard cog N16", 200, RebarCatalog.StandardCog(16, 5));
            Eq("standard cog N20", 240, RebarCatalog.StandardCog(20, 5));
            Eq("standard cog N24", 290, RebarCatalog.StandardCog(24, 5));
            Eq("bend deduction 90°", 2 * 36 - 36 * Math.PI / 2, GeomUtil.BendDeduction(Math.PI / 2, 36), 1e-9);
            Eq("bend deduction straight", 0, GeomUtil.BendDeduction(0, 36));

            // fillet: góc vuông → 2 điểm tiếp tuyến + bulge tan(π/8); đoạn ngắn → bán kính giảm
            var pts = new List<Vec2> { new Vec2(0, 0), new Vec2(100, 0), new Vec2(100, 100) };
            var fp = new List<Vec2>(); var fb = new List<double>();
            GeomUtil.Fillet(pts, 36, fp, fb);
            Eq("fillet points", 4, fp.Count);
            Eq("fillet tangent 1 x", 64, fp[1].X); Eq("fillet tangent 2 y", 36, fp[2].Y);
            Eq("fillet bulge", Math.Tan(Math.PI / 8), fb[1], 1e-12);
            True("fillet bulge sign CCW", fb[1] > 0 && fb[0] == 0 && fb[2] == 0);
            var cw = new List<Vec2> { new Vec2(0, 0), new Vec2(100, 0), new Vec2(100, -100) };
            GeomUtil.Fillet(cw, 36, fp, fb);
            True("fillet bulge sign CW", fb[1] < 0);
            GeomUtil.Fillet(new List<Vec2> { new Vec2(0, 0), new Vec2(20, 0), new Vec2(20, 100) }, 36, fp, fb);
            Eq("fillet clamps radius to half the short leg", 10, fp[1].X);
            GeomUtil.Fillet(new List<Vec2> { new Vec2(0, 0), new Vec2(100, 0) }, 36, fp, fb);
            Eq("fillet of a line is the line", 2, fp.Count);
            var pf = PPolyline.Fillet(pts, 36, Layers.Rebar);
            True("PPolyline.Fillet carries bulges", pf.Points.Count == 4 && pf.Bulges.Count == 4 && Math.Abs(pf.Bulges[1]) > 0);

            // BarItem: chiều dài = tổng đỉnh + cog − phần uốn
            var u = new BarItem { Group = 'W', Dia = 12, Shape = BarShape.U, A = 300, B = 2000, C = 170, BendDia = 60, Count = 1 };
            Eq("U vertex length", 2470, u.VertexLength);
            Eq("U cut length", 2470 - 2 * GeomUtil.BendDeduction(Math.PI / 2, 36), u.Length, 1e-9);
            var sNoBend = new BarItem { Group = 'B', Dia = 16, Shape = BarShape.Straight, A = 2000, CogStart = 200, CogEnd = 200, Count = 1 };
            Eq("cogged straight without bend dia = plain sum", 2400, sNoBend.Length);
            sNoBend.BendDia = 80;
            Eq("cogged straight cut length", 2400 - 2 * GeomUtil.BendDeduction(Math.PI / 2, 48), sNoBend.Length, 1e-9);
            True("cog in signature", new BarItem { Group = 'B', Dia = 16, Shape = BarShape.Straight, A = 2000 }.Signature != sNoBend.Signature);
            True("cog count", sNoBend.CogCount == 2 && new BarItem { CogStart = 100 }.CogCount == 1);

            // hố chữ nhật mẫu: lưới bản cog hai đầu, lớp dưới móc lên tới lớp trên (giới hạn bởi chiều dày bản)
            var p = ManholeParams.SampleRectangular();
            var g = new ManholeGeometry(p);
            var rd = RebarDesigner.Design(g);
            var L = rd.Layout;
            var baseBotX = L.BaseLayers.First(l => l.Key == BarSets.X(BarSets.BaseBot));
            True("base bottom X cogs up", baseBotX.CogDir == 1 && baseBotX.CogStd == 200);
            // bản đáy 250, N16: cog tới lớp trên = 250 − 2·50 − 16 = 134 < 200
            Eq("base bottom cog limited by slab depth", 250 - 2 * 50 - 16, baseBotX.CogAt(baseBotX.Z), 1e-9);
            var baseTopX = L.BaseLayers.First(l => l.Key == BarSets.X(BarSets.BaseTop));
            True("base top X cogs down", baseTopX.CogDir == -1 && Math.Abs(baseTopX.CogAt(baseTopX.Z) - (250 - 2 * 50 - 16)) < 1e-9);
            var roofBotX = L.RoofLayers.First(l => l.Key == BarSets.X(BarSets.RoofBot));
            // bản nắp 200, N12: 200 − 100 − 12 = 88
            Eq("roof bottom cog", 88, roofBotX.CogAt(roofBotX.Z), 1e-9);
            var dp = roofBotX.DrawProfile(roofBotX.Positions[0], roofBotX.Run);
            True("draw profile has vertical cogs at both ends", dp.Count == 4 && Math.Abs(dp[0].X - dp[1].X) < 1e-9 && dp[0].Y > dp[1].Y
                && Math.Abs(dp[3].X - dp[2].X) < 1e-9 && dp[3].Y > dp[2].Y && Math.Abs(dp[0].Y - dp[1].Y - 88) < 1e-9);
            var b1 = rd.Schedule.Marks.First(mk => mk.Mark == "B1");
            True("B1 scheduled with 2 equal cogs", b1.CogStart > 0 && Math.Abs(b1.CogStart - b1.CogEnd) < 1e-9 && b1.BendDia == 80);
            True("schedule bars all carry bend dia", rd.Schedule.Marks.All(mk => mk.BendDia > 0));
            True("trimmers have no cogs", rd.Schedule.Marks.Where(mk => mk.Mark.StartsWith("T")).All(mk => mk.CogCount == 0));
            True("rings have no cogs", RebarDesigner.Design(new ManholeGeometry(ManholeParams.SampleCircularCast())).Schedule.Marks
                .Where(mk => mk.Shape == BarShape.Ring).All(mk => mk.CogCount == 0));
            Eq("vertical bar cog", 170, L.VertCog);
            Eq("vertical bend radius", 36, L.VertBendRadius);

            // mặt cắt: thép đứng là polyline có cung (bulge ≠ 0) với đỉnh cog ở cao độ VertZTop quay vào trong
            var sec = new SectionBuilder(g, rd, new DrawStyle(20)).Build();
            var verts = sec.Items.OfType<PPolyline>().Where(l => l.Layer == Layers.Rebar && l.Key == RebarKeys.WallVertical).ToList();
            True("vertical bars have bends drawn as arcs", verts.Count > 0 && verts.Any(l => l.Bulges.Any(bb => Math.Abs(bb) > 1e-9)));
            True("vertical bar top cog into roof", verts.Any(l => l.Points.Count(q => Math.Abs(q.Y - L.VertZTop) < 1e-6) >= 2));
            var mats = sec.Items.OfType<PPolyline>().Where(l => l.Layer == Layers.Rebar && l.Key == "BaseBottom").ToList();
            True("base bottom bar drawn with cogs and arcs", mats.Count > 0 && mats.All(l => l.Points.Count >= 6 && l.Bulges.Count(bb => Math.Abs(bb) > 1e-9) == 2));
            // mặt bằng: góc thanh U thép ngang là cung
            var plan = new PlanBuilder(g, rd, new DrawStyle(20)).Build();
            True("plan U-bar corners are arcs", plan.Items.OfType<PPolyline>().Count(l => l.Key == RebarKeys.WallHorizontal && l.Bulges.Any(bb => Math.Abs(bb) > 1e-9)) == 8);
            // bản nắp: vạch cog ở đầu thanh lớp dưới
            var roof = new RoofPlanBuilder(g, rd, new DrawStyle(20)).Build();
            True("roof plan cog ticks", roof.Items.OfType<PPolyline>().Count(l => l.Layer == Layers.RebarHidden && l.Points.Count == 2 && (l.Points[1] - l.Points[0]).Length < 60) > 10);
            var res = DetailComposer.Compose(p);
            True("cogs compose ok", res.Ok);
            var tbl = res.Model.Items.OfType<PTable>().First();
            True("schedule COG column text", tbl.Rows.Any(r => r[0] == "B1" && r[7].StartsWith("2x")) && tbl.Rows.Any(r => r[0].StartsWith("T") && r[7] == "-"));
            True("notes mention bend diameter and cogs", res.Model.Items.OfType<PText>().Any(tx => tx.Text.Contains("BEND DIAMETER 5d") && tx.Text.Contains("COG = STANDARD")));

            // tắt cog: thép đứng lại là chữ L, lưới bản thẳng, không ghi chú cog
            var off = p.Clone(); off.Cogs = false;
            var rdOff = RebarDesigner.Design(new ManholeGeometry(off));
            True("cogs off: W1 is L", rdOff.Schedule.Marks.First(mk => mk.Mark == "W1").Shape == BarShape.L);
            True("cogs off: no cogs scheduled", rdOff.Schedule.Marks.All(mk => mk.CogCount == 0));
            True("cogs off: no cog note", DetailComposer.Compose(off).Model.Items.OfType<PText>().All(tx => !tx.Text.Contains("COG = STANDARD")));
            // đổi hệ số uốn
            var k8 = p.Clone(); k8.BendFactor = 8;
            var rd8 = RebarDesigner.Design(new ManholeGeometry(k8));
            True("bend factor 8 -> larger cog and bend dia", rd8.Layout.VertCog > L.VertCog && rd8.Schedule.Marks.First(mk => mk.Mark == "W1").BendDia == 96);
            var bad = p.Clone(); bad.BendFactor = 2;
            True("bend factor < 3 -> error", !new ManholeGeometry(bad).IsValid);
            var partial = ParamsJson.Deserialize("{\"id\":\"X\",\"pipes\":[{\"mark\":\"A\",\"il\":10.0,\"role\":\"outlet\"}]}");
            True("json default cogs on, 5d", partial.Cogs && Math.Abs(partial.BendFactor - 5) < 1e-9);
            True("KeysForField cogs", RebarLayoutPreview.KeysForField("Cogs").Length >= 5 && RebarLayoutPreview.KeysForField("BendFactor").Length >= 5);
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
