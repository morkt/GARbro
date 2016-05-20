using System.Windows.Controls;
using System.Linq;
using GameRes.Formats.NonColor;

namespace GameRes.Formats.GUI
{
    /// <summary>
    /// Interaction logic for WidgetNCARC.xaml
    /// </summary>
    public partial class WidgetNCARC : StackPanel
    {
        public WidgetNCARC()
        {
            InitializeComponent();
            Scheme.ItemsSource = DatOpener.KnownSchemes.OrderBy (x => x.Key);
        }
    }
}
