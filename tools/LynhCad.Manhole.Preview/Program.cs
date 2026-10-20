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
                    case "snapshot":
                        string samples = Snapshots.FindSamplesDir();
                        if (samples == null) { Console.WriteLine("samples/ not found"); return 2; }
                        return Snapshots.Write(samples);
                    default:
                        Console.WriteLine("commands: check | svg <json> <svg> | sample <folder> | snapshot");
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
            Validation();
            Json();
            Naming();
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
            Eq("W1 count (66 EF)", 66, w1.Count);
            True("W1 is L", w1.Shape == BarShape.L);
            Eq("W1 leg", 300, w1.B);
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
            // vòng trong r = 750+50+12+6 = 818, vòng ngoài r = 950-68 = 882 → ceil(2πr/200) = 26 + 28
            Eq("circ cast vertical count", 26 + 28, vert.Count);
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
