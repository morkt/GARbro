using GameRes.Formats.Marble;
using GameRes.Formats.Strings;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;

namespace GameRes.Formats.GUI
{
    /// <summary>
    /// Interaction logic for WidgetMBL.xaml
    /// </summary>
    public partial class WidgetMBL : Grid
    {
        public WidgetMBL ()
        {
            InitializeComponent ();
            var keys = new[] { new KeyValuePair<string, string> (arcStrings.ArcDefault, "") };
            EncScheme.ItemsSource = keys.Concat (MblOpener.KnownKeys);
            if (-1 == EncScheme.SelectedIndex)
                EncScheme.SelectedIndex = 0;
        }
    }
}
