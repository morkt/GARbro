using System.Windows.Controls;
using GameRes.Formats.NitroPlus;

namespace GameRes.Formats.GUI
{
    /// <summary>
    /// Interaction logic for CreateNPAWidget.xaml
    /// </summary>
    public partial class CreateNPAWidget : Grid
    {
        public CreateNPAWidget ()
        {
            InitializeComponent ();
        }

        private void Reset_Click (object sender, System.Windows.RoutedEventArgs e)
        {
            this.EncryptionWidget.Scheme.SelectedIndex = 0;
            this.Key1Box.Text = NpaOpener.DefaultKey1.ToString ("X8");
            this.Key2Box.Text = NpaOpener.DefaultKey2.ToString ("X8");
            this.CompressContents.IsChecked = false;
        }
    }
}
