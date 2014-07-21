using System.Windows;
using System.Windows.Controls;
using GameRes.Formats.KiriKiri;

namespace GameRes.Formats.GUI
{
    /// <summary>
    /// Interaction logic for WidgetXP3.xaml
    /// </summary>
    public partial class WidgetXP3 : Grid
    {
        public WidgetXP3 (string scheme)
        {
            InitializeComponent();
            Scheme.ItemsSource = Xp3Opener.KnownSchemes.Keys;
            Scheme.SelectedItem = scheme;
        }

        public string GetScheme ()
        {
            return Scheme.SelectedItem as string;
        }
    }
}
