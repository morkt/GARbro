using System.Windows.Controls;
using System.Linq;
using GameRes.Formats.Eagls;
using GameRes.Formats.Strings;

namespace GameRes.Formats.GUI
{
    /// <summary>
    /// Interaction logic for WidgetEAGLS.xaml
    /// </summary>
    public partial class WidgetEAGLS : StackPanel
    {
        public WidgetEAGLS ()
        {
            InitializeComponent ();
            var schemes = new string[] { arcStrings.ArcIgnoreEncryption };
            Scheme.ItemsSource = schemes.Concat (PakOpener.KnownSchemes.Keys);
            if (-1 == Scheme.SelectedIndex)
                Scheme.SelectedValue = PakOpener.KnownSchemes.First().Key;
        }
    }
}
