using System.Windows.Controls;

namespace GameRes.Formats.GUI
{
    /// <summary>
    /// Interaction logic for WidgetDPK.xaml
    /// </summary>
    public partial class WidgetDPK : Grid
    {
        public WidgetDPK ()
        {
            InitializeComponent ();
            if (null == EncScheme.SelectedItem)
                EncScheme.SelectedIndex = 0;
        }
    }
}
