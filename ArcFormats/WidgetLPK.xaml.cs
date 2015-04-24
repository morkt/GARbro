using System.Windows;
using System.Windows.Controls;
using GameRes.Formats.Properties;
using GameRes.Formats.Strings;

namespace GameRes.Formats.GUI
{
    /// <summary>
    /// Interaction logic for WidgetWARC.xaml
    /// </summary>
    public partial class WidgetLPK : Grid
    {
        public WidgetLPK ()
        {
            InitializeComponent();
            // select default scheme
            if (-1 == Scheme.SelectedIndex)
                Scheme.SelectedIndex = 0;
        }
    }
}
