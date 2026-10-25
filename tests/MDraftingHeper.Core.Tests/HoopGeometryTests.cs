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

        private static void AssertG1(Path2 path)
        {
            var segs = path.Segments.ToList();
            for (int i = 0; i + 1 < segs.Count; i++)
            {
                var a = segs[i].EndTangent; var b = segs[i + 1].StartTangent;
                Assert.True(Math.Abs(a.Cross(b)) < 1e-9 && a.Dot(b) > 0, "Tangent break between segments " + i + " and " + (i + 1));
            }
        }

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
        public void RectangularHoop_IsOneContinuousPolyline_AroundAllCorners()
        {
            var r = TieGeometryBuilder.BuildHoop(Rect());
            Assert.True(r.Validation.IsValid, r.Validation.ToString());
            Assert.False(r.Path.IsClosed); // open polyline: the two hooks overlap on the closure bend
            AssertG1(r.Path);
            var arcs = r.Path.Segments.Where(s => s.IsArc).ToList();
            Assert.Equal(5, arcs.Count);                              // 3 corner bends + 2 hooks
            Assert.All(arcs, a => Assert.Equal(r.CentrelineRadius, a.Radius, 7));
            Assert.Equal(Math.PI / 2, Math.Abs(arcs[1].Sweep), 9);    // corner bends are 90° on a rectangle
            Assert.Equal(3 * Math.PI / 4, Math.Abs(arcs[0].Sweep), 9); // hooks 135°
            Assert.Equal(new[] { 0, 1, 2, 3 }, r.HoopBars);
        }

        [Fact]
        public void LegsTouchTheBars_AndMandrelMovesOnlyTheBendCentres()
        {
            var r = TieGeometryBuilder.BuildHoop(Rect());
            double touch = 12.5 + 5;                                  // D/2 + tie/2, no clear gap
            Assert.Equal(touch, r.FitRadius, 12);
            Assert.Equal(25, r.CentrelineRadius, 12);                 // 4φ mandrel: 20 + 5
            foreach (var c in new[] { new Point2(0, 0), new Point2(300, 0), new Point2(300, 400), new Point2(0, 400) })
                Assert.Equal(touch, r.Path.Segments.Min(s => s.DistanceTo(c)), 6);
        }

        [Fact]
        public void ClosureTails_ArePararallelAndPointIntoTheCore()
        {
            var r = TieGeometryBuilder.BuildHoop(Rect());
            var a = r.TailDirectionA.Value; var b = r.TailDirectionB.Value;
            Assert.Equal(1, a.Dot(b), 9);                             // classic 135° closure: parallel tails
            Assert.Equal(Math.Sqrt(0.5), a.X, 9);                     // pointing up-right from the bottom-left bar
            Assert.Equal(Math.Sqrt(0.5), a.Y, 9);
            var tailA = r.Path.Segments.First(); var tailB = r.Path.Segments.Last();
            foreach (var p in new[] { tailA.Start, tailB.End })
                Assert.True(p.X > 0 && p.X < 300 && p.Y > 0 && p.Y < 400, "tail end inside the section: " + p);
            Assert.Equal(100, tailA.ChordLength, 9);
            Assert.Equal(100, tailB.ChordLength, 9);
        }

        [Fact]
        public void IndependentHookAngles_AndClosureCornerChoice()
        {
            var input = Rect();
            input.AngleA = HookAngle.Deg90; input.TailLengthA = Ec2DraftingRules.TailLength(HookAngle.Deg90, 10);
            input.AngleB = HookAngle.Deg135;
            input.ClosureBar = 2;                                     // top-right bar
            var r = TieGeometryBuilder.BuildHoop(input);
            Assert.True(r.Validation.IsValid, r.Validation.ToString());
            Assert.Equal(2, r.HoopBars[0]);
            var arcs = r.Path.Segments.Where(s => s.IsArc).ToList();
            Assert.Equal(Math.PI / 2, Math.Abs(arcs.First().Sweep), 9);
            Assert.Equal(3 * Math.PI / 4, Math.Abs(arcs.Last().Sweep), 9);
        }

        [Fact]
        public void LargestBarGovernsTheBends_AndInteriorBarsAreNotWrapped()
        {
            var input = Rect(B(150, 200, 32));                        // interior bar, largest diameter
            var r = TieGeometryBuilder.BuildHoop(input);
            Assert.True(r.Validation.IsValid, r.Validation.ToString());
            Assert.Equal(32, r.ReferenceDiameter);
            Assert.Equal(1, r.InteriorBars);
            Assert.Equal(4, r.HoopBars.Count);
        }

        [Fact]
        public void Flip_ReversesTheLoopAndMirrorsTheClosure()
        {
            var a = TieGeometryBuilder.BuildHoop(Rect());
            var input = Rect(); input.Flip = true;
            var b = TieGeometryBuilder.BuildHoop(input);
            Assert.True(b.Validation.IsValid, b.Validation.ToString());
            Assert.Equal(a.Path.Length, b.Path.Length, 6);
            Assert.NotEqual(a.Path.Vertices[0].Point, b.Path.Vertices[0].Point);
            Assert.All(b.Path.Segments.Where(s => s.IsArc), s => Assert.True(s.Sweep < 0)); // clockwise
        }

        [Fact]
        public void InvalidHoops_AreRejected()
        {
            Assert.False(TieGeometryBuilder.BuildHoop(new HoopInput { Bars = new[] { B(0, 0), B(100, 0) } }).Validation.IsValid);
            Assert.False(TieGeometryBuilder.BuildHoop(new HoopInput { Bars = new[] { B(0, 0), B(100, 0), B(200, 0) } }).Validation.IsValid);
            var tooLong = Rect(); tooLong.TailLengthA = 900;          // tail would cross the opposite legs
            Assert.False(TieGeometryBuilder.BuildHoop(tooLong).Validation.IsValid);
            var tight = new HoopInput { Bars = new[] { B(0, 0), B(30, 0), B(30, 30), B(0, 30) }, InsideMandrelDiameter = 140, TieDiameter = 20 };
            Assert.False(TieGeometryBuilder.BuildHoop(tight).Validation.IsValid);
        }
    }
}
