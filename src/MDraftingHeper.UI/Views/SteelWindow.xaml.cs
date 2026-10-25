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
            vm.RequestClose += Close;
            Closed += (s, e) => vm.RequestClose -= Close;
        }
    }
}
