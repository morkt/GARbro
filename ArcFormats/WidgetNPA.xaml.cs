using System.Windows;
using System.Windows.Controls;
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
            Scheme.SelectedItem = NpaOpener.KnownSchemes[(int)Settings.Default.NPAScheme];
        }

        public string GetScheme()
        {
            return Scheme.SelectedItem as string;
        }
    }
}
