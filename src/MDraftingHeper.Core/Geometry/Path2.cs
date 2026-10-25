using System;
using System.Collections.Generic;
using System.Linq;

namespace MDraftingHeper.Core.Geometry
{
    /// <summary>
    /// A vertex of a bulged 2D path. The bulge describes the segment that starts at this
    /// vertex, using the AutoCAD convention: bulge = tan(sweep / 4), positive = counter-clockwise.
    /// </summary>
    public struct PathVertex
    {
        public readonly Point2 Point;
        public readonly double Bulge;

        public PathVertex(Point2 point, double bulge = 0) { Point = point; Bulge = bulge; }
    }

    /// <summary>One segment of a path: a straight line or a circular arc.</summary>
    public sealed class PathSegment
    {
        public Point2 Start { get; }
        public Point2 End { get; }
        public double Bulge { get; }

        public PathSegment(Point2 start, Point2 end, double bulge)
        {
            Start = start; End = end; Bulge = bulge;
        }

        public bool IsArc => Math.Abs(Bulge) > 1e-12;

        /// <summary>Signed sweep angle in radians (positive = CCW).</summary>
        public double Sweep => 4 * Math.Atan(Bulge);

        public double ChordLength => Start.DistanceTo(End);

        public double Radius
        {
            get
            {
                if (!IsArc) return double.PositiveInfinity;
                return Math.Abs(ChordLength / (2 * Math.Sin(Sweep / 2)));
            }
        }

        public Point2 Center
        {
            get
            {
                if (!IsArc) throw new InvalidOperationException("A straight segment has no centre.");
                var d = End - Start;
                double offset = (1 - Bulge * Bulge) / (2 * Bulge);
                return Point2.Mid(Start, End) + d.PerpLeft() * (offset / 2);
            }
        }

        public double Length => IsArc ? Radius * Math.Abs(Sweep) : ChordLength;

        /// <summary>Unit tangent at the start, in the direction of travel.</summary>
        public Vector2 StartTangent
        {
            get
            {
                if (!IsArc) return (End - Start).Normalized();
                var r = (Start - Center).Normalized();
                return Sweep > 0 ? r.PerpLeft() : -r.PerpLeft();
            }
        }

        /// <summary>Unit tangent at the end, in the direction of travel.</summary>
        public Vector2 EndTangent
        {
            get
            {
                if (!IsArc) return (End - Start).Normalized();
                var r = (End - Center).Normalized();
                return Sweep > 0 ? r.PerpLeft() : -r.PerpLeft();
            }
        }

        /// <summary>Signed area between the chord and the arc (zero for a line).</summary>
        public double SegmentArea
        {
            get
            {
                if (!IsArc) return 0;
                double t = Math.Abs(Sweep), r = Radius;
                return Math.Sign(Bulge) * r * r / 2 * (t - Math.Sin(t));
            }
        }

        /// <summary>Exact shortest distance from a point to this line or arc segment.</summary>
        public double DistanceTo(Point2 p)
        {
            if (!IsArc) return GeometryMath.DistancePointToSegment(p, Start, End);
            var c = Center;
            var cp = p - c;
            double endpoints = Math.Min(p.DistanceTo(Start), p.DistanceTo(End));
            if (cp.Length < 1e-12) return Radius;
            // Is the direction to p within the swept angular range?
            double a0 = (Start - c).Angle, rel = GeometryMath.NormalizeAngle(cp.Angle - a0);
            double sweep = Sweep;
            bool within = sweep > 0
                ? (rel >= 0 ? rel : rel + 2 * Math.PI) <= sweep + 1e-12
                : (rel <= 0 ? -rel : 2 * Math.PI - rel) <= -sweep + 1e-12;
            return within ? Math.Min(Math.Abs(cp.Length - Radius), endpoints) : endpoints;
        }

        /// <summary>Points along the segment, including both ends.</summary>
        public IList<Point2> Sample(double maxChordAngleRad = Math.PI / 90)
        {
            if (!IsArc) return new[] { Start, End };
            var c = Center;
            double a0 = (Start - c).Angle, sweep = Sweep, r = Radius;
            int n = Math.Max(2, (int)Math.Ceiling(Math.Abs(sweep) / maxChordAngleRad));
            var pts = new List<Point2>(n + 1) { Start };
            for (int i = 1; i < n; i++)
                pts.Add(c + Vector2.FromAngle(a0 + sweep * i / n) * r);
            pts.Add(End);
            return pts;
        }
    }

    /// <summary>A 2D lightweight-polyline-like path with genuine arc bulges.</summary>
    public sealed class Path2
    {
        public IReadOnlyList<PathVertex> Vertices { get; }
        public bool IsClosed { get; }

        public Path2(IEnumerable<PathVertex> vertices, bool closed)
        {
            Vertices = vertices.ToList().AsReadOnly();
            IsClosed = closed;
        }

        public IEnumerable<PathSegment> Segments
        {
            get
            {
                int n = Vertices.Count;
                int count = IsClosed ? n : n - 1;
                for (int i = 0; i < count; i++)
                {
                    var a = Vertices[i];
                    var b = Vertices[(i + 1) % n];
                    yield return new PathSegment(a.Point, b.Point, a.Bulge);
                }
            }
        }

        /// <summary>Signed enclosed area including arc segments (positive = CCW). Closed paths only.</summary>
        public double SignedArea
        {
            get
            {
                if (!IsClosed) throw new InvalidOperationException("Area requires a closed path.");
                double a = 0;
                foreach (var s in Segments)
                {
                    a += (s.Start.X * s.End.Y - s.End.X * s.Start.Y) / 2;
                    a += s.SegmentArea;
                }
                return a;
            }
        }

        public double Area => Math.Abs(SignedArea);

        public double Length => Segments.Sum(s => s.Length);

        /// <summary>Polygonal approximation (closing vertex not repeated for closed paths).</summary>
        public List<Point2> Sample(double maxChordAngleRad = Math.PI / 90)
        {
            var pts = new List<Point2>();
            foreach (var s in Segments)
            {
                var sp = s.Sample(maxChordAngleRad);
                if (pts.Count == 0) pts.Add(sp[0]);
                for (int i = 1; i < sp.Count; i++) pts.Add(sp[i]);
            }
            if (IsClosed && pts.Count > 1 && pts[0].IsEqualTo(pts[pts.Count - 1], 1e-9))
                pts.RemoveAt(pts.Count - 1);
            return pts;
        }

        public Path2 Transform(Func<Point2, Point2> map, bool mirrored = false)
        {
            var v = Vertices.Select(x => new PathVertex(map(x.Point), mirrored ? -x.Bulge : x.Bulge));
            return new Path2(v, IsClosed);
        }

        public static double BulgeFromSweep(double sweepRad) => Math.Tan(sweepRad / 4);
    }
}
