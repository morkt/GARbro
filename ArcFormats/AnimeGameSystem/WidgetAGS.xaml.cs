using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;
using GameRes.Formats.Ags;
using GameRes.Formats.Strings;

namespace GameRes.Formats.GUI
{
    /// <summary>
    /// Interaction logic for WidgetAGS.xaml
    /// </summary>
    public partial class WidgetAGS : StackPanel
    {
        public WidgetAGS (IEnumerable<string> known_titles)
        {
            InitializeComponent();
            var keys = new string[] { arcStrings.ArcNoEncryption };
            Scheme.ItemsSource = keys.Concat (known_titles.OrderBy (x => x));
            if (-1 == Scheme.SelectedIndex)
                Scheme.SelectedIndex = 0;
        }
    }
}
