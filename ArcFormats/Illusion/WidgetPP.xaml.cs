using System.Linq;
using System.Windows.Controls;
using GameRes.Formats.Illusion;

namespace GameRes.Formats.GUI
{
    /// <summary>
    /// Interaction logic for WidgetPP.xaml
    /// </summary>
    public partial class WidgetPP : StackPanel
    {
        public WidgetPP (PpOpener pp)
        {
            InitializeComponent();
            Scheme.ItemsSource = pp.KnownKeys.Keys.OrderBy (x => x);
            if (-1 == Scheme.SelectedIndex)
                Scheme.SelectedIndex = 0;
        }
    }
}
