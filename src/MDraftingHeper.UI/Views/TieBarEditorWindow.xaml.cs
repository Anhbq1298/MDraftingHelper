using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using MDraftingHeper.Core.Hooks;
using MDraftingHeper.Core.Rectangular;
using MDraftingHeper.UI.Preview;
using MDraftingHeper.UI.ViewModels;

namespace MDraftingHeper.UI.Views
{
    public partial class TieBarEditorWindow : Window
    {
        private readonly IList<LongitudinalBarRow> _bars;
        private bool _loading;
        public bool IsHoop { get; }
        public Guid? BarAId => (BarABox.SelectedItem as LongitudinalBarRow)?.BarId;
        public Guid? BarBId => (BarBBox.SelectedItem as LongitudinalBarRow)?.BarId;
        public string SizeName => (SizeBox.SelectedItem as TieBarSize)?.Name;
        public HookAngle HookA => (HookAngle)HookABox.SelectedItem;
        public HookAngle HookB => (HookAngle)HookBBox.SelectedItem;
        public bool Flip => FlipBox.IsChecked == true;
        public double Mandrel { get; private set; }
        public double Tail { get; private set; }
        public double Gap { get; private set; }

        public TieBarEditorWindow(IList<LongitudinalBarRow> bars, TieBar tie)
        {
            InitializeComponent();
            _loading = true; _bars = bars; IsHoop = tie != null && tie.Kind != RectangularTieKind.CrossTie;
            BarABox.ItemsSource = bars; BarBBox.ItemsSource = bars; Picker.Bars = bars;
            SizeBox.ItemsSource = TieBarSize.Standard; HookABox.ItemsSource = Enum.GetValues(typeof(HookAngle)); HookBBox.ItemsSource = Enum.GetValues(typeof(HookAngle));
            var size = TieBarSize.Find(tie?.TieSizeName) ?? TieBarSize.Standard[0];
            SizeBox.SelectedItem = size; HookABox.SelectedItem = tie?.HookA ?? HookAngle.Deg135; HookBBox.SelectedItem = tie?.HookB ?? HookAngle.Deg135;
            FlipBox.IsChecked = tie?.Flip ?? false; MandrelBox.Text = (tie?.InsideMandrelDiameter ?? 20).ToString(CultureInfo.InvariantCulture);
            TailBox.Text = (tie?.TailLength ?? 100).ToString(CultureInfo.InvariantCulture); GapBox.Text = (tie?.ClearGap ?? 0).ToString(CultureInfo.InvariantCulture);
            if (tie?.BarAId != null) BarABox.SelectedItem = bars.FirstOrDefault(b => b.BarId == tie.BarAId.Value);
            if (tie?.BarBId != null) BarBBox.SelectedItem = bars.FirstOrDefault(b => b.BarId == tie.BarBId.Value);
            if (IsHoop)
            {
                BarABox.IsEnabled = BarBBox.IsEnabled = Picker.IsHitTestVisible = false;
                PickerHelp.Text = "Outer hoop support bars are fixed: " + string.Join(", ", tie.OrderedSupportBarIds.Select(id => bars.FirstOrDefault(b => b.BarId == id)?.BarName ?? "[missing]")) + ".";
            }
            Picker.BarPicked += Picker_BarPicked;
            _loading = false; SyncPicker();
        }

        private void Picker_BarPicked(object sender, BarPickedEventArgs e)
        {
            var row = _bars.First(b => b.BarId == e.BarId);
            if (BarABox.SelectedItem == null || (BarAId.HasValue && BarBId.HasValue)) { BarABox.SelectedItem = row; BarBBox.SelectedItem = null; }
            else if (BarAId != e.BarId) BarBBox.SelectedItem = row;
            else ErrorText.Text = "Circle B must be different from Circle A.";
            SyncPicker();
        }

        private void BarBox_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (!_loading) SyncPicker(); }

        private void SyncPicker()
        {
            Picker.BarAId = BarAId; Picker.BarBId = BarBId; Picker.Refresh();
            string Describe(LongitudinalBarRow b) => b == null ? "—" : b.BarName + ": X " + b.X + ", Y " + b.Y + ", Ø" + b.Diameter;
            InspectionText.Text = "A  " + Describe(BarABox.SelectedItem as LongitudinalBarRow) + "\nB  " + Describe(BarBBox.SelectedItem as LongitudinalBarRow);
        }

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            ErrorText.Text = "";
            if (!IsHoop && (!BarAId.HasValue || !BarBId.HasValue || BarAId == BarBId)) { ErrorText.Text = "Choose two different current longitudinal bars."; return; }
            if (SizeBox.SelectedItem == null || HookABox.SelectedItem == null || HookBBox.SelectedItem == null ||
                !double.TryParse(MandrelBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var m) ||
                !double.TryParse(TailBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var t) ||
                !double.TryParse(GapBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var g))
            { ErrorText.Text = "Complete the tie properties using numeric mandrel, tail and gap values."; return; }
            Mandrel = m; Tail = t; Gap = g; DialogResult = true;
        }
    }
}
