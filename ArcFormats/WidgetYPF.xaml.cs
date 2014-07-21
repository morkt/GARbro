using System.Windows;
using System.Windows.Controls;

namespace GameRes.Formats.GUI
{
    /// <summary>
    /// Interaction logic for WidgetYPF.xaml
    /// </summary>
    public partial class WidgetYPF : Grid
    {
        public WidgetYPF (uint? key)
        {
            InitializeComponent();
            if (null != key)
                this.Passkey.Text = key.Value.ToString();
            else
                this.Passkey.Text = null;
        }

        public uint? GetKey ()
        {
            uint key;
            if (uint.TryParse (this.Passkey.Text, out key))
                return key;
            else
                return null;
        }
    }
}
