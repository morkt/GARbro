using System.Windows;

namespace GARbro.GUI
{
    /// <summary>
    /// Interaction logic for ConvertImages.xaml
    /// </summary>
    public partial class ConvertImages : Window
    {
        public ConvertImages ()
        {
            InitializeComponent ();
        }

        private void ConvertButton_Click (object sender, RoutedEventArgs e)
        {
            this.DialogResult = true;
        }
    }
}
