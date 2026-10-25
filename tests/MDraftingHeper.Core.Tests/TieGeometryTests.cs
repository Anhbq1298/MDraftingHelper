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

        [Theory]
        [InlineData(HookAngle.Deg90, false)]
        [InlineData(HookAngle.Deg135, false)]
        [InlineData(HookAngle.Deg180, false)]
        [InlineData(HookAngle.Deg90, true)]
        [InlineData(HookAngle.Deg135, true)]
        [InlineData(HookAngle.Deg180, true)]
        public void AllHooks_AreTangentContinuous_WithExactSweepsAroundTheBars(HookAngle angle, bool flip)
        {
            var r = TieGeometryBuilder.Build(Input(angle, flip));
            Assert.True(r.Validation.IsValid, r.Validation.ToString());
            AssertG1Continuous(r.Path);

            var arcs = r.Path.Segments.Where(s => s.IsArc).ToList();
            Assert.Equal(2, arcs.Count);
            foreach (var a in arcs)
            {
                Assert.Equal((int)angle * Math.PI / 180, Math.Abs(a.Sweep), 9);
                Assert.Equal(r.CentrelineRadius, a.Radius, 7);
            }
            // Mandrel governs (R = 25 > d = 15): bend centres move away from the leg by R − d; bars stay put.
            double shift = r.CentrelineRadius - r.FitRadius;
            double side = flip ? -1 : 1;
            Assert.True(arcs[0].Center.IsEqualTo(new Point2(0, -side * shift), Tol));
            Assert.True(arcs[1].Center.IsEqualTo(new Point2(300, -side * shift), Tol));
        }

        [Fact]
        public void Hook180_IsSemicircularReturnWithTailParallelToTie()
        {
            var r = TieGeometryBuilder.Build(Input(HookAngle.Deg180));
            double rc = r.CentrelineRadius, d = r.FitRadius;
            // Tie on the left (+y) of A→B; the B hook returns on the -y side heading back towards A.
            Assert.Equal(-1, r.TailDirectionB.Value.X, 12);
            Assert.Equal(0, r.TailDirectionB.Value.Y, 12);
            var v = r.Path.Vertices;
            double returnY = d - 2 * rc; // semicircle of radius rc starting from the leg at +d
            Assert.True(v[v.Count - 2].Point.IsEqualTo(new Point2(300, returnY), Tol));
            Assert.True(v[v.Count - 1].Point.IsEqualTo(new Point2(300 - 60, returnY), Tol));
            // Straight tie touches the bars: offset d = D/2 + tie/2 from the reference axis.
            var tie = r.Path.Segments.Single(s => !s.IsArc && Math.Abs(s.Start.Y - d) < Tol && Math.Abs(s.End.Y - d) < Tol);
            Assert.Equal(300, tie.ChordLength, 9);
        }

        [Fact]
        public void Hook90_TailIsPerpendicularToTie()
        {
            var r = TieGeometryBuilder.Build(Input(HookAngle.Deg90));
            Assert.Equal(0, r.TailDirectionB.Value.X, 12);
            Assert.Equal(-1, r.TailDirectionB.Value.Y, 12);
            Assert.Equal(0, r.TailDirectionA.Value.X, 12);
            Assert.Equal(-1, r.TailDirectionA.Value.Y, 12);
        }

        [Fact]
        public void Hook135_TailPointsInwardAt45DegreesToTheAxis()
        {
            var r = TieGeometryBuilder.Build(Input(HookAngle.Deg135));
            var t = r.TailDirectionB.Value;
            Assert.Equal(-Math.Sqrt(0.5), t.X, 12);
            Assert.Equal(-Math.Sqrt(0.5), t.Y, 12);
        }

        [Fact]
        public void UnequalBars_UseLargerDiameterAtBothEnds_AndKeepActualCentres()
        {
            var input = Input(HookAngle.Deg135, da: 16, db: 25);
            input.InsideMandrelDiameter = 10; // let the fit radius govern
            var r = TieGeometryBuilder.Build(input);
            Assert.True(r.Validation.IsValid, r.Validation.ToString());
            Assert.Equal(25, r.ReferenceDiameter);
            Assert.Equal(25 / 2.0 + 10 / 2.0, r.CentrelineRadius, 12);
            var arcs = r.Path.Segments.Where(s => s.IsArc).ToList();
            Assert.Equal(arcs[0].Radius, arcs[1].Radius, 9);
            Assert.True(arcs[0].Center.IsEqualTo(input.CenterA, Tol));
            Assert.True(arcs[1].Center.IsEqualTo(input.CenterB, Tol));
            Assert.True(r.ClearanceA > r.ClearanceB); // the smaller bar gets the larger clearance
            Assert.Equal(0, r.ClearanceB, 12);
            Assert.Equal(4.5, r.ClearanceA, 12);
        }

        [Fact]
        public void MandrelRadius_GovernsWhenLarger_AndClearanceIsReported()
        {
            var input = Input(HookAngle.Deg135, da: 20, db: 20);
            input.InsideMandrelDiameter = 64;
            var r = TieGeometryBuilder.Build(input);
            Assert.Equal(32 + 5, r.CentrelineRadius, 12);
            // No extra clearance: the straight tie still touches the bar; only the bend centre moves.
            Assert.Equal(0, r.ClearanceA, 12);
            double touch = 10 + 5;
            Assert.Equal(touch, r.Path.Segments.Min(s => s.DistanceTo(input.CenterA)), 9);
            Assert.Equal(touch, r.Path.Segments.Min(s => s.DistanceTo(input.CenterB)), 9);
        }

        [Fact]
        public void Flip_MirrorsAcrossTheReferenceAxis()
        {
            var a = TieGeometryBuilder.Build(Input(HookAngle.Deg135));
            var b = TieGeometryBuilder.Build(Input(HookAngle.Deg135, flip: true));
            Assert.Equal(a.Path.Vertices.Count, b.Path.Vertices.Count);
            for (int i = 0; i < a.Path.Vertices.Count; i++)
            {
                var p = a.Path.Vertices[i]; var q = b.Path.Vertices[i];
                Assert.True(new Point2(p.Point.X, -p.Point.Y).IsEqualTo(q.Point, Tol));
                Assert.Equal(-p.Bulge, q.Bulge, 12);
            }
        }

        [Theory]
        [InlineData(HookAngle.Deg90)]
        [InlineData(HookAngle.Deg135)]
        [InlineData(HookAngle.Deg180)]
        public void Geometry_IsInvariantUnderRotationAndTranslation(HookAngle angle)
        {
            double rot = 0.7, dx = 1234.5, dy = -987.25;
            Func<Point2, Point2> map = p => new Point2(
                p.X * Math.Cos(rot) - p.Y * Math.Sin(rot) + dx,
                p.X * Math.Sin(rot) + p.Y * Math.Cos(rot) + dy);
            var baseResult = TieGeometryBuilder.Build(Input(angle, da: 20, db: 25));
            var b = map(new Point2(300, 0));
            var moved = TieGeometryBuilder.Build(Input(angle, da: 20, db: 25, ax: dx, ay: dy, bx: b.X, by: b.Y));
            Assert.Equal(baseResult.Path.Vertices.Count, moved.Path.Vertices.Count);
            for (int i = 0; i < baseResult.Path.Vertices.Count; i++)
            {
                Assert.True(map(baseResult.Path.Vertices[i].Point).IsEqualTo(moved.Path.Vertices[i].Point, 1e-6));
                Assert.Equal(baseResult.Path.Vertices[i].Bulge, moved.Path.Vertices[i].Bulge, 12);
            }
        }

        [Fact]
        public void SingleHook_EndsAtTheTangentPointOfTheOtherBar()
        {
            var r = TieGeometryBuilder.Build(Input(HookAngle.Deg135, ends: HookEnds.EndB));
            Assert.True(r.Path.Vertices[0].Point.IsEqualTo(new Point2(0, r.FitRadius), Tol));
            Assert.Single(r.Path.Segments.Where(s => s.IsArc));
            Assert.Null(r.TailDirectionA);
        }

        [Fact]
        public void Degenerate_AndImpossibleInputs_AreRejected()
        {
            Assert.False(TieGeometryBuilder.Build(Input(HookAngle.Deg135, bx: 0, by: 0)).Validation.IsValid);
            Assert.False(TieGeometryBuilder.Build(Input(HookAngle.Deg135, bx: 15, by: 0)).Validation.IsValid); // bars overlap
            Assert.False(TieGeometryBuilder.Build(Input(HookAngle.Deg135, da: 0)).Validation.IsValid);
            Assert.False(TieGeometryBuilder.Build(Input(HookAngle.Deg135, tail: -1)).Validation.IsValid);
        }

        [Fact]
        public void LongTails_ThatClash_AreRejectedNotShortened()
        {
            // 180° tails run towards each other on the same line; 2 × 200 > 300 spacing.
            var r = TieGeometryBuilder.Build(Input(HookAngle.Deg180, tail: 200));
            Assert.False(r.Validation.IsValid);
            Assert.Null(r.Path);
            // 135° tails crossing bar A/B or each other in a close pair.
            var c = TieGeometryBuilder.Build(Input(HookAngle.Deg135, bx: 80, tail: 150));
            Assert.False(c.Validation.IsValid);
        }

        [Fact]
        public void PerEndTailLengths_AreAppliedIndependently()
        {
            var input = Input(HookAngle.Deg135);
            input.AngleA = HookAngle.Deg90;
            input.AngleB = HookAngle.Deg180;
            input.TailLengthA = 70;
            input.TailLengthB = 50;
            var r = TieGeometryBuilder.Build(input);
            Assert.True(r.Validation.IsValid, r.Validation.ToString());
            Assert.Equal(70, r.Path.Segments.First().ChordLength, 9);
            Assert.Equal(50, r.Path.Segments.Last().ChordLength, 9);
        }

        [Fact]
        public void PresetRules_SelectMandrelByDiameter_AndTailByAngle()
        {
            var p = new HookPreset
            {
                MandrelRules = { new MandrelRule { MaxTieDiameter = 16, FactorOfTieDiameter = 4 }, new MandrelRule { FactorOfTieDiameter = 7 } },
                TailRules = { new TailRule { AngleDegrees = 135, FactorOfTieDiameter = 5, MinimumMm = 50 } }
            };
            Assert.Equal(40, p.InsideMandrelDiameter(10));
            Assert.Equal(140, p.InsideMandrelDiameter(20));
            Assert.Equal(50, p.TailLength(HookAngle.Deg135, 8));
            Assert.Equal(80, p.TailLength(HookAngle.Deg135, 16));
            Assert.Null(p.TailLength(HookAngle.Deg90, 16));
        }
    }
}
