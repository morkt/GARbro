using System.Linq;
using System.Windows;
using System.Windows.Controls;
using GameRes.Formats.AZSys;

namespace GameRes.Formats.GUI
{
    /// <summary>
    /// Interaction logic for WidgetAZ.xaml
    /// </summary>
    public partial class WidgetAZ : StackPanel
    {
        public WidgetAZ()
        {
            InitializeComponent();
            Scheme.ItemsSource = ArcOpener.KnownKeys.Keys.OrderBy (x => x);
        }
    }
}
