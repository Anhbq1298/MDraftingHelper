using System.Globalization;
using System.Windows;
using MDraftingHeper.UI.ViewModels;

namespace MDraftingHeper.UI.Views
{
    public partial class CustomBarWindow : Window
    {
        public string BarName { get; private set; }
        public double Diameter { get; private set; }
        public double X { get; private set; }
        public double Y { get; private set; }
        public CustomBarWindow(LongitudinalBarRow row)
        {
            InitializeComponent();
            if (row == null) { DiameterBox.Text = "20"; XBox.Text = "0"; YBox.Text = "0"; }
            else { NameBox.Text = row.BarName; DiameterBox.Text = row.Diameter; XBox.Text = row.X; YBox.Text = row.Y; }
        }
        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(NameBox.Text) || !double.TryParse(DiameterBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ||
                !double.TryParse(XBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var x) || !double.TryParse(YBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
            { ErrorText.Text = "Enter a name and numeric diameter, X and Y values."; return; }
            BarName = NameBox.Text.Trim(); Diameter = d; X = x; Y = y; DialogResult = true;
        }
    }
}
