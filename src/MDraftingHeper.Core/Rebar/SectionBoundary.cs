using System;
using System.Collections.Generic;
using System.Linq;
using MDraftingHeper.Core.Geometry;
using MDraftingHeper.Core.Validation;

namespace MDraftingHeper.Core.Rebar
{
    /// <summary>A validated closed boundary representing the gross concrete section.</summary>
    public sealed class SectionBoundary
    {
        public Path2 Path { get; }
        public double Area { get; }
        private readonly List<Point2> _polygon;

        private SectionBoundary(Path2 path, List<Point2> polygon)
        {
            Path = path;
            Area = path.Area;
            _polygon = polygon;
        }

        /// <summary>
        /// Validates closure, vertex count, positive area and absence of self-intersection.
        /// Arc segments are handled exactly for the area and by fine sampling for intersection
        /// and containment checks.
        /// </summary>
        public static ValidationResult TryCreate(Path2 path, out SectionBoundary boundary)
        {
            boundary = null;
            var v = new ValidationResult();
            if (path == null) return v.Error("No boundary was supplied.");
            if (!path.IsClosed) return v.Error("The boundary polyline is not closed.");
            if (path.Vertices.Count < 2 || (path.Vertices.Count < 3 && path.Vertices.All(x => Math.Abs(x.Bulge) < 1e-12)))
                return v.Error("The boundary needs at least three vertices (or two with arc segments).");

            foreach (var s in path.Segments)
                if (s.ChordLength < 1e-9) return v.Error("The boundary has a zero-length segment (duplicate vertex).");

            var poly = path.Sample(Math.PI / 180);
            if (GeometryMath.PolygonSelfIntersects(poly)) return v.Error("The boundary intersects itself.");
            if (!(path.Area > 1e-6)) return v.Error("The boundary encloses no area.");

            boundary = new SectionBoundary(path, poly);
            return v;
        }

        public BarPlacement Classify(RebarBar bar, double tolerance = 1e-6)
        {
            bool centreInside = GeometryMath.PointInPolygon(bar.Center, _polygon);
            double d = GeometryMath.DistanceToPolygonEdge(bar.Center, _polygon);
            double r = bar.Diameter / 2;
            if (centreInside && d >= r - tolerance) return BarPlacement.Inside;
            if (!centreInside && d >= r - tolerance) return BarPlacement.Outside;
            return BarPlacement.Crossing;
        }
    }
}
