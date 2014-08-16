using System.Windows;
using System.Windows.Controls;
using GameRes.Formats.Properties;

namespace GameRes.Formats.GUI
{
    /// <summary>
    /// Interaction logic for WidgetYPF.xaml
    /// </summary>
    public partial class WidgetYPF : Grid
    {
        public WidgetYPF ()
        {
            InitializeComponent();
        }

        public uint? GetKey ()
        {
            uint key;
            if (uint.TryParse (this.Passkey.Text, out key) && key < 0x100)
                return key;
            else
                return null;
        }
    }
}
