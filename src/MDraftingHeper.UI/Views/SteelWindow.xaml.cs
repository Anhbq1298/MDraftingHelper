using System.Windows;
using MDraftingHeper.UI.ViewModels;

namespace MDraftingHeper.UI.Views
{
    public partial class SteelWindow : Window
    {
        public SteelWindow(SteelViewModel vm)
        {
            InitializeComponent();
            DataContext = vm;
            vm.BeginDialog(); // a fresh round: closing with X must not repeat the previous action
            vm.RequestClose += Close;
            Closed += (s, e) => vm.RequestClose -= Close;
        }
    }
}
