using System.Linq;
using System.Windows;
using System.Windows.Controls;
using GameRes.Formats.KiriKiri;
using GameRes.Formats.Properties;
using GameRes.Formats.Strings;

namespace GameRes.Formats.GUI
{
    /// <summary>
    /// Interaction logic for WidgetXP3.xaml
    /// </summary>
    public partial class WidgetXP3 : Grid
    {
        public WidgetXP3 ()
        {
            InitializeComponent();
            var keys = new string[] { arcStrings.ArcNoEncryption };
            Scheme.ItemsSource = keys.Concat (Xp3Opener.KnownSchemes.Keys.OrderBy (x => x));
            if (-1 == Scheme.SelectedIndex)
                Scheme.SelectedIndex = 0;
        }

        public ICrypt GetScheme ()
        {
            return Xp3Opener.GetScheme (Scheme.SelectedItem as string);
        }
    }
}
