using System.Windows;
using System.Windows.Controls;

namespace GameRes.Formats.GUI
{
    /// <summary>
    /// Interaction logic for WidgetNPA.xaml
    /// </summary>
    public partial class WidgetNPA : Grid
    {
        public WidgetNPA (string scheme)
        {
            InitializeComponent();
            Scheme.ItemsSource = NpaOpener.KnownSchemes;
            Scheme.SelectedItem = scheme;
        }

        public string GetScheme()
        {
            return Scheme.SelectedItem as string;
        }
    }
}
