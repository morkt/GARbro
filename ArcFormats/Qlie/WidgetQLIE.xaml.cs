using GameRes.Formats.Qlie;
using GameRes.Formats.Strings;
using System.Windows.Controls;
using System.Linq;

namespace GameRes.Formats.GUI
{
    /// <summary>
    /// Interaction logic for WidgetQLIE.xaml
    /// </summary>
    public partial class WidgetQLIE : StackPanel
    {
        public WidgetQLIE ()
        {
            InitializeComponent ();
            var keys = new string[] { arcStrings.QLIEDefaultScheme };
            Scheme.ItemsSource = keys.Concat (PackOpener.KnownKeys.Keys.OrderBy (x => x));
            if (-1 == Scheme.SelectedIndex)
                Scheme.SelectedIndex = 0;
        }
    }
}
