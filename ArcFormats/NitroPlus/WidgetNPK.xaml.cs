using System.Collections.Generic;
using System.Windows.Controls;
using System.Linq;

namespace GameRes.Formats.GUI
{
    /// <summary>
    /// Interaction logic for WidgetNPK.xaml
    /// </summary>
    public partial class WidgetNPK : Grid
    {
        public WidgetNPK (IEnumerable<string> titles)
        {
            InitializeComponent();
            Scheme.ItemsSource = titles.OrderBy (x => x);
        }
    }
}
