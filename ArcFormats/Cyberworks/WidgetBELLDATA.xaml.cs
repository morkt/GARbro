using System.Linq;
using System.Windows.Controls;
using GameRes.Formats.Cyberworks;
using GameRes.Formats.Strings;

namespace GameRes.Formats.GUI
{
    /// <summary>
    /// Interaction logic for WidgetBELLDATA.xaml
    /// </summary>
    public partial class WidgetBELLDATA : StackPanel
    {
        public WidgetBELLDATA()
        {
            InitializeComponent();
            var keys = new string[] { arcStrings.ArcIgnoreEncryption };
            Title.ItemsSource = keys.Concat (DataOpener.KnownSchemes.Keys.OrderBy (x => x));
            if (-1 == Title.SelectedIndex)
                Title.SelectedIndex = 0;
        }
    }
}
