using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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

        /// <summary>Largest longitudinal bar diameter: governs every bend.</summary>
        public double ReferenceDiameter { get; internal set; }

        /// <summary>Distance from a bar centre to the straight tie centreline: Dref/2 + gap + tie/2.</summary>
        public double FitRadius { get; internal set; }
        public double MandrelRadius { get; internal set; }

        /// <summary>Centreline bend radius actually used: max(fit, mandrel/2 + tie/2).</summary>
        public double CentrelineRadius { get; internal set; }

        /// <summary>Clear distance between a bar surface and the tie surface along the straight tie (mm).</summary>
        public double ClearanceA { get; internal set; }
        public double ClearanceB { get; internal set; }

        /// <summary>Unit tail directions (end of each hook), when that end has a hook.</summary>
        public Vector2? TailDirectionA { get; internal set; }
        public Vector2? TailDirectionB { get; internal set; }

        /// <summary>Closed hoop only: bar indices on the hoop in path order (closure bar first).</summary>
        public IReadOnlyList<int> HoopBars { get; internal set; }

        /// <summary>Closed hoop only: selected bars inside the hoop that it does not wrap.</summary>
        public int InteriorBars { get; internal set; }
    }

    /// <summary>
    /// Builds tie centrelines for 2D drafting. Bar centres never move. Every bend is sized from
    /// Dref = the largest selected bar diameter. Straight legs run at Dref/2 + gap + tie/2 from the bar
    /// centres, so the tie touches the largest bar. When the mandrel radius is larger, the bend centre
    /// moves away from the bar instead of pushing the tie off it.
    ///
    /// Frame: u = unit(CB − CA); n = u rotated +90° (or −90° when flipped) points to the tie side.
    /// At end B the path travels +u along the leg and wraps around the bar away from the tie side by
    /// the hook angle; end A is the mirror image. A 180° hook therefore returns as a semicircle with its
    /// tail running back parallel to the tie.
    /// </summary>
    public static class TieGeometryBuilder
    {
        public static TieResult Build(TieInput input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            var v = new ValidationResult();
            var result = new TieResult { Validation = v };

            if (!(input.DiameterA > 0) || !(input.DiameterB > 0)) v.Error("Both longitudinal bar diameters must be positive.");
            CheckCommon(input.TieDiameter, input.ClearGap, input.InsideMandrelDiameter, v);
            double tailA = input.TailLengthA ?? input.TailLength;
            double tailB = input.TailLengthB ?? input.TailLength;
            if (!(tailA >= 0) || !(tailB >= 0)) v.Error("Tail length cannot be negative.");
            var axis = input.CenterB - input.CenterA;
            if (axis.Length < 1e-6) v.Error("The two bars have coincident centres.");
            if (!v.IsValid) return result;

            double dref = Math.Max(input.DiameterA, input.DiameterB);
            Size(result, dref, input.TieDiameter, input.ClearGap, input.InsideMandrelDiameter);
            double d = result.FitRadius, rc = result.CentrelineRadius;
            result.ClearanceA = d - input.TieDiameter / 2 - input.DiameterA / 2;
            result.ClearanceB = d - input.TieDiameter / 2 - input.DiameterB / 2;

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

            // Bend centres: on the bar centre when the fit governs, otherwise moved away from the leg so
            // the bar still touches the tie where the leg meets the bend.
            var ca = input.CenterA - n * (rc - d);
            var cb = input.CenterB - n * (rc - d);
            var vertices = new List<PathVertex>();

            if (hookA)
            {
                var hook = Hook(ca, n, -u, rc, -s * thetaA, tailA);
                if (tailA > 1e-9) vertices.Add(new PathVertex(hook.TailEnd));
                vertices.Add(new PathVertex(hook.Rim, Path2.BulgeFromSweep(s * thetaA)));
                result.TailDirectionA = hook.TailDirection;
            }
            vertices.Add(new PathVertex(input.CenterA + n * d));

            var tb = input.CenterB + n * d;
            if (hookB)
            {
                vertices.Add(new PathVertex(tb, Path2.BulgeFromSweep(s * thetaB)));
                var hook = Hook(cb, n, u, rc, s * thetaB, tailB);
                vertices.Add(new PathVertex(hook.Rim));
                if (tailB > 1e-9) vertices.Add(new PathVertex(hook.TailEnd));
                result.TailDirectionB = hook.TailDirection;
            }
            else
            {
                vertices.Add(new PathVertex(tb));
            }

            var path = new Path2(vertices, false);
            CheckClashes(path, input.TieDiameter, v);
            CheckBarsClear(path, new[] { (input.CenterA, input.DiameterA, "A"), (input.CenterB, input.DiameterB, "B") }, input.TieDiameter, v);
            if (!v.IsValid) return result;

            result.Path = path;
            return result;
        }

        /// <summary>
        /// Closed hoop around the outer bars of the selection (their convex hull). Corners are bends of
        /// the same radius; both hooks sit on the closure bar, each measured from its own leg, so a 135°
        /// closure gives two parallel tails into the core. Output is one open polyline whose two hook
        /// arcs overlap on the closure bend, as drawn on reinforcement details.
        /// </summary>
        public static TieResult BuildHoop(HoopInput input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            var v = new ValidationResult();
            var result = new TieResult { Validation = v };
            var bars = input.Bars ?? new List<HoopBar>();
            if (bars.Count < 3) v.Error("A closed hoop needs at least three longitudinal bars.");
            if (bars.Any(b => !(b.Diameter > 0))) v.Error("Every longitudinal bar diameter must be positive.");
            CheckCommon(input.TieDiameter, input.ClearGap, input.InsideMandrelDiameter, v);
            if (!(input.TailLengthA >= 0) || !(input.TailLengthB >= 0)) v.Error("Tail length cannot be negative.");
            if (!v.IsValid) return result;

            var hull = HoopCorners(bars);
            if (hull.Count < 3) { v.Error("The selected bars are collinear; a hoop needs bars around an area."); return result; }
            if (input.Flip) hull.Reverse();
            int closure = hull.IndexOf(input.ClosureBar);
            if (closure < 0) closure = 0;
            hull = hull.Skip(closure).Concat(hull.Take(closure)).ToList();
            result.HoopBars = hull;
            result.InteriorBars = bars.Count - hull.Count;

            double dref = bars.Max(b => b.Diameter);
            Size(result, dref, input.TieDiameter, input.ClearGap, input.InsideMandrelDiameter);
            double d = result.FitRadius, rc = result.CentrelineRadius;
            result.ClearanceA = d - input.TieDiameter / 2 - bars[hull[0]].Diameter / 2;
            result.ClearanceB = result.ClearanceA;

            // σ = +1 for a counter-clockwise loop (outside = right of travel, left turns), −1 when flipped.
            double sigma = input.Flip ? -1 : 1;
            int k = hull.Count;
            var p = hull.Select(i => bars[i].Center).ToList();
            Vector2 Outward(Vector2 along) => sigma > 0 ? -along.PerpLeft() : along.PerpLeft();

            // Corner bend centres: the bar nests in the bend, touching the inside of the arc on the corner
            // bisector, so the centre moves inward by rc − d (zero when the fit governs). A bend larger than
            // the bar cannot also hold it against both straight legs; those stand slightly off the bar.
            var centre = new Point2[k];
            for (int i = 0; i < k; i++)
            {
                var nin = Outward((p[i] - p[(i - 1 + k) % k]).Normalized());
                var nout = Outward((p[(i + 1) % k] - p[i]).Normalized());
                centre[i] = p[i] - (nin + nout).Normalized() * (rc - d);
            }

            // Legs: outer tangents between consecutive bends (equal radii), then the turn at each bend.
            var dir = new Vector2[k];
            var nrm = new Vector2[k];
            for (int i = 0; i < k; i++)
            {
                dir[i] = (centre[(i + 1) % k] - centre[i]).Normalized();
                nrm[i] = Outward(dir[i]);
            }
            var turn = new double[k];
            for (int i = 0; i < k; i++)
                turn[i] = Math.Acos(Math.Max(-1, Math.Min(1, nrm[(i - 1 + k) % k].Dot(nrm[i]))));

            // A bend too large for the bar spacing folds the loop over: a leg then runs against its bars,
            // or the loop stops turning one way.
            for (int i = 0; i < k; i++)
            {
                bool backwards = dir[i].Dot(p[(i + 1) % k] - p[i]) <= 1e-9;
                bool folded = sigma * dir[(i - 1 + k) % k].Cross(dir[i]) < -1e-9;
                if (backwards || folded)
                {
                    v.Error("Bars are too close together for the bend radius (centreline radius " +
                            rc.ToString("0.#", CultureInfo.InvariantCulture) + " mm).");
                    return result;
                }
            }

            var vertices = new List<PathVertex>();
            double thetaA = GeometryMath.ToRadians((int)input.AngleA);
            double thetaB = GeometryMath.ToRadians((int)input.AngleB);

            // Start hook A on the closure bar, leading into leg 0.
            var hookA = Hook(centre[0], nrm[0], -dir[0], rc, -sigma * thetaA, input.TailLengthA);
            if (input.TailLengthA > 1e-9) vertices.Add(new PathVertex(hookA.TailEnd));
            vertices.Add(new PathVertex(hookA.Rim, Path2.BulgeFromSweep(sigma * thetaA)));
            result.TailDirectionA = hookA.TailDirection;

            for (int i = 0; i < k; i++)
            {
                var legStart = centre[i] + nrm[i] * rc;
                int j = (i + 1) % k;
                var legEnd = centre[j] + nrm[i] * rc;
                vertices.Add(new PathVertex(legStart));
                if (j != 0) vertices.Add(new PathVertex(legEnd, Path2.BulgeFromSweep(sigma * turn[j])));
                else vertices.Add(new PathVertex(legEnd, Path2.BulgeFromSweep(sigma * thetaB)));
            }

            // End hook B on the closure bar, continuing from the last leg.
            var hookB = Hook(centre[0], nrm[k - 1], dir[k - 1], rc, sigma * thetaB, input.TailLengthB);
            vertices.Add(new PathVertex(hookB.Rim));
            if (input.TailLengthB > 1e-9) vertices.Add(new PathVertex(hookB.TailEnd));
            result.TailDirectionB = hookB.TailDirection;

            var path = new Path2(Dedupe(vertices), false);
            CheckBarsClear(path, bars.Select((b, i) => (b.Center, b.Diameter, "#" + (i + 1))), input.TieDiameter, v);
            CheckTailsCrossLegs(path, input.TailLengthA > 1e-9, input.TailLengthB > 1e-9, v);
            if (!v.IsValid) return result;

            result.Path = path;
            return result;
        }

        /// <summary>Bars on the convex hull, counter-clockwise, starting from the lowest-left bar.</summary>
        public static List<int> HoopCorners(IList<HoopBar> bars)
        {
            var idx = Enumerable.Range(0, bars.Count)
                .OrderBy(i => bars[i].Center.X).ThenBy(i => bars[i].Center.Y).ToList();
            if (idx.Count < 3) return idx;
            double Cross(int o, int a, int b) => (bars[a].Center - bars[o].Center).Cross(bars[b].Center - bars[o].Center);
            var lower = new List<int>();
            foreach (var i in idx)
            {
                while (lower.Count >= 2 && Cross(lower[lower.Count - 2], lower[lower.Count - 1], i) <= 1e-9) lower.RemoveAt(lower.Count - 1);
                lower.Add(i);
            }
            var upper = new List<int>();
            for (int t = idx.Count - 1; t >= 0; t--)
            {
                int i = idx[t];
                while (upper.Count >= 2 && Cross(upper[upper.Count - 2], upper[upper.Count - 1], i) <= 1e-9) upper.RemoveAt(upper.Count - 1);
                upper.Add(i);
            }
            lower.RemoveAt(lower.Count - 1);
            upper.RemoveAt(upper.Count - 1);
            var hull = lower.Concat(upper).ToList();
            // Start from the bottom-left corner so closure choices are stable.
            int start = hull.IndexOf(hull.OrderBy(i => bars[i].Center.Y).ThenBy(i => bars[i].Center.X).First());
            return hull.Skip(start).Concat(hull.Take(start)).ToList();
        }

        private struct HookPoints { public Point2 Rim; public Vector2 TailDirection; public Point2 TailEnd; }

        /// <summary>
        /// A hook bending around <paramref name="centre"/>, starting where the leg (direction
        /// <paramref name="legDir"/>, side <paramref name="n"/>) meets the bend and turning by
        /// <paramref name="signedSweep"/>.
        /// </summary>
        private static HookPoints Hook(Point2 centre, Vector2 n, Vector2 legDir, double r, double signedSweep, double tail)
        {
            var rim = centre + n.Rotate(signedSweep) * r;
            var tailDir = legDir.Rotate(signedSweep);
            return new HookPoints { Rim = rim, TailDirection = tailDir, TailEnd = rim + tailDir * tail };
        }

        private static void Size(TieResult r, double dref, double tie, double gap, double mandrel)
        {
            r.ReferenceDiameter = dref;
            r.FitRadius = dref / 2 + gap + tie / 2;
            r.MandrelRadius = mandrel / 2 + tie / 2;
            r.CentrelineRadius = Math.Max(r.FitRadius, r.MandrelRadius);
        }

        private static void CheckCommon(double tie, double gap, double mandrel, ValidationResult v)
        {
            if (!(tie > 0)) v.Error("Tie diameter must be positive.");
            if (gap < 0) v.Error("Clear gap cannot be negative.");
            if (!(mandrel > 0)) v.Error("Inside mandrel diameter must be positive.");
        }

        private static List<PathVertex> Dedupe(List<PathVertex> vertices)
        {
            var outList = new List<PathVertex>();
            foreach (var vtx in vertices)
            {
                if (outList.Count > 0 && outList[outList.Count - 1].Point.IsEqualTo(vtx.Point, 1e-9))
                {
                    // Zero-length leg: keep the later vertex's bulge on the merged point.
                    outList[outList.Count - 1] = new PathVertex(vtx.Point, vtx.Bulge);
                    continue;
                }
                outList.Add(vtx);
            }
            return outList;
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
                    double dd = GeometryMath.SegmentToSegmentDistance(pts[i], pts[i + 1], pts[j], pts[j + 1]);
                    if (dd < tieDiameter - 1e-6)
                    {
                        v.Error("The hooks or tails overlap other parts of the tie. Reduce the tail length, change the hook angle or select bars further apart.");
                        return;
                    }
                }
            }
        }

        /// <summary>
        /// Hoop: a tail must not cross a straight leg. The hook arcs overlap on purpose, and a tail lying
        /// along a leg (a 90° closure hook lapping the adjacent side) is a lap, not a crossing.
        /// </summary>
        private static void CheckTailsCrossLegs(Path2 path, bool hasTailA, bool hasTailB, ValidationResult v)
        {
            var segs = path.Segments.ToList();
            var tails = new List<int>();
            if (hasTailA) tails.Add(0);
            if (hasTailB) tails.Add(segs.Count - 1);
            for (int ti = 0; ti < tails.Count; ti++)
            {
                var t = segs[tails[ti]];
                for (int i = 0; i < segs.Count; i++)
                {
                    if (tails.Contains(i) || segs[i].IsArc) continue;
                    var td = t.End - t.Start; var ld = segs[i].End - segs[i].Start;
                    if (Math.Abs(td.Normalized().Cross(ld.Normalized())) < 1e-9) continue; // collinear lap
                    if (GeometryMath.SegmentsIntersect(t.Start, t.End, segs[i].Start, segs[i].End, 1e-6))
                    {
                        v.Error("A hook tail crosses the hoop. Shorten the tails or choose another closure corner.");
                        return;
                    }
                }
            }
        }

        /// <summary>Rejects geometry where the tie centreline passes through a longitudinal bar.</summary>
        private static void CheckBarsClear(Path2 path, IEnumerable<(Point2 c, double d, string name)> bars, double tieDiameter, ValidationResult v)
        {
            foreach (var (c, d, name) in bars)
            {
                double needed = d / 2 + tieDiameter / 2 - 1e-6;
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

    public sealed class HoopBar
    {
        public Point2 Center { get; }
        public double Diameter { get; }
        public HoopBar(Point2 center, double diameter) { Center = center; Diameter = diameter; }
    }

    public sealed class HoopInput
    {
        public IList<HoopBar> Bars { get; set; }
        public double TieDiameter { get; set; } = 10;
        public HookAngle AngleA { get; set; } = HookAngle.Deg135;
        public HookAngle AngleB { get; set; } = HookAngle.Deg135;

        /// <summary>Index into <see cref="Bars"/> of the corner bar carrying both hooks.</summary>
        public int ClosureBar { get; set; }

        /// <summary>False: counter-clockwise loop. True: clockwise (mirrors the closure hooks).</summary>
        public bool Flip { get; set; }
        public double ClearGap { get; set; }
        public double InsideMandrelDiameter { get; set; } = 40;
        public double TailLengthA { get; set; } = 100;
        public double TailLengthB { get; set; } = 100;
    }
}
