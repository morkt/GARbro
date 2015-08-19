using System.Windows;
using System.Windows.Controls;
using System.Linq;
using GameRes.Formats.Properties;
using GameRes.Formats.NitroPlus;

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
            var sorted = NpaOpener.KnownSchemes.Skip (1).OrderBy (x => x);
            Scheme.ItemsSource = NpaOpener.KnownSchemes.Take(1).Concat (sorted);
            if (NpaTitleId.NotEncrypted == NpaOpener.GetTitleId (selected))
                Scheme.SelectedIndex = 0;
            else
                Scheme.SelectedValue = selected;
        }
    }
}
