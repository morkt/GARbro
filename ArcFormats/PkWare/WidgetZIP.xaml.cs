using System.Collections;
using System.Windows.Controls;

namespace GameRes.Formats.GUI
{
    /// <summary>
    /// Interaction logic for WidgetZIP.xaml
    /// </summary>
    public partial class WidgetZIP : StackPanel
    {
        public WidgetZIP (IEnumerable titles)
        {
            InitializeComponent();
            this.DataContext = titles;
        }
    }
}
