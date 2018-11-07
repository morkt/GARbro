using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;

namespace GameRes.Formats.GUI
{
    /// <summary>
    /// Interaction logic for WidgetLEAF.xaml
    /// </summary>
    public partial class WidgetLEAF : StackPanel
    {
        public WidgetLEAF (IEnumerable<string> titles)
        {
            this.InitializeComponent();
            this.DataContext = titles.OrderBy (x => x);
        }
    }
}
