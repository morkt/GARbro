using System.Collections.Generic;
using System.Windows.Controls;
using GameRes.Formats.Properties;

namespace GameRes.Formats.GUI
{
    /// <summary>
    /// Interaction logic for WidgetRCT.xaml
    /// </summary>
    public partial class WidgetRCT : Grid
    {
        public WidgetRCT ()
        {
            InitializeComponent ();
            this.Password.Text = Settings.Default.RCTPassword;
            if (null != this.Title.SelectedItem)
            {
                var selected = (KeyValuePair<string, string>)this.Title.SelectedItem;
                if (Settings.Default.RCTPassword != selected.Value)
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
