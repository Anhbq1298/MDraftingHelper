using System.Windows;
using MDraftingHeper.UI.ViewModels;

namespace MDraftingHeper.UI.Views
{
    public partial class TieWindow : Window
    {
        public TieWindow(TieViewModel vm)
        {
            InitializeComponent();
            DataContext = vm;
            vm.RequestClose += Close;
            Closed += (s, e) => vm.RequestClose -= Close;
        }
    }
}
