using System;
using System.Collections.Generic;
using System.Globalization;
using MDraftingHeper.Core.Geometry;
using MDraftingHeper.Core.Validation;

namespace MDraftingHeper.Core.Hooks
{
    public sealed class TieInput
    {
        public Point2 CenterA { get; set; }
        public Point2 CenterB { get; set; }
        public double DiameterA { get; set; }
        public double DiameterB { get; set; }

        public double TieDiameter { get; set; } = 10;
        public HookAngle Angle { get; set; } = HookAngle.Deg135;
        /// <summary>Optional per-end angles. When omitted, <see cref="Angle"/> is used.</summary>
        public HookAngle? AngleA { get; set; }
        public HookAngle? AngleB { get; set; }
        public HookEnds Ends { get; set; } = HookEnds.Both;

        /// <summary>False: the tie runs on the left of A→B. True: on the right.</summary>
        public bool Flip { get; set; }

        /// <summary>Clear gap between the longitudinal bar surface and the tie surface (mm).</summary>
        public double ClearGap { get; set; }

        /// <summary>Inside mandrel (bend) diameter of the tie (mm).</summary>
        public double InsideMandrelDiameter { get; set; } = 40;

        /// <summary>Straight tail length after each hook (mm).</summary>
        public double TailLength { get; set; } = 100;

        /// <summary>Optional per-end straight tail lengths. When omitted, <see cref="TailLength"/> is used.</summary>
        public double? TailLengthA { get; set; }
        public double? TailLengthB { get; set; }
    }

    public sealed class TieResult
    {
        public ValidationResult Validation { get; internal set; }

        /// <summary>Tie centreline as an open path with genuine arc bulges; null when invalid.</summary>
        public Path2 Path { get; internal set; }

        /// <summary>max(DA, DB): used for the hook geometry at both ends.</summary>
        public double ReferenceDiameter { get; internal set; }
        public double FitRadius { get; internal set; }
        public double MandrelRadius { get; internal set; }

        /// <summary>Centreline bend radius actually used: max(Rfit, Rmandrel).</summary>
        public double CentrelineRadius { get; internal set; }

        /// <summary>Clear distance between bar A surface and the tie surface at the hook (mm).</summary>
        public double ClearanceA { get; internal set; }
        public double ClearanceB { get; internal set; }

        /// <summary>Unit tail directions (end of each hook), when that end has a hook.</summary>
        public Vector2? TailDirectionA { get; internal set; }
        public Vector2? TailDirectionB { get; internal set; }
    }

    /// <summary>
    /// Builds a tie between two longitudinal bars. Both hooks use Dref = max(DA, DB) while each
    /// hook stays concentric with its own bar centre. The straight tie runs tangent to both bend
    /// circles, offset from the A→B reference axis by the centreline radius.
    ///
    /// Frame: u = unit(CB − CA); n = u rotated +90° (or −90° when flipped) points to the tie side.
    /// At end B the path travels +u at CB + R·n and wraps around CB away from the tie side by the
    /// hook angle; end A is the mirror image. A 180° hook therefore returns as a semicircle around
    /// the bar with its tail running back parallel to the tie.
    /// </summary>
    public static class TieGeometryBuilder
    {
        public static TieResult Build(TieInput input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            var v = new ValidationResult();
            var result = new TieResult { Validation = v };

            if (!(input.DiameterA > 0) || !(input.DiameterB > 0)) v.Error("Both longitudinal bar diameters must be positive.");
            if (!(input.TieDiameter > 0)) v.Error("Tie diameter must be positive.");
            if (input.ClearGap < 0) v.Error("Clear gap cannot be negative.");
            if (!(input.InsideMandrelDiameter > 0)) v.Error("Inside mandrel diameter must be positive.");
            double tailA = input.TailLengthA ?? input.TailLength;
            double tailB = input.TailLengthB ?? input.TailLength;
            if (!(tailA >= 0) || !(tailB >= 0)) v.Error("Tail length cannot be negative.");
            var axis = input.CenterB - input.CenterA;
            if (axis.Length < 1e-6) v.Error("The two bars have coincident centres.");
            if (!v.IsValid) return result;

            double dref = Math.Max(input.DiameterA, input.DiameterB);
            double rFit = dref / 2 + input.ClearGap + input.TieDiameter / 2;
            double rMandrel = input.InsideMandrelDiameter / 2 + input.TieDiameter / 2;
            double rc = Math.Max(rFit, rMandrel);
            result.ReferenceDiameter = dref;
            result.FitRadius = rFit;
            result.MandrelRadius = rMandrel;
            result.CentrelineRadius = rc;
            result.ClearanceA = rc - input.TieDiameter / 2 - input.DiameterA / 2;
            result.ClearanceB = rc - input.TieDiameter / 2 - input.DiameterB / 2;

            // The bends wrap the far side of each bar, so they never face each other; spacing
            // problems show up as overlapping bars or as tail clashes checked on the final path.
            double spacing = axis.Length;
            if (spacing < (input.DiameterA + input.DiameterB) / 2)
                v.Error(string.Format(CultureInfo.InvariantCulture,
                    "The selected bars overlap (centre spacing {0:0.#} mm).", spacing));
            if (!v.IsValid) return result;

            var u = axis.Normalized();
            var n = input.Flip ? -u.PerpLeft() : u.PerpLeft();
            // Sense of rotation around B when travelling +u on the n side: clockwise for the
            // left side (n = +perp), counter-clockwise when flipped.
            double s = input.Flip ? 1 : -1;
            double thetaA = GeometryMath.ToRadians((int)(input.AngleA ?? input.Angle));
            double thetaB = GeometryMath.ToRadians((int)(input.AngleB ?? input.Angle));
            bool hookA = input.Ends != HookEnds.EndB;
            bool hookB = input.Ends != HookEnds.EndA;

            var vertices = new List<PathVertex>();
            var ca = input.CenterA;
            var cb = input.CenterB;
            var ta = ca + n * rc;
            var tb = cb + n * rc;
            double bulgeA = Path2.BulgeFromSweep(s * thetaA);
            double bulgeB = Path2.BulgeFromSweep(s * thetaB);

            if (hookA)
            {
                // Hook A traversed away from the tie rotates opposite to hook B; the path is
                // written from the tail end, so the arc runs in the same sense as at B.
                var rimA = ca + n.Rotate(-s * thetaA) * rc;
                var tailDirA = (-u).Rotate(-s * thetaA);
                var tailEndA = rimA + tailDirA * tailA;
                if (tailA > 1e-9) vertices.Add(new PathVertex(tailEndA));
                vertices.Add(new PathVertex(rimA, bulgeA));
                result.TailDirectionA = tailDirA;
            }
            vertices.Add(new PathVertex(ta));

            if (hookB)
            {
                vertices.Add(new PathVertex(tb, bulgeB));
                var rimB = cb + n.Rotate(s * thetaB) * rc;
                var tailDirB = u.Rotate(s * thetaB);
                vertices.Add(new PathVertex(rimB));
                if (tailB > 1e-9) vertices.Add(new PathVertex(rimB + tailDirB * tailB));
                result.TailDirectionB = tailDirB;
            }
            else
            {
                vertices.Add(new PathVertex(tb));
            }

            var path = new Path2(vertices, false);
            CheckClashes(path, input.TieDiameter, v);
            CheckBarsClear(path, input, v);
            if (!v.IsValid) return result;

            result.Path = path;
            return result;
        }

        /// <summary>Rejects tails or hooks that cross or touch other parts of the same tie.</summary>
        private static void CheckClashes(Path2 path, double tieDiameter, ValidationResult v)
        {
            var pts = path.Sample(Math.PI / 72);
            var along = new double[pts.Count];
            for (int i = 1; i < pts.Count; i++) along[i] = along[i - 1] + pts[i].DistanceTo(pts[i - 1]);
            double minSeparation = 3 * tieDiameter;
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                for (int j = i + 2; j + 1 < pts.Count; j++)
                {
                    if (along[j] - along[i + 1] < minSeparation) continue;
                    double d = GeometryMath.SegmentToSegmentDistance(pts[i], pts[i + 1], pts[j], pts[j + 1]);
                    if (d < tieDiameter - 1e-6)
                    {
                        v.Error("The hooks or tails overlap other parts of the tie. Reduce the tail length, change the hook angle or select bars further apart.");
                        return;
                    }
                }
            }
        }

        /// <summary>Rejects geometry where the tie centreline passes through a longitudinal bar.</summary>
        private static void CheckBarsClear(Path2 path, TieInput input, ValidationResult v)
        {
            var bars = new[] { (input.CenterA, input.DiameterA, "A"), (input.CenterB, input.DiameterB, "B") };
            foreach (var (c, d, name) in bars)
            {
                double needed = d / 2 + input.TieDiameter / 2 - 1e-6;
                foreach (var seg in path.Segments)
                {
                    if (seg.DistanceTo(c) < needed)
                    {
                        v.Error("The tie or a tail passes through bar " + name + ". Reduce the tail length or change the hook angle.");
                        return;
                    }
                }
            }
        }
    }
}
