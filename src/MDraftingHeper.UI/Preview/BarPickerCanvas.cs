using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using MDraftingHeper.UI.ViewModels;

namespace MDraftingHeper.UI.Preview
{
    public sealed class BarPickedEventArgs : EventArgs
    {
        public Guid BarId { get; }
        public BarPickedEventArgs(Guid id) { BarId = id; }
    }

    /// <summary>Longitudinal-only graphical picker used by the rectangular tie editor.</summary>
    public sealed class BarPickerCanvas : FrameworkElement
    {
        public IList<LongitudinalBarRow> Bars { get; set; }
        public Guid? BarAId { get; set; }
        public Guid? BarBId { get; set; }
        public event EventHandler<BarPickedEventArgs> BarPicked;
        private readonly List<Tuple<LongitudinalBarRow, Point, double>> _hits = new List<Tuple<LongitudinalBarRow, Point, double>>();

        public void Refresh() => InvalidateVisual();

        protected override void OnRender(DrawingContext dc)
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x21, 0x28, 0x30)), null, new Rect(0, 0, ActualWidth, ActualHeight));
            _hits.Clear();
            var bars = Bars == null ? new List<LongitudinalBarRow>() : Bars.ToList();
            if (bars.Count == 0) return;
            double minX = bars.Min(b => b.Bar.Center.X - b.Bar.Diameter), maxX = bars.Max(b => b.Bar.Center.X + b.Bar.Diameter);
            double minY = bars.Min(b => b.Bar.Center.Y - b.Bar.Diameter), maxY = bars.Max(b => b.Bar.Center.Y + b.Bar.Diameter);
            double w = Math.Max(1, maxX - minX), h = Math.Max(1, maxY - minY), margin = 24;
            double scale = Math.Min((ActualWidth - 2 * margin) / w, (ActualHeight - 2 * margin) / h);
            Point Map(LongitudinalBarRow b) => new Point(margin + (b.Bar.Center.X - minX) * scale,
                ActualHeight - margin - (b.Bar.Center.Y - minY) * scale);
            foreach (var b in bars)
            {
                var p = Map(b);
                double rr = Math.Max(5, b.Bar.Diameter / 2 * scale);
                bool isA = BarAId == b.BarId, isB = BarBId == b.BarId;
                var brush = isA ? Brushes.DeepSkyBlue : isB ? Brushes.Orange : Brushes.Gainsboro;
                dc.DrawEllipse(null, new Pen(brush, isA || isB ? 3 : 1.4), p, rr, rr);
                var label = (isA ? "A: " : isB ? "B: " : "") + b.BarName;
                var ft = new FormattedText(label, System.Globalization.CultureInfo.CurrentUICulture,
                    FlowDirection.LeftToRight, new Typeface("Segoe UI"), 10, brush,
                    VisualTreeHelper.GetDpi(this).PixelsPerDip);
                dc.DrawText(ft, new Point(p.X + rr + 3, p.Y - ft.Height / 2));
                _hits.Add(Tuple.Create(b, p, Math.Max(rr, 12)));
            }
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            var p = e.GetPosition(this);
            var hit = _hits.OrderBy(x => (x.Item2 - p).Length).FirstOrDefault();
            if (hit != null && (hit.Item2 - p).Length <= hit.Item3)
            {
                Focus();
                BarPicked?.Invoke(this, new BarPickedEventArgs(hit.Item1.BarId));
            }
        }
    }
}
