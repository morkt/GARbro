using System.Collections.Generic;
using System.Windows.Controls;

namespace GameRes.Formats.GUI
{
    /// <summary>
    /// Interaction logic for WidgetPCK.xaml
    /// </summary>
    public partial class WidgetPCK : StackPanel
    {
        public WidgetPCK (IEnumerable<string> keys)
        {
            InitializeComponent ();
            Title.ItemsSource = keys;
        }
    }
}
