using System.Windows.Controls;

namespace GameRes.Formats.GUI
{
    /// <summary>
    /// Interaction logic for WidgetNOA.xaml
    /// </summary>
    public partial class WidgetNOA : Grid
    {
        public WidgetNOA ()
        {
            InitializeComponent ();
            // select first scheme as default
            if (-1 == Scheme.SelectedIndex)
                Scheme.SelectedIndex = 0;
        }
    }
}
