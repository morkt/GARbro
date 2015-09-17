using GameRes.Formats.Ikura;
using GameRes.Formats.Strings;
using System.Linq;
using System.Windows.Controls;

namespace GameRes.Formats.GUI
{
    /// <summary>
    /// Interaction logic for WidgetISF.xaml
    /// </summary>
    public partial class WidgetISF : StackPanel
    {
        public WidgetISF ()
        {
            InitializeComponent ();
            var keys = new string[] { arcStrings.ISFIgnoreEncryption};
            Scheme.ItemsSource = keys.Concat (MpxOpener.KnownSecrets.Keys.OrderBy (x => x));
            if (-1 == Scheme.SelectedIndex)
                Scheme.SelectedIndex = 0;
        }
    }
}
