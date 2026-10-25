using System;
using System.Linq;
using MDraftingHeper.Core.Geometry;
using MDraftingHeper.Core.Hooks;
using MDraftingHeper.Core.Rectangular;
using Xunit;

namespace MDraftingHeper.Core.Tests
{
    public class RectangularTests
    {
        private static RectangularSectionModel FaceSection(bool auto = true)
        {
            var s = new RectangularSectionModel();
            s.AutoTieSettings.Enabled = auto;
            var v = LayoutChangeCoordinator.CommitFaceLayout(s, auto);
            Assert.True(v.IsValid, v.Summary);
            return s;
        }

        [Fact]
        public void FaceLayout_HasUniqueCornersNamesCountsAndActualArea()
        {
            var s = new RectangularSectionModel();
            s.FaceParameters.BarsOnBFace = 5;
            s.FaceParameters.BarsOnHFace = 4;
            s.FaceParameters.BFaceDiameter = 20;
            s.FaceParameters.HFaceDiameter = 25;
            var v = LayoutChangeCoordinator.CommitFaceLayout(s, false);
            Assert.True(v.IsValid, v.Summary);
            Assert.Equal(2 * 5 + 2 * 4 - 4, s.LongitudinalBars.Count);
            Assert.Equal(s.LongitudinalBars.Count, s.LongitudinalBars.Select(b => b.BarId).Distinct().Count());
            Assert.Equal(s.LongitudinalBars.Count, s.LongitudinalBars.Select(b => b.BarName.ToUpperInvariant()).Distinct().Count());
            Assert.Contains(new[] { "C-TL", "C-TR", "C-BL", "C-BR" }, n => s.LongitudinalBars.Single(b => b.BarName == n).Diameter == 25);
            Assert.Equal(3, s.LongitudinalBars.Count(b => b.BarName.StartsWith("T")));
            double expected = 4 * Math.PI * 25 * 25 / 4 + 6 * Math.PI * 20 * 20 / 4 + 4 * Math.PI * 25 * 25 / 4;
            Assert.Equal(expected, s.SteelArea, 7);
        }

        [Fact]
        public void Cover_IsOutsideTieAndBarCentresUseActualDiameters()
        {
            var s = FaceSection(false);
            var tr = s.LongitudinalBars.Single(b => b.BarName == "C-TR");
            Assert.Equal(s.Width / 2 - s.Cover - 10 - tr.Diameter / 2, tr.Center.X, 8);
            Assert.Equal(s.Height / 2 - s.Cover - 10 - tr.Diameter / 2, tr.Center.Y, 8);
        }

        [Fact]
        public void FaceRegeneration_ClearsAllTiesAndCreatesFreshBarIdentities()
        {
            var s = FaceSection(true);
            var old = s.LongitudinalBars.Select(b => b.BarId).ToArray();
            Assert.NotEmpty(s.TieBars);
            s.FaceParameters.BarsOnBFace++;
            var v = LayoutChangeCoordinator.CommitFaceLayout(s, false);
            Assert.True(v.IsValid, v.Summary);
            Assert.Empty(s.TieBars);
            Assert.True(s.TiesRequireDefinition);
            Assert.DoesNotContain(s.LongitudinalBars, b => old.Contains(b.BarId));
        }

        [Fact]
        public void CustomRename_PreservesRevisionIdentityAndTieConnectivity()
        {
            var s = new RectangularSectionModel();
            var a = new LongitudinalBar("Alpha", 20, new Point2(-100, 0));
            var b = new LongitudinalBar("Beta", 20, new Point2(100, 0));
            Assert.True(LayoutChangeCoordinator.CommitCustomLayout(s, new[] { a, b }).IsValid);
            var tie = new TieBar { TieName = "ST1", Kind = RectangularTieKind.CrossTie, BarAId = a.BarId, BarBId = b.BarId, LayoutRevision = s.LayoutRevision };
            tie.DirectlyRestrainedBarIds.Add(a.BarId); tie.DirectlyRestrainedBarIds.Add(b.BarId); s.TieBars.Add(tie);
            long revision = s.LayoutRevision;
            var v = LayoutChangeCoordinator.RenameCustomBar(s, a.BarId, "  ALPHA-RENAMED  ");
            Assert.True(v.IsValid, v.Summary);
            Assert.Equal(revision, s.LayoutRevision);
            Assert.Same(tie, Assert.Single(s.TieBars));
            Assert.Equal(a.BarId, tie.BarAId);
            Assert.Equal("ALPHA-RENAMED", a.BarName);
        }

        [Fact]
        public void CustomGeometryEdit_InvalidatesEveryTieButKeepsSurvivingIds()
        {
            var s = new RectangularSectionModel();
            var a = new LongitudinalBar("A", 20, new Point2(-100, 0));
            var b = new LongitudinalBar("B", 20, new Point2(100, 0));
            LayoutChangeCoordinator.CommitCustomLayout(s, new[] { a, b });
            s.TieBars.Add(new TieBar { TieName = "ST1", BarAId = a.BarId, BarBId = b.BarId, LayoutRevision = s.LayoutRevision });
            long before = s.LayoutRevision;
            a.Center = new Point2(-110, 0);
            var v = LayoutChangeCoordinator.CommitCustomLayout(s, new[] { a, b });
            Assert.True(v.IsValid, v.Summary);
            Assert.Equal(before + 1, s.LayoutRevision);
            Assert.Empty(s.TieBars);
            Assert.Contains(s.LongitudinalBars, x => x.BarId == a.BarId);
        }

        [Fact]
        public void CustomNames_AreTrimmedCaseInsensitiveUnique()
        {
            var s = new RectangularSectionModel();
            var v = LayoutChangeCoordinator.CommitCustomLayout(s, new[]
            {
                new LongitudinalBar("Bar 1", 20, new Point2(-100, 0)),
                new LongitudinalBar(" bar 1 ", 20, new Point2(100, 0))
            });
            Assert.False(v.IsValid);
            Assert.Contains("duplicated", v.Summary);
        }

        [Fact]
        public void AutoArrangement_IsFaceOnlyAndAuditUsesDirectRestraints()
        {
            var s = FaceSection(true);
            Assert.Contains(s.TieBars, t => t.Kind == RectangularTieKind.OuterHoop);
            Assert.Contains(s.TieBars, t => t.Kind == RectangularTieKind.CrossTie);
            Assert.Empty(RestraintAuditService.Audit(s).UnrestrainedNames);
            s.ReinforcementMode = ReinforcementMode.CustomCoordinates;
            Assert.False(RestraintArrangementService.Generate(s).Validation.IsValid);
        }

        [Fact]
        public void AutoArrangement_PreservesSymmetryForEvenFaceCounts()
        {
            var s = new RectangularSectionModel();
            s.FaceParameters.BarsOnBFace = 8;
            s.FaceParameters.BarsOnHFace = 2;
            var v = LayoutChangeCoordinator.CommitFaceLayout(s, true);
            Assert.True(v.IsValid, v.Summary);
            var links = s.TieBars.Where(t => t.Kind == RectangularTieKind.CrossTie)
                .Select(t => s.FindBar(t.BarAId.Value).Center.X).OrderBy(x => x).ToArray();
            Assert.Equal(2, links.Length);
            Assert.Equal(-links[0], links[1], 8);
        }

        [Fact]
        public void ClearDistance_UsesBothActualBarRadii()
        {
            var a = new LongitudinalBar("A", 20, new Point2(0, 0));
            var b = new LongitudinalBar("B", 40, new Point2(200, 0));
            Assert.Equal(200, RestraintArrangementService.FaceDistance(a, b, RestraintDistanceConvention.CentreToCentre), 8);
            Assert.Equal(170, RestraintArrangementService.FaceDistance(a, b, RestraintDistanceConvention.ClearDistance), 8);
        }

        [Fact]
        public void TieGeometry_RejectsStaleRevisionAndOuterHoopHasArcsAndClosure()
        {
            var s = FaceSection(true);
            var outer = s.TieBars.Single(t => t.Kind == RectangularTieKind.OuterHoop);
            var g = RectangularTieGeometryBuilder.Build(s, outer);
            Assert.True(g.Validation.IsValid, g.Validation.Summary);
            Assert.Equal(2, g.Paths.Count); // perimeter plus shared-engine closure/anchorage path
            Assert.Contains(g.Paths[0].Segments, x => x.IsArc);
            outer.LayoutRevision--;
            Assert.False(RectangularTieGeometryBuilder.Build(s, outer).Validation.IsValid);
        }

        [Fact]
        public void SharedTieEngine_SupportsDifferentEndAngles()
        {
            var r = TieGeometryBuilder.Build(new TieInput
            {
                CenterA = new Point2(0, 0), CenterB = new Point2(300, 0), DiameterA = 20, DiameterB = 25,
                TieDiameter = 10, AngleA = HookAngle.Deg90, AngleB = HookAngle.Deg180,
                InsideMandrelDiameter = 40, TailLength = 40
            });
            Assert.True(r.Validation.IsValid, r.Validation.Summary);
            var sweeps = r.Path.Segments.Where(x => x.IsArc).Select(x => Math.Round(Math.Abs(x.Sweep * 180 / Math.PI))).ToArray();
            Assert.Contains(90d, sweeps);
            Assert.Contains(180d, sweeps);
            Assert.Equal(25, r.ReferenceDiameter);
        }
    }
}
