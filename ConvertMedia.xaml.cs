using System.Windows;

namespace GARbro.GUI
{
    /// <summary>
    /// Interaction logic for ConvertMedia.xaml
    /// </summary>
    public partial class ConvertMedia : Window
    {
        public ConvertMedia ()
        {
            InitializeComponent ();
        }

        private void ConvertButton_Click (object sender, RoutedEventArgs e)
        {
            this.DialogResult = true;
        }
    }
}
