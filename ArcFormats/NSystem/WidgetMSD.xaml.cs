using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;
using GameRes.Formats.NSystem;
using GameRes.Formats.Properties;
using GameRes.Formats.Strings;

namespace GameRes.Formats.GUI
{
    /// <summary>
    /// Interaction logic for WidgetMSD.xaml
    /// </summary>
    public partial class WidgetMSD : StackPanel
    {
        public WidgetMSD ()
        {
            InitializeComponent ();
            var first = new Dictionary<string, string> { { arcStrings.ArcNoEncryption, "" } };
            Title.ItemsSource = first.Concat (FjsysOpener.KnownPasswords.OrderBy (x => x.Key));
            Password.Text = Settings.Default.FJSYSPassword;
        }

        private void Title_SelectionChanged (object sender, SelectionChangedEventArgs e)
        {
            if (null != this.Title.SelectedItem && null != this.Password)
            {
                var selected = (KeyValuePair<string, string>)this.Title.SelectedItem;
                this.Password.Text = selected.Value;
            }
        }
    }
}
