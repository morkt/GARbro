using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;

namespace GameRes.Formats.GUI
{
    /// <summary>
    /// Interaction logic for WidgetSJDAT.xaml
    /// </summary>
    public partial class WidgetSJDAT : StackPanel
    {
        public WidgetSJDAT (IEnumerable<string> known_schemes)
        {
            InitializeComponent ();
            this.Title.ItemsSource = known_schemes.OrderBy (x => x);
        }
    }
}
