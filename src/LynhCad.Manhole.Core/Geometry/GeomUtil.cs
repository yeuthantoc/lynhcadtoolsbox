using System;
using System.Collections.Generic;
using System.Linq;

namespace LynhCad.Manhole.Core.Geometry
{
    public static class GeomUtil
    {
        public const double Deg = Math.PI / 180.0;

        /// <summary>
        /// Rải thanh đều trong [start, end] với khoảng cách KHÔNG vượt quá spacing.
        /// Có thanh ở cả hai đầu. n = ceil(L / s) + 1.
        /// </summary>
        public static List<double> Distribute(double start, double end, double spacing)
        {
            var list = new List<double>();
            double len = end - start;
            if (len < -1e-9) return list;
            if (len < 1e-6 || spacing <= 0) { list.Add(start); return list; }
            int n = (int)Math.Ceiling(len / spacing - 1e-9) + 1;
            double step = len / (n - 1);
            for (int i = 0; i < n; i++) list.Add(start + i * step);
            return list;
        }

        /// <summary>Số thanh khi rải theo Distribute.</summary>
        public static int CountBars(double length, double spacing)
        {
            if (length < 1e-6) return 1;
            return (int)Math.Ceiling(length / spacing - 1e-9) + 1;
        }

        /// <summary>Làm tròn lên bội số step (mm).</summary>
        public static double RoundUp(double v, double step)
        {
            return Math.Ceiling(v / step - 1e-9) * step;
        }

        /// <summary>Chuẩn hoá góc về [0, 2π).</summary>
        public static double NormAngle(double a)
        {
            double t = a % (2 * Math.PI);
            if (t < 0) t += 2 * Math.PI;
            return t;
        }

        /// <summary>
        /// Giao của đường thẳng (origin + s·dir) với hình chữ nhật [-hx,hx]×[-hy,hy].
        /// Trả về khoảng tham số s. Dùng để biết mặt cắt đi qua bản đáy/bản nắp dài bao nhiêu.
        /// </summary>
        public static Interval? LineRectClip(Vec2 origin, Vec2 dir, double hx, double hy)
        {
            double t0 = double.MinValue, t1 = double.MaxValue;
            if (!Slab(origin.X, dir.X, -hx, hx, ref t0, ref t1)) return null;
            if (!Slab(origin.Y, dir.Y, -hy, hy, ref t0, ref t1)) return null;
            if (t1 - t0 < 1e-9) return null;
            return new Interval(t0, t1);
        }

        /// <summary>Đoạn thẳng a–b có cắt qua (hoặc nằm trong) hình chữ nhật r không.</summary>
        public static bool SegmentIntersectsRect(Vec2 a, Vec2 b, Rect2 r)
        {
            var d = b - a;
            double t0 = 0, t1 = 1;
            if (!Slab(a.X, d.X, r.X1, r.X2, ref t0, ref t1)) return false;
            if (!Slab(a.Y, d.Y, r.Y1, r.Y2, ref t0, ref t1)) return false;
            return t0 <= t1;
        }

        private static bool Slab(double o, double d, double lo, double hi, ref double t0, ref double t1)
        {
            if (Math.Abs(d) < 1e-12) return o >= lo && o <= hi;
            double a = (lo - o) / d, b = (hi - o) / d;
            if (a > b) { var t = a; a = b; b = t; }
            t0 = Math.Max(t0, a);
            t1 = Math.Min(t1, b);
            return t0 <= t1;
        }

        /// <summary>Nửa dây cung của đường tròn bán kính r tại khoảng cách δ từ tâm (0 nếu không cắt).</summary>
        public static double HalfChord(double r, double delta)
        {
            double v = r * r - delta * delta;
            return v <= 0 ? 0 : Math.Sqrt(v);
        }

        /// <summary>Bulge của cung tròn quét góc sweep (radian, dương = CCW).</summary>
        public static double Bulge(double sweep) { return Math.Tan(sweep / 4.0); }

        /// <summary>
        /// Union của các hình chữ nhật song song trục → các cạnh biên (đã gộp cạnh thẳng hàng).
        /// Dùng để vẽ đường bao bê tông liền khối (đáy + tường + nắp) mà không lộ mạch nối giữa các mảng hatch.
        /// </summary>
        public static List<Tuple<Vec2, Vec2>> UnionOutline(IList<Rect2> rects)
        {
            var result = new List<Tuple<Vec2, Vec2>>();
            var rs = rects.Where(r => !r.IsEmpty).ToList();
            if (rs.Count == 0) return result;

            var xs = rs.SelectMany(r => new[] { r.X1, r.X2 }).Distinct().OrderBy(v => v).ToList();
            var ys = rs.SelectMany(r => new[] { r.Y1, r.Y2 }).Distinct().OrderBy(v => v).ToList();
            xs = Dedup(xs); ys = Dedup(ys);
            int nx = xs.Count - 1, ny = ys.Count - 1;
            var filled = new bool[nx, ny];
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < ny; j++)
                {
                    double cx = (xs[i] + xs[i + 1]) / 2, cy = (ys[j] + ys[j + 1]) / 2;
                    filled[i, j] = rs.Any(r => r.Contains(cx, cy));
                }

            Func<int, int, bool> F = (i, j) => i >= 0 && j >= 0 && i < nx && j < ny && filled[i, j];

            // cạnh ngang tại ys[j]
            for (int j = 0; j <= ny; j++)
            {
                double? start = null;
                for (int i = 0; i <= nx; i++)
                {
                    bool edge = i < nx && (F(i, j - 1) != F(i, j));
                    if (edge && start == null) start = xs[i];
                    if (!edge && start != null)
                    {
                        result.Add(Tuple.Create(new Vec2(start.Value, ys[j]), new Vec2(xs[i], ys[j])));
                        start = null;
                    }
                }
            }
            // cạnh đứng tại xs[i]
            for (int i = 0; i <= nx; i++)
            {
                double? start = null;
                for (int j = 0; j <= ny; j++)
                {
                    bool edge = j < ny && (F(i - 1, j) != F(i, j));
                    if (edge && start == null) start = ys[j];
                    if (!edge && start != null)
                    {
                        result.Add(Tuple.Create(new Vec2(xs[i], start.Value), new Vec2(xs[i], ys[j])));
                        start = null;
                    }
                }
            }
            return result;
        }

        private static List<double> Dedup(List<double> sorted)
        {
            var o = new List<double>();
            foreach (var v in sorted)
                if (o.Count == 0 || v - o[o.Count - 1] > 1e-6) o.Add(v);
            return o;
        }
    }
}
