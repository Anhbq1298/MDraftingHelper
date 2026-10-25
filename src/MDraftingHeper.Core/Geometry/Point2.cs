using System;
using System.Globalization;

namespace MDraftingHeper.Core.Geometry
{
    /// <summary>A point in a 2D drafting plane (millimetres). Independent of any CAD type.</summary>
    public struct Point2 : IEquatable<Point2>
    {
        public readonly double X;
        public readonly double Y;

        public Point2(double x, double y) { X = x; Y = y; }

        public static readonly Point2 Origin = new Point2(0, 0);

        public static Vector2 operator -(Point2 a, Point2 b) => new Vector2(a.X - b.X, a.Y - b.Y);
        public static Point2 operator +(Point2 p, Vector2 v) => new Point2(p.X + v.X, p.Y + v.Y);
        public static Point2 operator -(Point2 p, Vector2 v) => new Point2(p.X - v.X, p.Y - v.Y);

        public double DistanceTo(Point2 other) => (other - this).Length;

        public static Point2 Mid(Point2 a, Point2 b) => new Point2((a.X + b.X) / 2, (a.Y + b.Y) / 2);

        public bool IsEqualTo(Point2 other, double tol) => DistanceTo(other) <= tol;

        public bool Equals(Point2 other) => X.Equals(other.X) && Y.Equals(other.Y);
        public override bool Equals(object obj) => obj is Point2 p && Equals(p);
        public override int GetHashCode() => (X.GetHashCode() * 397) ^ Y.GetHashCode();

        public override string ToString() =>
            string.Format(CultureInfo.InvariantCulture, "({0:0.###}, {1:0.###})", X, Y);
    }

    public struct Vector2
    {
        public readonly double X;
        public readonly double Y;

        public Vector2(double x, double y) { X = x; Y = y; }

        public double Length => Math.Sqrt(X * X + Y * Y);
        public double Angle => Math.Atan2(Y, X);

        public Vector2 Normalized()
        {
            double l = Length;
            if (l < 1e-12) throw new InvalidOperationException("Cannot normalise a zero-length vector.");
            return new Vector2(X / l, Y / l);
        }

        /// <summary>Rotates the vector counter-clockwise by 90 degrees.</summary>
        public Vector2 PerpLeft() => new Vector2(-Y, X);

        /// <summary>Rotates the vector counter-clockwise by <paramref name="radians"/>.</summary>
        public Vector2 Rotate(double radians)
        {
            double c = Math.Cos(radians), s = Math.Sin(radians);
            return new Vector2(X * c - Y * s, X * s + Y * c);
        }

        public double Dot(Vector2 o) => X * o.X + Y * o.Y;
        public double Cross(Vector2 o) => X * o.Y - Y * o.X;

        public static Vector2 FromAngle(double radians) => new Vector2(Math.Cos(radians), Math.Sin(radians));

        public static Vector2 operator *(Vector2 v, double k) => new Vector2(v.X * k, v.Y * k);
        public static Vector2 operator *(double k, Vector2 v) => new Vector2(v.X * k, v.Y * k);
        public static Vector2 operator +(Vector2 a, Vector2 b) => new Vector2(a.X + b.X, a.Y + b.Y);
        public static Vector2 operator -(Vector2 a, Vector2 b) => new Vector2(a.X - b.X, a.Y - b.Y);
        public static Vector2 operator -(Vector2 v) => new Vector2(-v.X, -v.Y);

        public override string ToString() =>
            string.Format(CultureInfo.InvariantCulture, "<{0:0.####}, {1:0.####}>", X, Y);
    }
}
