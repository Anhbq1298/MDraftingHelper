using System;
using System.Linq;
using MDraftingHeper.Core.Geometry;
using MDraftingHeper.Core.Hooks;
using Xunit;

namespace MDraftingHeper.Core.Tests
{
    public class TieGeometryTests
    {
        private const double Tol = 1e-7;

        private static TieInput Input(HookAngle angle, bool flip = false, double da = 20, double db = 20,
                                      double ax = 0, double ay = 0, double bx = 300, double by = 0,
                                      double tail = 60, HookEnds ends = HookEnds.Both) =>
            new TieInput
            {
                CenterA = new Point2(ax, ay), CenterB = new Point2(bx, by), DiameterA = da, DiameterB = db,
                TieDiameter = 10, Angle = angle, Ends = ends, Flip = flip, ClearGap = 0,
                InsideMandrelDiameter = 40, TailLength = tail
            };

        private static void AssertG1Continuous(Path2 path)
        {
            var segs = path.Segments.ToList();
            for (int i = 0; i + 1 < segs.Count; i++)
            {
                var a = segs[i].EndTangent; var b = segs[i + 1].StartTangent;
                Assert.True(Math.Abs(a.Cross(b)) < 1e-9 && a.Dot(b) > 0, "Tangent break between segments " + i + " and " + (i + 1));
            }
        }

        [Fact]
        public void Hooks_AllAnglesAndFlip_AreTangent_ExactSweeps_CorrectTailDirections()
        {
            foreach (var angle in new[] { HookAngle.Deg90, HookAngle.Deg135, HookAngle.Deg180 })
            foreach (var flip in new[] { false, true })
            {
                var r = TieGeometryBuilder.Build(Input(angle, flip));
                Assert.True(r.Validation.IsValid, r.Validation.ToString());
                AssertG1Continuous(r.Path);
                var arcs = r.Path.Segments.Where(s => s.IsArc).ToList();
                Assert.Equal(2, arcs.Count);
                Assert.All(arcs, a => Assert.Equal((int)angle * Math.PI / 180, Math.Abs(a.Sweep), 9));
                Assert.All(arcs, a => Assert.Equal(r.CentrelineRadius, a.Radius, 7));
                // Mandrel governs (R = 25 > d = 15): bend centres move away from the leg by R − d; bars stay put.
                double shift = (r.CentrelineRadius - r.FitRadius) * (flip ? -1 : 1);
                Assert.True(arcs[0].Center.IsEqualTo(new Point2(0, -shift), Tol));
                Assert.True(arcs[1].Center.IsEqualTo(new Point2(300, -shift), Tol));
            }
            // Tail directions at B (tie on +y side): 90° straight down, 135° inward at 45°, 180° back along the tie.
            var t90 = TieGeometryBuilder.Build(Input(HookAngle.Deg90)).TailDirectionB.Value;
            Assert.Equal(0, t90.X, 12); Assert.Equal(-1, t90.Y, 12);
            var t135 = TieGeometryBuilder.Build(Input(HookAngle.Deg135)).TailDirectionB.Value;
            Assert.Equal(-Math.Sqrt(0.5), t135.X, 12); Assert.Equal(-Math.Sqrt(0.5), t135.Y, 12);

            var r180 = TieGeometryBuilder.Build(Input(HookAngle.Deg180));
            double rc = r180.CentrelineRadius, d = r180.FitRadius;
            var v = r180.Path.Vertices;
            Assert.True(v[v.Count - 2].Point.IsEqualTo(new Point2(300, d - 2 * rc), Tol)); // semicircular return
            Assert.True(v[v.Count - 1].Point.IsEqualTo(new Point2(240, d - 2 * rc), Tol)); // tail back parallel
            Assert.Equal(300, r180.Path.Segments.Single(s => !s.IsArc && Math.Abs(s.Start.Y - d) < Tol && Math.Abs(s.End.Y - d) < Tol).ChordLength, 9);

            var single = TieGeometryBuilder.Build(Input(HookAngle.Deg135, ends: HookEnds.EndB));
            Assert.True(single.Path.Vertices[0].Point.IsEqualTo(new Point2(0, single.FitRadius), Tol));
            Assert.Single(single.Path.Segments.Where(s => s.IsArc));
            Assert.Null(single.TailDirectionA);
        }

        [Fact]
        public void BendSizing_LargestBarGoverns_TieTouchesBars_MandrelOnlyMovesBendCentres()
        {
            var fit = Input(HookAngle.Deg135, da: 16, db: 25);
            fit.InsideMandrelDiameter = 10; // fit radius governs: concentric bends on the actual centres
            var r = TieGeometryBuilder.Build(fit);
            Assert.Equal(25, r.ReferenceDiameter);
            Assert.Equal(25 / 2.0 + 10 / 2.0, r.CentrelineRadius, 12);
            var arcs = r.Path.Segments.Where(s => s.IsArc).ToList();
            Assert.True(arcs[0].Center.IsEqualTo(fit.CenterA, Tol) && arcs[1].Center.IsEqualTo(fit.CenterB, Tol));
            Assert.Equal(4.5, r.ClearanceA, 12); // the smaller bar gets the larger clearance
            Assert.Equal(0, r.ClearanceB, 12);

            var mandrel = Input(HookAngle.Deg135);
            mandrel.InsideMandrelDiameter = 64;
            var m = TieGeometryBuilder.Build(mandrel);
            Assert.Equal(37, m.CentrelineRadius, 12);
            Assert.Equal(0, m.ClearanceA, 12);    // no extra clearance from the bigger bend
            Assert.Equal(15, m.Path.Segments.Min(s => s.DistanceTo(mandrel.CenterA)), 9);
            Assert.Equal(15, m.Path.Segments.Min(s => s.DistanceTo(mandrel.CenterB)), 9);

            // Visualisation (Tie & Hook): bend Ø = largest bar Ø + 5, concentric, 2.5 mm off the largest bar.
            var visual = Input(HookAngle.Deg135, da: 20, db: 25);
            visual.BendDiameter = VisualBend.Diameter(25);
            var vr = TieGeometryBuilder.Build(visual);
            Assert.True(vr.Validation.IsValid, vr.Validation.ToString());
            Assert.Equal(15, vr.CentrelineRadius, 12);
            var va = vr.Path.Segments.Where(s => s.IsArc).ToList();
            Assert.True(va[0].Center.IsEqualTo(visual.CenterA, Tol) && va[1].Center.IsEqualTo(visual.CenterB, Tol));
            Assert.Equal(15, vr.Path.Segments.Min(s => s.DistanceTo(visual.CenterB)), 9);
            visual.BendDiameter = 25;                                   // not larger than the bar
            Assert.False(TieGeometryBuilder.Build(visual).Validation.IsValid);
        }

        [Fact]
        public void Symmetry_FlipMirrors_AndGeometryIsRotationTranslationInvariant()
        {
            var a = TieGeometryBuilder.Build(Input(HookAngle.Deg135));
            var b = TieGeometryBuilder.Build(Input(HookAngle.Deg135, flip: true));
            for (int i = 0; i < a.Path.Vertices.Count; i++)
            {
                var p = a.Path.Vertices[i]; var q = b.Path.Vertices[i];
                Assert.True(new Point2(p.Point.X, -p.Point.Y).IsEqualTo(q.Point, Tol));
                Assert.Equal(-p.Bulge, q.Bulge, 12);
            }

            double rot = 0.7, dx = 1234.5, dy = -987.25;
            Func<Point2, Point2> map = p => new Point2(p.X * Math.Cos(rot) - p.Y * Math.Sin(rot) + dx, p.X * Math.Sin(rot) + p.Y * Math.Cos(rot) + dy);
            foreach (var angle in new[] { HookAngle.Deg90, HookAngle.Deg135, HookAngle.Deg180 })
            {
                var baseResult = TieGeometryBuilder.Build(Input(angle, da: 20, db: 25));
                var e = map(new Point2(300, 0));
                var moved = TieGeometryBuilder.Build(Input(angle, da: 20, db: 25, ax: dx, ay: dy, bx: e.X, by: e.Y));
                for (int i = 0; i < baseResult.Path.Vertices.Count; i++)
                {
                    Assert.True(map(baseResult.Path.Vertices[i].Point).IsEqualTo(moved.Path.Vertices[i].Point, 1e-6));
                    Assert.Equal(baseResult.Path.Vertices[i].Bulge, moved.Path.Vertices[i].Bulge, 12);
                }
            }
        }

        [Fact]
        public void Validation_RejectsInvalidAndClashingTies_PerEndTailsApply()
        {
            Assert.False(TieGeometryBuilder.Build(Input(HookAngle.Deg135, bx: 0, by: 0)).Validation.IsValid);  // coincident
            Assert.False(TieGeometryBuilder.Build(Input(HookAngle.Deg135, bx: 15, by: 0)).Validation.IsValid); // bars overlap
            Assert.False(TieGeometryBuilder.Build(Input(HookAngle.Deg135, da: 0)).Validation.IsValid);
            Assert.False(TieGeometryBuilder.Build(Input(HookAngle.Deg135, tail: -1)).Validation.IsValid);
            var clash = TieGeometryBuilder.Build(Input(HookAngle.Deg180, tail: 200)); // 180° tails meet: 2 × 200 > 300
            Assert.False(clash.Validation.IsValid);
            Assert.Null(clash.Path);                                                    // rejected, not shortened
            Assert.False(TieGeometryBuilder.Build(Input(HookAngle.Deg135, bx: 80, tail: 150)).Validation.IsValid);

            var perEnd = Input(HookAngle.Deg135);
            perEnd.AngleA = HookAngle.Deg90; perEnd.AngleB = HookAngle.Deg180;
            perEnd.TailLengthA = 70; perEnd.TailLengthB = 50;
            var r = TieGeometryBuilder.Build(perEnd);
            Assert.True(r.Validation.IsValid, r.Validation.ToString());
            Assert.Equal(70, r.Path.Segments.First().ChordLength, 9);
            Assert.Equal(50, r.Path.Segments.Last().ChordLength, 9);
        }
    }
}
