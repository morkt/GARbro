using System.Linq;
using System.Windows.Controls;
using GameRes.Formats.Ags;
using GameRes.Formats.Strings;

namespace GameRes.Formats.GUI
{
    /// <summary>
    /// Interaction logic for WidgetAGS.xaml
    /// </summary>
    public partial class WidgetAGS : StackPanel
    {
        public WidgetAGS()
        {
            InitializeComponent();
            var keys = new string[] { arcStrings.ArcNoEncryption };
            Scheme.ItemsSource = keys.Concat (DatOpener.KnownSchemes.Keys.OrderBy (x => x));
            if (-1 == Scheme.SelectedIndex)
                Scheme.SelectedIndex = 0;
        }
    }
}
