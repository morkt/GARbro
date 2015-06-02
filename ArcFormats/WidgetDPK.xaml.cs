using System.Windows.Controls;
using GameRes.Formats.Dac;
using GameRes.Formats.Properties;

namespace GameRes.Formats.GUI
{
    /// <summary>
    /// Interaction logic for WidgetDPK.xaml
    /// </summary>
    public partial class WidgetDPK : Grid
    {
        public WidgetDPK ()
        {
            InitializeComponent ();
            var last_scheme = EncScheme.SelectedItem as DpkScheme;
            if (null == last_scheme)
                last_scheme = DpkOpener.KnownSchemes[0];
            uint key1 = Settings.Default.DPKKey1;
            uint key2 = Settings.Default.DPKKey2; 
            if (last_scheme.Key1 != key1 || last_scheme.Key2 != key2)
                EncScheme.SelectedIndex = -1;
            Key1.Text = key1.ToString ("X");
            Key2.Text = key2.ToString ("X8");

            EncScheme.SelectionChanged += OnSchemeChanged;
        }

        void OnSchemeChanged (object sender, SelectionChangedEventArgs e)
        {
            var widget = sender as ComboBox;
            var scheme = widget.SelectedItem as DpkScheme;
            if (null != scheme)
            {
                Key1.Text = scheme.Key1.ToString ("X");
                Key2.Text = scheme.Key2.ToString ("X8");
            }
        }
    }
}
