using System;
using System.Collections.Generic;

namespace LynhCad.Manhole.Core.Geometry
{
    /// <summary>Điểm / vector 2D (đơn vị mm).</summary>
    public struct Vec2
    {
        public readonly double X;
        public readonly double Y;

        public Vec2(double x, double y) { X = x; Y = y; }

        public static readonly Vec2 Zero = new Vec2(0, 0);

        public static Vec2 operator +(Vec2 a, Vec2 b) { return new Vec2(a.X + b.X, a.Y + b.Y); }
        public static Vec2 operator -(Vec2 a, Vec2 b) { return new Vec2(a.X - b.X, a.Y - b.Y); }
        public static Vec2 operator *(Vec2 a, double k) { return new Vec2(a.X * k, a.Y * k); }
        public static Vec2 operator *(double k, Vec2 a) { return new Vec2(a.X * k, a.Y * k); }
        public static Vec2 operator -(Vec2 a) { return new Vec2(-a.X, -a.Y); }

        public double Length { get { return Math.Sqrt(X * X + Y * Y); } }
        public double Dot(Vec2 o) { return X * o.X + Y * o.Y; }
        public double Cross(Vec2 o) { return X * o.Y - Y * o.X; }

        public Vec2 Normalized()
        {
            double l = Length;
            return l < 1e-12 ? this : new Vec2(X / l, Y / l);
        }

        /// <summary>Quay 90° ngược chiều kim đồng hồ.</summary>
        public Vec2 Perp() { return new Vec2(-Y, X); }

        public static Vec2 FromAngle(double radians) { return new Vec2(Math.Cos(radians), Math.Sin(radians)); }

        public double Angle() { return Math.Atan2(Y, X); }

        public override string ToString() { return string.Format("({0:0.###}, {1:0.###})", X, Y); }
    }

    /// <summary>Khoảng số [Min, Max].</summary>
    public struct Interval
    {
        public readonly double Min;
        public readonly double Max;

        public Interval(double a, double b)
        {
            Min = Math.Min(a, b);
            Max = Math.Max(a, b);
        }

        public double Length { get { return Max - Min; } }
        public double Mid { get { return (Min + Max) / 2; } }
        public bool Contains(double v, double tol = 0) { return v >= Min - tol && v <= Max + tol; }
        public bool Overlaps(Interval o) { return o.Min < Max && o.Max > Min; }

        /// <summary>
        /// Trừ một loạt khoảng "lỗ" khỏi khoảng này, trả về các đoạn còn lại (đã sắp xếp).
        /// Dùng cho: tường bị ống xuyên, thanh thép bị lỗ mở cắt...
        /// </summary>
        public List<Interval> Subtract(IEnumerable<Interval> holes)
        {
            var result = new List<Interval> { this };
            foreach (var h in holes)
            {
                var next = new List<Interval>();
                foreach (var seg in result)
                {
                    if (!seg.Overlaps(h)) { next.Add(seg); continue; }
                    if (h.Min > seg.Min) next.Add(new Interval(seg.Min, h.Min));
                    if (h.Max < seg.Max) next.Add(new Interval(h.Max, seg.Max));
                }
                result = next;
            }
            result.RemoveAll(i => i.Length < 1e-6);
            result.Sort((a, b) => a.Min.CompareTo(b.Min));
            return result;
        }

        public override string ToString() { return string.Format("[{0:0.#}, {1:0.#}]", Min, Max); }
    }

    /// <summary>Hình chữ nhật song song trục.</summary>
    public struct Rect2
    {
        public readonly double X1, Y1, X2, Y2;

        public Rect2(double x1, double y1, double x2, double y2)
        {
            X1 = Math.Min(x1, x2); X2 = Math.Max(x1, x2);
            Y1 = Math.Min(y1, y2); Y2 = Math.Max(y1, y2);
        }

        public static Rect2 Centered(double cx, double cy, double w, double h)
        {
            return new Rect2(cx - w / 2, cy - h / 2, cx + w / 2, cy + h / 2);
        }

        public double Width { get { return X2 - X1; } }
        public double Height { get { return Y2 - Y1; } }
        public bool IsEmpty { get { return Width < 1e-6 || Height < 1e-6; } }

        public bool Contains(double x, double y)
        {
            return x > X1 && x < X2 && y > Y1 && y < Y2;
        }

        public List<Vec2> Corners()
        {
            return new List<Vec2> { new Vec2(X1, Y1), new Vec2(X2, Y1), new Vec2(X2, Y2), new Vec2(X1, Y2) };
        }
    }

    /// <summary>Bounding box có thể mở rộng dần.</summary>
    public class Bounds
    {
        public double MinX = double.MaxValue, MinY = double.MaxValue;
        public double MaxX = double.MinValue, MaxY = double.MinValue;

        public bool IsValid { get { return MinX <= MaxX && MinY <= MaxY; } }
        public double Width { get { return MaxX - MinX; } }
        public double Height { get { return MaxY - MinY; } }
        public double CenterX { get { return (MinX + MaxX) / 2; } }
        public double CenterY { get { return (MinY + MaxY) / 2; } }

        public void Add(double x, double y)
        {
            if (x < MinX) MinX = x;
            if (y < MinY) MinY = y;
            if (x > MaxX) MaxX = x;
            if (y > MaxY) MaxY = y;
        }

        public void Add(Vec2 p) { Add(p.X, p.Y); }

        public void Add(Bounds b)
        {
            if (!b.IsValid) return;
            Add(b.MinX, b.MinY);
            Add(b.MaxX, b.MaxY);
        }
    }
}
