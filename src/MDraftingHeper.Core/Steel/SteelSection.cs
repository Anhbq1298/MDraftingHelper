using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MDraftingHeper.Core.Geometry;
using MDraftingHeper.Core.Validation;

namespace MDraftingHeper.Core.Steel
{
    /// <summary>Family plus dimensions (mm) keyed by template parameter name.</summary>
    public sealed class SteelSection
    {
        public SteelFamily Family { get; }
        public IReadOnlyDictionary<string, double> Dimensions { get; }

        public SteelSection(SteelFamily family, IDictionary<string, double> dimensions)
        {
            Family = family;
            Dimensions = new Dictionary<string, double>(dimensions, StringComparer.Ordinal);
        }

        public double this[string key] => Dimensions.TryGetValue(key, out var v) ? v : double.NaN;

        public SteelSection With(string key, double value)
        {
            var d = new Dictionary<string, double>(Dimensions.ToDictionary(k => k.Key, k => k.Value), StringComparer.Ordinal) { [key] = value };
            return new SteelSection(Family, d);
        }

        public string DimensionText()
        {
            return string.Join(" ", SteelFamilies.Parameters(Family)
                .Select(p => p + "=" + this[p].ToString("0.###", CultureInfo.InvariantCulture)));
        }
    }

    public static class SteelValidator
    {
        /// <summary>Rejects non-positive, impossible and degenerate dimension combinations.</summary>
        public static ValidationResult Validate(SteelSection s)
        {
            var v = new ValidationResult();
            foreach (var p in SteelFamilies.Parameters(s.Family))
            {
                double x = s[p];
                if (double.IsNaN(x)) v.Error(SteelFamilies.Describe(p, s.Family) + " is missing.");
                else if (!(x > 0) || double.IsInfinity(x)) v.Error(SteelFamilies.Describe(p, s.Family) + " must be a positive number.");
            }
            if (!v.IsValid) return v;

            switch (s.Family)
            {
                case SteelFamily.CHS:
                    v.Require(2 * s["t"] < s["D"], "CHS requires 0 < 2t < D (positive internal void).");
                    break;

                case SteelFamily.SHS:
                case SteelFamily.RHS:
                {
                    double b = s["B"], h = s.Family == SteelFamily.SHS ? s["B"] : s["H"], t = s["t"], r = s["r"];
                    double minSide = Math.Min(b, h);
                    v.Require(2 * t < minSide, "The wall thickness leaves no internal void (requires 2t < " + (s.Family == SteelFamily.SHS ? "B" : "min(B, H)") + ").");
                    v.Require(r > t, "The outer corner radius must exceed t so the inner corner radius (r − t) is positive.");
                    v.Require(2 * r < minSide, "The corner radii overlap (requires 2r < the smaller side).");
                    break;
                }

                case SteelFamily.UB:
                case SteelFamily.UC:
                {
                    double h = s["H"], b = s["B"], tw = s["tw"], tf = s["tf"], r = s["r"];
                    v.Require(tw < b, "The web is wider than the flange (requires tw < B).");
                    v.Require(2 * tf < h, "The flanges overlap (requires 2tf < H).");
                    v.Require(tw / 2 + r < b / 2, "The root fillet does not fit within the flange outstand (requires tw/2 + r < B/2).");
                    v.Require(2 * tf + 2 * r < h, "The root fillets overlap along the web (requires 2tf + 2r < H).");
                    break;
                }
            }
            return v;
        }
    }

    /// <summary>
    /// Section outlines centred on the centroid (all supported sections are doubly symmetric),
    /// shared by the dialog preview and by geometry checks.
    /// </summary>
    public static class SectionGeometry
    {
        public static IReadOnlyList<Path2> Build(SteelSection s)
        {
            var v = SteelValidator.Validate(s);
            if (!v.IsValid) throw new ArgumentException(v.ToString(), nameof(s));
            switch (s.Family)
            {
                case SteelFamily.CHS:
                    return new[] { Circle(s["D"] / 2), Circle(s["D"] / 2 - s["t"]) };
                case SteelFamily.SHS:
                case SteelFamily.RHS:
                {
                    double b = s["B"], h = s.Family == SteelFamily.SHS ? s["B"] : s["H"], t = s["t"], r = s["r"];
                    return new[] { RoundedRect(b / 2, h / 2, r), RoundedRect(b / 2 - t, h / 2 - t, r - t) };
                }
                default:
                    return new[] { ISection(s["H"], s["B"], s["tw"], s["tf"], s["r"]) };
            }
        }

        /// <summary>Net steel area computed from the drafted geometry (mm²).</summary>
        public static double GeometricArea(SteelSection s)
        {
            var loops = Build(s);
            return loops[0].Area - loops.Skip(1).Sum(l => l.Area);
        }

        public static Path2 Circle(double r)
        {
            double b = Path2.BulgeFromSweep(Math.PI);
            return new Path2(new[] { new PathVertex(new Point2(r, 0), b), new PathVertex(new Point2(-r, 0), b) }, true);
        }

        public static Path2 RoundedRect(double hx, double hy, double r)
        {
            double cx = hx - r, cy = hy - r, q = Path2.BulgeFromSweep(Math.PI / 2);
            return new Path2(new[]
            {
                new PathVertex(new Point2(hx, -cy)), new PathVertex(new Point2(hx, cy), q),
                new PathVertex(new Point2(cx, hy)), new PathVertex(new Point2(-cx, hy), q),
                new PathVertex(new Point2(-hx, cy)), new PathVertex(new Point2(-hx, -cy), q),
                new PathVertex(new Point2(-cx, -hy)), new PathVertex(new Point2(cx, -hy), q),
            }, true);
        }

        public static Path2 ISection(double h, double b, double tw, double tf, double r)
        {
            double x1 = b / 2, y1 = h / 2, y2 = y1 - tf, xw = tw / 2, xf = xw + r, yf = y2 - r;
            double cw = Path2.BulgeFromSweep(-Math.PI / 2); // fillets turn clockwise on a CCW outline
            return new Path2(new[]
            {
                new PathVertex(new Point2(-x1, -y1)), new PathVertex(new Point2(x1, -y1)),
                new PathVertex(new Point2(x1, -y2)), new PathVertex(new Point2(xf, -y2), cw),
                new PathVertex(new Point2(xw, -yf)), new PathVertex(new Point2(xw, yf), cw),
                new PathVertex(new Point2(xf, y2)), new PathVertex(new Point2(x1, y2)),
                new PathVertex(new Point2(x1, y1)), new PathVertex(new Point2(-x1, y1)),
                new PathVertex(new Point2(-x1, y2)), new PathVertex(new Point2(-xf, y2), cw),
                new PathVertex(new Point2(-xw, yf)), new PathVertex(new Point2(-xw, -yf), cw),
                new PathVertex(new Point2(-xf, -y2)), new PathVertex(new Point2(-x1, -y2)),
            }, true);
        }
    }

    public enum InsertionAnchor { Centroid, TopLeft, BottomCentre }

    public static class AnchorMath
    {
        /// <summary>
        /// Offset from the anchor point to the block base point (the centroid), in section
        /// coordinates before rotation. The block base point stays at the centroid, so the anchor
        /// applies at insertion; later Properties edits keep the centroid fixed.
        /// </summary>
        public static Vector2 CentroidOffset(SteelSection s, InsertionAnchor anchor)
        {
            double w, h;
            if (s.Family == SteelFamily.CHS) { w = h = s["D"]; }
            else if (s.Family == SteelFamily.SHS) { w = h = s["B"]; }
            else { w = s["B"]; h = s["H"]; }
            switch (anchor)
            {
                case InsertionAnchor.TopLeft: return new Vector2(w / 2, -h / 2);
                case InsertionAnchor.BottomCentre: return new Vector2(0, h / 2);
                default: return new Vector2(0, 0);
            }
        }
    }
}
