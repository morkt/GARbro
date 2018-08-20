using System.Collections.Generic;
using System.Windows.Controls;

namespace GameRes.Formats.GUI
{
    /// <summary>
    /// Interaction logic for WidgetGYU.xaml
    /// </summary>
    public partial class WidgetGYU : StackPanel
    {
        public WidgetGYU (IEnumerable<string> titles)
        {
            InitializeComponent();
            Title.ItemsSource = titles;
        }
    }
}
