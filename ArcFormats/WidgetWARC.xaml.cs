using System.Windows;
using System.Windows.Controls;
using System.Linq;
using GameRes.Formats.KiriKiri;
using GameRes.Formats.Properties;
using GameRes.Formats.Strings;

namespace GameRes.Formats.GUI
{
    /// <summary>
    /// Interaction logic for WidgetWARC.xaml
    /// </summary>
    public partial class WidgetWARC : Grid
    {
        public WidgetWARC ()
        {
            InitializeComponent();
            // select the most recent scheme as default
            if (-1 == Scheme.SelectedIndex)
                Scheme.SelectedIndex = Scheme.ItemsSource.Cast<object>().Count()-1;
        }

        public ICrypt GetScheme ()
        {
            return Xp3Opener.GetScheme (Scheme.SelectedItem as string);
        }
    }
}
