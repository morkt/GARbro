using System.Windows.Controls;
using System.Linq;
using GameRes.Formats.NitroPlus;

namespace GameRes.Formats.GUI
{
    /// <summary>
    /// Interaction logic for WidgetNPK.xaml
    /// </summary>
    public partial class WidgetNPK : Grid
    {
        public WidgetNPK ()
        {
            InitializeComponent();
            Scheme.ItemsSource = NpkOpener.KnownKeys.Keys.OrderBy (x => x);
        }
    }
}
