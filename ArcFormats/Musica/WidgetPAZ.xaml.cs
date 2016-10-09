using System.Linq;
using System.Windows.Controls;
using GameRes.Formats.Musica;

namespace GameRes.Formats.GUI
{
    /// <summary>
    /// Interaction logic for WidgetPAZ.xaml
    /// </summary>
    public partial class WidgetPAZ : StackPanel
    {
        public WidgetPAZ (PazOpener paz)
        {
            InitializeComponent();
            Scheme.ItemsSource = paz.KnownTitles.Keys.OrderBy (x => x);
        }
    }
}
