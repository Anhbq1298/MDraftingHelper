using System;
using System.Collections.Generic;

namespace MDraftingHeper.Core.Geometry
{
    public static class GeometryMath
    {
        public const double Eps = 1e-9;

        public static double DistancePointToSegment(Point2 p, Point2 a, Point2 b)
        {
            var ab = b - a;
            double len2 = ab.Dot(ab);
            if (len2 < Eps * Eps) return p.DistanceTo(a);
            double t = Math.Max(0, Math.Min(1, (p - a).Dot(ab) / len2));
            return p.DistanceTo(a + ab * t);
        }

        /// <summary>Proper or touching intersection of two closed segments.</summary>
        public static bool SegmentsIntersect(Point2 p1, Point2 p2, Point2 q1, Point2 q2, double tol = Eps)
        {
            double d1 = Orient(q1, q2, p1), d2 = Orient(q1, q2, p2);
            double d3 = Orient(p1, p2, q1), d4 = Orient(p1, p2, q2);
            if (((d1 > tol && d2 < -tol) || (d1 < -tol && d2 > tol)) &&
                ((d3 > tol && d4 < -tol) || (d3 < -tol && d4 > tol)))
                return true;
            return DistancePointToSegment(p1, q1, q2) <= tol || DistancePointToSegment(p2, q1, q2) <= tol ||
                   DistancePointToSegment(q1, p1, p2) <= tol || DistancePointToSegment(q2, p1, p2) <= tol;
        }

        public static double SegmentToSegmentDistance(Point2 p1, Point2 p2, Point2 q1, Point2 q2)
        {
            if (SegmentsIntersect(p1, p2, q1, q2)) return 0;
            return Math.Min(Math.Min(DistancePointToSegment(p1, q1, q2), DistancePointToSegment(p2, q1, q2)),
                            Math.Min(DistancePointToSegment(q1, p1, p2), DistancePointToSegment(q2, p1, p2)));
        }

        private static double Orient(Point2 a, Point2 b, Point2 c) => (b - a).Cross(c - a);

        /// <summary>Even-odd point-in-polygon test on a sampled closed outline.</summary>
        public static bool PointInPolygon(Point2 p, IList<Point2> poly)
        {
            bool inside = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                var a = poly[i]; var b = poly[j];
                if ((a.Y > p.Y) != (b.Y > p.Y) &&
                    p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X)
                    inside = !inside;
            }
            return inside;
        }

        public static double DistanceToPolygonEdge(Point2 p, IList<Point2> poly)
        {
            double best = double.MaxValue;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
                best = Math.Min(best, DistancePointToSegment(p, poly[j], poly[i]));
            return best;
        }

        /// <summary>True when any two non-adjacent edges of a closed sampled outline intersect.</summary>
        public static bool PolygonSelfIntersects(IList<Point2> poly, double tol = 1e-7)
        {
            int n = poly.Count;
            for (int i = 0; i < n; i++)
            {
                var a1 = poly[i]; var a2 = poly[(i + 1) % n];
                for (int j = i + 1; j < n; j++)
                {
                    if (j == i || (j + 1) % n == i || (i + 1) % n == j) continue;
                    var b1 = poly[j]; var b2 = poly[(j + 1) % n];
                    if (SegmentsIntersect(a1, a2, b1, b2, tol)) return true;
                }
            }
            return false;
        }

        public static double NormalizeAngle(double a)
        {
            while (a <= -Math.PI) a += 2 * Math.PI;
            while (a > Math.PI) a -= 2 * Math.PI;
            return a;
        }

        public static double ToRadians(double degrees) => degrees * Math.PI / 180;
    }
}
