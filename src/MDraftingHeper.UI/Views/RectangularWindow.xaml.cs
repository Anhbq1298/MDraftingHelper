using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MDraftingHeper.UI.ViewModels;

namespace MDraftingHeper.UI.Views
{
    public partial class RectangularWindow : Window
    {
        private readonly RectangularViewModel _vm;
        private bool _pickerOpen;
        private Guid? _lastAutoOpened;

        public RectangularWindow(RectangularViewModel vm)
        {
            InitializeComponent();
            _vm = vm; DataContext = vm; vm.BeginDialog();
            vm.RequestClose += Close;
            Closed += (s, e) => vm.RequestClose -= Close;
        }

        private void AddBar_Click(object sender, RoutedEventArgs e)
        {
            if (!_vm.IsCustomMode) return;
            var d = new CustomBarWindow(null) { Owner = this };
            if (d.ShowDialog() == true) ShowValidation(_vm.AddCustomBar(d.BarName, d.Diameter, d.X, d.Y));
        }

        private void EditBar_Click(object sender, RoutedEventArgs e)
        {
            if (!_vm.IsCustomMode || _vm.SelectedBar == null) return;
            var d = new CustomBarWindow(_vm.SelectedBar) { Owner = this };
            if (d.ShowDialog() == true) ShowValidation(_vm.EditCustomBar(_vm.SelectedBar.BarId, d.BarName, d.Diameter, d.X, d.Y));
        }

        private void AddTie_Click(object sender, RoutedEventArgs e) => OpenTieEditor(null);
        private void EditTie_Click(object sender, RoutedEventArgs e) => OpenTieEditor(_vm.SelectedTie);

        private void TieGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var row = _vm.SelectedTie;
            if (row == null || _pickerOpen || _lastAutoOpened == row.Tie.TieId) return;
            if (!TieGrid.IsKeyboardFocusWithin && !TieGrid.IsMouseOver) return;
            _lastAutoOpened = row.Tie.TieId;
            Dispatcher.BeginInvoke(new Action(() => OpenTieEditor(row)));
        }

        private void OpenTieEditor(TieBarRow row)
        {
            if (_pickerOpen || (row == null && _vm.Bars.Count < 2)) return;
            _pickerOpen = true;
            try
            {
                var d = new TieBarEditorWindow(_vm.Bars, row?.Tie, _vm.Presets) { Owner = this };
                if (d.ShowDialog() != true) return;
                if (d.IsHoop) ShowValidation(_vm.CommitHoopProperties(row.Tie, d.SizeName, d.HookA, d.HookB, d.Flip, d.PresetId, d.Mandrel, d.TailA, d.TailB, d.Gap));
                else ShowValidation(_vm.CommitCrossTie(row?.Tie, d.BarAId.Value, d.BarBId.Value, d.SizeName, d.HookA, d.HookB, d.Flip, d.PresetId, d.Mandrel, d.TailA, d.TailB, d.Gap));
            }
            finally { _pickerOpen = false; }
        }

        private static void ShowValidation(Core.Validation.ValidationResult v)
        {
            if (!v.IsValid) MessageBox.Show(v.Summary, "MDraftingHeper", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
