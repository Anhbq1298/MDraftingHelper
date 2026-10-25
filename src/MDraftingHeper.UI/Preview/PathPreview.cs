using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using MDraftingHeper.Core.Geometry;

namespace MDraftingHeper.UI.Preview
{
    public enum PreviewStyle { Section, Bar, Tie, Axis, Boundary, Flagged }

    /// <summary>A Core path to draw in a preview, with a role that sets its pen.</summary>
    public sealed class PreviewShape
    {
        public Path2 Path { get; }
        public PreviewStyle Style { get; }

        public PreviewShape(Path2 path, PreviewStyle style) { Path = path; Style = style; }
    }

    /// <summary>
    /// Draws Core geometry (the same Path2 objects used for CAD output) fitted to the control,
    /// Y axis up, on a dark model-space-like background.
    /// </summary>
    public sealed class PathPreview : FrameworkElement
    {
        public static readonly DependencyProperty ShapesProperty = DependencyProperty.Register(
            nameof(Shapes), typeof(IEnumerable<PreviewShape>), typeof(PathPreview),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty MessageProperty = DependencyProperty.Register(
            nameof(Message), typeof(string), typeof(PathPreview),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public IEnumerable<PreviewShape> Shapes
        {
            get => (IEnumerable<PreviewShape>)GetValue(ShapesProperty);
            set => SetValue(ShapesProperty, value);
        }

        public string Message
        {
            get => (string)GetValue(MessageProperty);
            set => SetValue(MessageProperty, value);
        }

        private static readonly Brush Background = Frozen(new SolidColorBrush(Color.FromRgb(0x21, 0x28, 0x30)));
        private static readonly Pen SectionPen = FrozenPen(Color.FromRgb(0x6F, 0xC3, 0xFF), 1.6);
        private static readonly Pen BarPen = FrozenPen(Color.FromRgb(0xE8, 0xE8, 0xE8), 1.2);
        private static readonly Pen TiePen = FrozenPen(Color.FromRgb(0xFF, 0xC8, 0x3D), 1.8);
        private static readonly Pen AxisPen = FrozenPen(Color.FromRgb(0x9A, 0x9A, 0x9A), 0.8, dashed: true);
        private static readonly Pen BoundaryPen = FrozenPen(Color.FromRgb(0x7C, 0xD6, 0x8A), 1.4);
        private static readonly Pen FlaggedPen = FrozenPen(Color.FromRgb(0xFF, 0x5C, 0x5C), 1.6);

        protected override void OnRender(DrawingContext dc)
        {
            var rect = new Rect(0, 0, ActualWidth, ActualHeight);
            dc.DrawRectangle(Background, null, rect);
            var shapes = Shapes?.Where(s => s?.Path != null).ToList();
            if (shapes == null || shapes.Count == 0)
            {
                DrawMessage(dc, Message ?? "No preview");
                return;
            }

            var sampled = shapes.Select(s => (s, pts: Sample(s.Path))).ToList();
            var all = sampled.SelectMany(x => x.pts).ToList();
            double minX = all.Min(p => p.X), maxX = all.Max(p => p.X), minY = all.Min(p => p.Y), maxY = all.Max(p => p.Y);
            double w = Math.Max(maxX - minX, 1e-6), h = Math.Max(maxY - minY, 1e-6);
            const double margin = 14;
            double scale = Math.Min((ActualWidth - 2 * margin) / w, (ActualHeight - 2 * margin) / h);
            if (!(scale > 0)) return;
            double ox = (ActualWidth - w * scale) / 2, oy = (ActualHeight - h * scale) / 2;
            Point Map(Point2 p) => new Point(ox + (p.X - minX) * scale, ActualHeight - (oy + (p.Y - minY) * scale));

            foreach (var (shape, pts) in sampled)
            {
                var g = new StreamGeometry();
                using (var ctx = g.Open())
                {
                    ctx.BeginFigure(Map(pts[0]), false, shape.Path.IsClosed);
                    ctx.PolyLineTo(pts.Skip(1).Select(Map).ToList(), true, true);
                }
                g.Freeze();
                dc.DrawGeometry(null, PenFor(shape.Style), g);
            }
        }

        private static List<Point2> Sample(Path2 p)
        {
            var pts = p.Sample(Math.PI / 60);
            if (p.IsClosed && pts.Count > 0) pts.Add(pts[0]);
            return pts;
        }

        /// <summary>Shown only when there is nothing to draw.</summary>
        private void DrawMessage(DrawingContext dc, string text)
        {
            var ft = new FormattedText(text, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), 11, Brushes.Gainsboro, VisualTreeHelper.GetDpi(this).PixelsPerDip)
            { MaxTextWidth = Math.Max(10, ActualWidth - 16) };
            dc.DrawText(ft, new Point(8, (ActualHeight - ft.Height) / 2));
        }

        private static Pen PenFor(PreviewStyle s)
        {
            switch (s)
            {
                case PreviewStyle.Bar: return BarPen;
                case PreviewStyle.Tie: return TiePen;
                case PreviewStyle.Axis: return AxisPen;
                case PreviewStyle.Boundary: return BoundaryPen;
                case PreviewStyle.Flagged: return FlaggedPen;
                default: return SectionPen;
            }
        }

        private static Brush Frozen(Brush b) { b.Freeze(); return b; }

        private static Pen FrozenPen(Color c, double w, bool dashed = false)
        {
            var p = new Pen(new SolidColorBrush(c), w) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            if (dashed) p.DashStyle = new DashStyle(new double[] { 6, 4 }, 0);
            p.Freeze();
            return p;
        }
    }

    public static class PreviewShapes
    {
        public static Path2 Circle(Point2 c, double r)
        {
            return new Path2(new[] { new PathVertex(new Point2(c.X + r, c.Y), 1), new PathVertex(new Point2(c.X - r, c.Y), 1) }, true);
        }

        public static Path2 Line(Point2 a, Point2 b) => new Path2(new[] { new PathVertex(a), new PathVertex(b) }, false);
    }
}
