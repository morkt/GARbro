using System.Windows;
using System.Windows.Controls;
using System.Linq;
using GameRes.Formats.Properties;

namespace GameRes.Formats.GUI
{
    /// <summary>
    /// Interaction logic for WidgetNPA.xaml
    /// </summary>
    public partial class WidgetNPA : Grid
    {
        public WidgetNPA ()
        {
            InitializeComponent();
            var sorted = NpaOpener.KnownSchemes.Skip (1).OrderBy (x => x);
            Scheme.ItemsSource = NpaOpener.KnownSchemes.Take(1).Concat (sorted);
            Scheme.SelectedItem = NpaOpener.KnownSchemes[(int)Settings.Default.NPAScheme];
        }

        public string GetScheme()
        {
            return Scheme.SelectedItem as string;
        }
    }
}
