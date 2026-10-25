using System;
using System.Linq;
using MDraftingHeper.Core.Geometry;
using MDraftingHeper.Core.Hooks;
using Xunit;

namespace MDraftingHeper.Core.Tests
{
    public class HoopGeometryTests
    {
        private static HoopBar B(double x, double y, double d = 25) => new HoopBar(new Point2(x, y), d);

        private static HoopInput Rect(params HoopBar[] extra) => new HoopInput
        {
            Bars = new[] { B(0, 0), B(300, 0), B(300, 400), B(0, 400) }.Concat(extra).ToList(),
            TieDiameter = 10, AngleA = HookAngle.Deg135, AngleB = HookAngle.Deg135, ClosureBar = 0,
            InsideMandrelDiameter = Ec2DraftingRules.InsideMandrelDiameter(10),
            TailLengthA = Ec2DraftingRules.TailLength(HookAngle.Deg135, 10),
            TailLengthB = Ec2DraftingRules.TailLength(HookAngle.Deg135, 10)
        };

        [Fact]
        public void Ec2DraftingRules_MandrelAndTails()
        {
            Assert.Equal(40, Ec2DraftingRules.InsideMandrelDiameter(10));
            Assert.Equal(64, Ec2DraftingRules.InsideMandrelDiameter(16));
            Assert.Equal(140, Ec2DraftingRules.InsideMandrelDiameter(20));
            Assert.Equal(100, Ec2DraftingRules.TailLength(HookAngle.Deg90, 10));
            Assert.Equal(70, Ec2DraftingRules.TailLength(HookAngle.Deg135, 6));
            Assert.Equal(120, Ec2DraftingRules.TailLength(HookAngle.Deg135, 12));
            Assert.Equal(50, Ec2DraftingRules.TailLength(HookAngle.Deg180, 10));
            Assert.Equal(80, Ec2DraftingRules.TailLength(HookAngle.Deg180, 16));
        }

        [Fact]
        public void RectangularHoop_OnePolyline_BendsTouchBars_ParallelClosureTailsIntoCore()
        {
            var r = TieGeometryBuilder.BuildHoop(Rect());
            Assert.True(r.Validation.IsValid, r.Validation.ToString());
            Assert.False(r.Path.IsClosed); // open polyline: the two hooks overlap on the closure bend
            var segs = r.Path.Segments.ToList();
            for (int i = 0; i + 1 < segs.Count; i++)
                Assert.True(Math.Abs(segs[i].EndTangent.Cross(segs[i + 1].StartTangent)) < 1e-9, "tangent break at " + i);
            var arcs = segs.Where(s => s.IsArc).ToList();
            Assert.Equal(5, arcs.Count);                                // 3 corner bends + 2 hooks
            Assert.All(arcs, a => Assert.Equal(25, a.Radius, 7));       // 4φ mandrel: 20 + 5
            Assert.Equal(Math.PI / 2, Math.Abs(arcs[1].Sweep), 9);
            Assert.Equal(3 * Math.PI / 4, Math.Abs(arcs[0].Sweep), 9);
            Assert.Equal(new[] { 0, 1, 2, 3 }, r.HoopBars);
            foreach (var c in new[] { new Point2(0, 0), new Point2(300, 0), new Point2(300, 400), new Point2(0, 400) })
                Assert.Equal(12.5 + 5, segs.Min(s => s.DistanceTo(c)), 6); // bar touches its bend

            var tailA = r.TailDirectionA.Value; var tailB = r.TailDirectionB.Value;
            Assert.Equal(1, tailA.Dot(tailB), 9);                       // classic 135° closure
            Assert.Equal(Math.Sqrt(0.5), tailA.X, 9); Assert.Equal(Math.Sqrt(0.5), tailA.Y, 9);
            foreach (var p in new[] { segs.First().Start, segs.Last().End })
                Assert.True(p.X > 0 && p.X < 300 && p.Y > 0 && p.Y < 400, "tail end inside the section: " + p);
            Assert.Equal(100, segs.First().ChordLength, 9);
        }

        [Fact]
        public void Options_AnglesClosureFlipInteriorBars_AndInvalidHoops()
        {
            var input = Rect();
            input.AngleA = HookAngle.Deg90; input.TailLengthA = Ec2DraftingRules.TailLength(HookAngle.Deg90, 10);
            input.ClosureBar = 2;
            var r = TieGeometryBuilder.BuildHoop(input);
            Assert.True(r.Validation.IsValid, r.Validation.ToString());
            Assert.Equal(2, r.HoopBars[0]);
            var arcs = r.Path.Segments.Where(s => s.IsArc).ToList();
            Assert.Equal(Math.PI / 2, Math.Abs(arcs.First().Sweep), 9);     // 90° hook laps along the adjacent leg
            Assert.Equal(3 * Math.PI / 4, Math.Abs(arcs.Last().Sweep), 9);

            var flipped = Rect(); flipped.Flip = true;
            var f = TieGeometryBuilder.BuildHoop(flipped);
            Assert.Equal(TieGeometryBuilder.BuildHoop(Rect()).Path.Length, f.Path.Length, 6);
            Assert.All(f.Path.Segments.Where(s => s.IsArc), s => Assert.True(s.Sweep < 0)); // clockwise

            var visual = Rect(); visual.BendDiameter = VisualBend.Diameter(25); // Tie & Hook: Ø + 5, concentric
            var vr = TieGeometryBuilder.BuildHoop(visual);
            Assert.True(vr.Validation.IsValid, vr.Validation.ToString());
            Assert.All(vr.Path.Segments.Where(s => s.IsArc), s => Assert.Equal(15, s.Radius, 7));
            Assert.Equal(15, vr.Path.Segments.Min(s => s.DistanceTo(new Point2(300, 0))), 6);

            var withInterior = TieGeometryBuilder.BuildHoop(Rect(B(150, 200, 32)));
            Assert.Equal(32, withInterior.ReferenceDiameter);           // largest bar governs, even inside
            Assert.Equal(1, withInterior.InteriorBars);

            Assert.False(TieGeometryBuilder.BuildHoop(new HoopInput { Bars = new[] { B(0, 0), B(100, 0) } }).Validation.IsValid);
            Assert.False(TieGeometryBuilder.BuildHoop(new HoopInput { Bars = new[] { B(0, 0), B(100, 0), B(200, 0) } }).Validation.IsValid);
            var tooLong = Rect(); tooLong.TailLengthA = 900;
            Assert.False(TieGeometryBuilder.BuildHoop(tooLong).Validation.IsValid);
            var tight = new HoopInput { Bars = new[] { B(0, 0), B(30, 0), B(30, 30), B(0, 30) }, InsideMandrelDiameter = 140, TieDiameter = 20 };
            Assert.False(TieGeometryBuilder.BuildHoop(tight).Validation.IsValid);
        }
    }
}
