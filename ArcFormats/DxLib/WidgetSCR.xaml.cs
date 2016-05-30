using System.Windows.Controls;
using System.Linq;
using GameRes.Formats.DxLib;
using GameRes.Formats.Strings;

namespace GameRes.Formats.GUI
{
    /// <summary>
    /// Interaction logic for WidgetSCR.xaml
    /// </summary>
    public partial class WidgetSCR : StackPanel
    {
        public WidgetSCR()
        {
            InitializeComponent();
            var keys = new string[] { arcStrings.ArcIgnoreEncryption };
            ScriptScheme.ItemsSource = keys.Concat (MedOpener.KnownSchemes.Keys.OrderBy (x => x));
            if (-1 == ScriptScheme.SelectedIndex)
                ScriptScheme.SelectedIndex = 0;
        }
    }
}
