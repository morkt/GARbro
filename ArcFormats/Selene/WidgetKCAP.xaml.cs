using System.Linq;
using System.Windows;
using System.Windows.Controls;
using GameRes.Formats.Properties;
using GameRes.Formats.Selene;
using GameRes.Formats.Strings;

namespace GameRes.Formats.GUI
{
    /// <summary>
    /// Interaction logic for WidgetKCAP.xaml
    /// </summary>
    public partial class WidgetKCAP : Grid
    {
        public WidgetKCAP ()
        {
            InitializeComponent ();
            var keys = new[] { arcStrings.ArcDefault };
            EncScheme.ItemsSource = keys.Concat (PackOpener.KnownSchemes.Keys);
            if (-1 == EncScheme.SelectedIndex)
                EncScheme.SelectedIndex = 0;
            EncScheme.SelectionChanged += OnSchemeChanged;
        }

        void OnSchemeChanged (object sender, SelectionChangedEventArgs e)
        {
            var widget = sender as ComboBox;
            var pass = PackOpener.GetPassPhrase (widget.SelectedItem as string);
            Passphrase.Text = pass;
            Settings.Default.KCAPPassPhrase = pass;
        }
    }
}
