using System.Linq;
using System.Windows.Controls;
using GameRes.Formats.Mg;
using GameRes.Formats.Strings;

namespace GameRes.Formats.GUI
{
    /// <summary>
    /// Interaction logic for WidgetMGPK.xaml
    /// </summary>
    public partial class WidgetMGPK : StackPanel
    {
        public WidgetMGPK()
        {
            InitializeComponent();
            var keys = new string[] { arcStrings.ArcNoEncryption };
            Title.ItemsSource = keys.Concat (MgpkOpener.KnownKeys.Keys.OrderBy (x => x));
            if (-1 == Title.SelectedIndex)
                Title.SelectedIndex = 0;
        }
    }
}
