using System;
using System.Collections.Generic;
using System.Linq;
using MDraftingHeper.Core.Geometry;
using MDraftingHeper.Core.Rebar;
using Xunit;

namespace MDraftingHeper.Core.Tests
{
    public class RebarTests
    {
        private static int _id;
        private static RebarBar Bar(double d, double x = 0, double y = 0, string id = null) =>
            new RebarBar(id ?? ("h" + (++_id)), new Point2(x, y), d);

        private static Path2 Rect(double w, double h) => new Path2(new[]
        {
            new PathVertex(new Point2(0, 0)), new PathVertex(new Point2(w, 0)),
            new PathVertex(new Point2(w, h)), new PathVertex(new Point2(0, h))
        }, true);

        [Fact]
        public void MixedDiameters_AreGroupedAndSummedAtFullPrecision()
        {
            var bars = Enumerable.Range(0, 4).Select(_ => Bar(20))
                .Concat(Enumerable.Range(0, 4).Select(_ => Bar(16)))
                .Concat(new[] { Bar(15.7), Bar(15.7) }).ToList();
            double ag = 400.0 * 400.0;

            var r = RebarCalculator.Calculate(bars, ag);

            Assert.Equal(3, r.Groups.Count);
            Assert.Equal(10, r.TotalCount);
            double expectedAs = 4 * Math.PI * 400 / 4 + 4 * Math.PI * 256 / 4 + 2 * Math.PI * 15.7 * 15.7 / 4;
            Assert.Equal(expectedAs, r.As, 9);
            Assert.Equal(100 * expectedAs / ag, r.RhoPercent, 12);

            var nonStd = r.Groups.Single(g => !g.IsNominal);
            Assert.Equal(2, nonStd.Quantity);
            Assert.Contains("non-standard", nonStd.Label);
            Assert.Equal(15.7, nonStd.MeanDiameter, 12); // measured size kept, not rounded to 16
            Assert.Equal("H16", r.Groups.Single(g => g.NominalDiameter == 16).Label);
            Assert.Equal("H20", r.Groups.Single(g => g.NominalDiameter == 20).Label);
        }

        [Fact]
        public void Tolerance_GroupsCloseDiameters_ButDoesNotChain()
        {
            var opts = new RebarGroupingOptions { DiameterTolerance = 0.1 };
            var near = RebarCalculator.Calculate(new[] { Bar(15.95), Bar(16.0), Bar(16.05) }, 1e5, opts);
            Assert.Single(near.Groups);
            Assert.Equal("H16", near.Groups[0].Label);

            var chain = RebarCalculator.Calculate(new[] { Bar(16.0), Bar(16.08), Bar(16.16), Bar(16.24) }, 1e5, opts);
            Assert.Equal(2, chain.Groups.Count);
            Assert.All(chain.Groups, g => Assert.True(g.MaxDiameter - g.MinDiameter <= 0.1 + 1e-9));
        }

        [Fact]
        public void NominalLabel_RequiresEveryMemberWithinTolerance()
        {
            var opts = new RebarGroupingOptions { DiameterTolerance = 0.1 };
            var r = RebarCalculator.Calculate(new[] { Bar(16.09), Bar(16.15) }, 1e5, opts);
            Assert.Single(r.Groups);
            Assert.False(r.Groups[0].IsNominal); // 16.15 is outside 16 ± 0.1
        }

        [Fact]
        public void DuplicateIds_AreCountedOnceAndReported()
        {
            var r = RebarCalculator.Calculate(new[] { Bar(20, id: "A1"), Bar(20, id: "A1"), Bar(20, id: "A2") }, 1e5);
            Assert.Equal(2, r.TotalCount);
            Assert.Equal(new[] { "A1" }, r.DuplicateIds);
        }

        [Fact]
        public void TargetRho_GivesRequiredAreaAndDifference()
        {
            var r = RebarCalculator.Calculate(new[] { Bar(20), Bar(20), Bar(20), Bar(20) }, 250000, targetRhoPercent: 1.0);
            Assert.Equal(2500, r.AsTarget.Value, 9);
            Assert.Equal(r.As - 2500, r.AreaDifference.Value, 9);
        }

        [Fact]
        public void InvalidInputs_AreRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => RebarCalculator.Calculate(new[] { Bar(20) }, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => RebarCalculator.Calculate(new[] { Bar(20) }, 1e5, targetRhoPercent: -1));
            Assert.Throws<ArgumentException>(() => RebarCalculator.Calculate(new[] { Bar(0) }, 1e5));
        }

        [Fact]
        public void Boundary_RectangleArea_AndClassification()
        {
            Assert.True(SectionBoundary.TryCreate(Rect(400, 600), out var b).IsValid);
            Assert.Equal(240000, b.Area, 6);
            Assert.Equal(BarPlacement.Inside, b.Classify(Bar(20, 50, 50)));
            Assert.Equal(BarPlacement.Crossing, b.Classify(Bar(20, 5, 300)));
            Assert.Equal(BarPlacement.Outside, b.Classify(Bar(20, -50, 300)));
        }

        [Fact]
        public void Boundary_CurvedPolyline_AreaIsExact()
        {
            double r = 250;
            var circle = new Path2(new[] { new PathVertex(new Point2(r, 0), 1), new PathVertex(new Point2(-r, 0), 1) }, true);
            Assert.True(SectionBoundary.TryCreate(circle, out var b).IsValid);
            Assert.Equal(Math.PI * r * r, b.Area, 6);

            // Rectangle with one semicircular end: 400x200 plus half disc of radius 100.
            var slot = new Path2(new[]
            {
                new PathVertex(new Point2(0, 0)), new PathVertex(new Point2(400, 0), 1),
                new PathVertex(new Point2(400, 200)), new PathVertex(new Point2(0, 200))
            }, true);
            Assert.True(SectionBoundary.TryCreate(slot, out var s).IsValid);
            Assert.Equal(400 * 200 + Math.PI * 100 * 100 / 2, s.Area, 6);
            Assert.Equal(BarPlacement.Inside, s.Classify(Bar(20, 450, 100)));
        }

        [Fact]
        public void Boundary_RejectsOpenSelfIntersectingAndDegenerate()
        {
            var open = new Path2(Rect(100, 100).Vertices, false);
            Assert.False(SectionBoundary.TryCreate(open, out _).IsValid);

            var bowTie = new Path2(new[]
            {
                new PathVertex(new Point2(0, 0)), new PathVertex(new Point2(100, 100)),
                new PathVertex(new Point2(100, 0)), new PathVertex(new Point2(0, 100))
            }, true);
            Assert.False(SectionBoundary.TryCreate(bowTie, out _).IsValid);

            var dup = new Path2(new[]
            {
                new PathVertex(new Point2(0, 0)), new PathVertex(new Point2(0, 0)),
                new PathVertex(new Point2(100, 0)), new PathVertex(new Point2(0, 100))
            }, true);
            Assert.False(SectionBoundary.TryCreate(dup, out _).IsValid);
        }
    }
}
