using System.Windows.Controls;

namespace GameRes.Formats.GUI
{
    /// <summary>
    /// Interaction logic for WidgetISF.xaml
    /// </summary>
    public partial class WidgetISF : StackPanel
    {
        public WidgetISF ()
        {
            InitializeComponent ();
            if (-1 == Scheme.SelectedIndex)
                Scheme.SelectedIndex = 0;
        }
    }
}