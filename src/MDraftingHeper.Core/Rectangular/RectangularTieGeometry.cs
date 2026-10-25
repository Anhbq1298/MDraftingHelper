using System;
using System.Collections.Generic;
using System.Linq;
using MDraftingHeper.Core.Geometry;
using MDraftingHeper.Core.Hooks;
using MDraftingHeper.Core.Validation;

namespace MDraftingHeper.Core.Rectangular
{
    public sealed class RectangularTieGeometryResult
    {
        public ValidationResult Validation { get; } = new ValidationResult();
        public List<Path2> Paths { get; } = new List<Path2>();
    }

    /// <summary>
    /// Adapts rectangular tie records to the shared hook engine. Cross-ties are emitted directly
    /// by TieGeometryBuilder. The outer hoop uses true quarter-circle bulges and a closure lap whose
    /// hooks are also emitted by TieGeometryBuilder, so preview and CAD consume identical paths.
    /// </summary>
    public static class RectangularTieGeometryBuilder
    {
        public static RectangularTieGeometryResult Build(RectangularSectionModel section, TieBar tie)
        {
            var r = new RectangularTieGeometryResult();
            if (tie.LayoutRevision != section.LayoutRevision)
            {
                r.Validation.Error("Tie '" + tie.TieName + "' is stale and must be redefined.");
                return r;
            }
            var size = TieBarSize.Find(tie.TieSizeName);
            if (size == null) { r.Validation.Error("Tie '" + tie.TieName + "' has an unsupported diameter."); return r; }
            if (tie.Kind == RectangularTieKind.CrossTie)
            {
                var a = tie.BarAId.HasValue ? section.FindBar(tie.BarAId.Value) : null;
                var b = tie.BarBId.HasValue ? section.FindBar(tie.BarBId.Value) : null;
                if (a == null || b == null || a.BarId == b.BarId)
                {
                    r.Validation.Error("Tie '" + tie.TieName + "' needs two different existing bars.");
                    return r;
                }
                var built = TieGeometryBuilder.Build(Input(tie, size, a, b));
                r.Validation.Merge(built.Validation);
                if (built.Path != null) r.Paths.Add(built.Path);
                ValidateFit(section, size, r);
                return r;
            }
            if (tie.Kind != RectangularTieKind.OuterHoop)
            {
                r.Validation.Error("Internal closed hoops are not available unless their ordered support geometry is valid.");
                return r;
            }
            BuildOuter(section, tie, size, r);
            ValidateFit(section, size, r);
            return r;
        }

        private static TieInput Input(TieBar tie, TieBarSize size, LongitudinalBar a, LongitudinalBar b) => new TieInput
        {
            CenterA = a.Center, CenterB = b.Center, DiameterA = a.Diameter, DiameterB = b.Diameter,
            TieDiameter = size.Diameter, Angle = tie.HookA, AngleA = tie.HookA, AngleB = tie.HookB,
            Ends = HookEnds.Both, Flip = tie.Flip, ClearGap = tie.ClearGap,
            InsideMandrelDiameter = tie.InsideMandrelDiameter, TailLength = tie.TailLength
        };

        private static void BuildOuter(RectangularSectionModel section, TieBar tie, TieBarSize size, RectangularTieGeometryResult result)
        {
            var supports = tie.OrderedSupportBarIds.Select(section.FindBar).ToList();
            if (supports.Count < 4 || supports.Any(b => b == null))
            {
                result.Validation.Error("The outer hoop needs its four current corner-bar references.");
                return;
            }
            double hx = section.Width / 2 - section.Cover - size.Diameter / 2;
            double hy = section.Height / 2 - section.Cover - size.Diameter / 2;
            double radius = Math.Max(supports.Max(b => b.Diameter) / 2 + tie.ClearGap + size.Diameter / 2,
                tie.InsideMandrelDiameter / 2 + size.Diameter / 2);
            if (!(hx > radius && hy > radius))
            {
                result.Validation.Error("The outer hoop bends do not fit the cover envelope.");
                return;
            }
            double q = Path2.BulgeFromSweep(-Math.PI / 2);
            var hoop = new Path2(new[]
            {
                new PathVertex(new Point2(-hx + radius, hy)),
                new PathVertex(new Point2(hx - radius, hy), q),
                new PathVertex(new Point2(hx, hy - radius)),
                new PathVertex(new Point2(hx, -hy + radius), q),
                new PathVertex(new Point2(hx - radius, -hy)),
                new PathVertex(new Point2(-hx + radius, -hy), q),
                new PathVertex(new Point2(-hx, -hy + radius)),
                new PathVertex(new Point2(-hx, hy - radius), q)
            }, true);
            result.Paths.Add(hoop);

            // A real lap/anchorage detail on the top leg, built by the same hook engine as MDH_TIE.
            var topIntermediate = section.LongitudinalBars.Where(b => b.IsOn(RectangularFace.Top) &&
                !b.BarName.StartsWith("C-", StringComparison.Ordinal)).OrderBy(b => b.Center.X).ToList();
            var tl = topIntermediate.Count >= 2 ? topIntermediate.First() : supports.FirstOrDefault(b => b.BarName == "C-TL") ?? supports[0];
            var tr = topIntermediate.Count >= 2 ? topIntermediate.Last() : supports.FirstOrDefault(b => b.BarName == "C-TR") ?? supports[1];
            var closureTie = new TieBar
            {
                HookA = tie.HookA, HookB = tie.HookB, Flip = false, ClearGap = tie.ClearGap,
                InsideMandrelDiameter = tie.InsideMandrelDiameter, TailLength = tie.TailLength
            };
            var closure = TieGeometryBuilder.Build(Input(closureTie, size, tl, tr));
            result.Validation.Merge(closure.Validation);
            if (closure.Path != null) result.Paths.Add(closure.Path);
        }

        private static void ValidateFit(RectangularSectionModel section, TieBarSize size, RectangularTieGeometryResult result)
        {
            double halfTie = size.Diameter / 2;
            double allowedX = section.Width / 2 - section.Cover;
            double allowedY = section.Height / 2 - section.Cover;
            foreach (var path in result.Paths)
            foreach (var p in path.Sample(Math.PI / 72))
                if (Math.Abs(p.X) + halfTie > allowedX + 1e-6 || Math.Abs(p.Y) + halfTie > allowedY + 1e-6)
                {
                    result.Validation.Error("Tie geometry does not fit the concrete-cover envelope; change the hooks, tails or bar references.");
                    return;
                }
            foreach (var path in result.Paths)
            foreach (var bar in section.LongitudinalBars)
            {
                double needed = bar.Diameter / 2 + halfTie - 1e-6;
                if (path.Segments.Any(seg => seg.DistanceTo(bar.Center) < needed))
                {
                    result.Validation.Error("Tie geometry passes through longitudinal bar '" + bar.BarName + "'.");
                    return;
                }
            }
        }
    }
}
