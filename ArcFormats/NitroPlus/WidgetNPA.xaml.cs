using System.Windows.Controls;
using System.Linq;
using GameRes.Formats.Properties;
using GameRes.Formats.NitroPlus;
using GameRes.Formats.Strings;

namespace GameRes.Formats.GUI
{
    /// <summary>
    /// Interaction logic for WidgetNPA.xaml
    /// </summary>
    public partial class WidgetNPA : Grid
    {
        public WidgetNPA ()
        {
            var selected = Settings.Default.NPAScheme;
            InitializeComponent();
            var keys = new string[] { arcStrings.ArcNoEncryption };
            Scheme.ItemsSource = keys.Concat (NpaOpener.KnownSchemes.Keys.OrderBy (x => x));
            if (NpaTitleId.NotEncrypted == NpaOpener.GetTitleId (selected))
                Scheme.SelectedIndex = 0;
            else
                Scheme.SelectedValue = selected;
        }
    }
}
