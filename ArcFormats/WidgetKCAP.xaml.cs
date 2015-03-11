using System.Windows;
using System.Windows.Controls;
using GameRes.Formats.Properties;
using GameRes.Formats.Selene;

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
            EncScheme.SelectionChanged += OnSchemeChanged;

            if (null == EncScheme.SelectedItem)
                EncScheme.SelectedIndex = 0;
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
