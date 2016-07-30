using System.Collections.Generic;
using System.Windows.Controls;
using GameRes.Formats.Properties;

namespace GameRes.Formats.GUI
{
    /// <summary>
    /// Interaction logic for NSAWidget.xaml
    /// </summary>
    public partial class WidgetNSA : Grid
    {
        public WidgetNSA (IDictionary<string, string> known_keys)
        {
            InitializeComponent ();
            this.Title.ItemsSource = known_keys;
            this.Password.Text = Settings.Default.NSAPassword;
            if (null != this.Title.SelectedItem)
            {
                var selected = (KeyValuePair<string, string>)this.Title.SelectedItem;
                if (Settings.Default.NSAPassword != selected.Value)
                    this.Title.SelectedIndex = -1;
            }
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
