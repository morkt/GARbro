using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;

namespace GARbro.GUI
{
    /// <summary>
    /// Interaction logic for EnterMaskDialog.xaml
    /// </summary>
    public partial class EnterMaskDialog : Window
    {
        public EnterMaskDialog (IEnumerable<string> mask_list)
        {
            InitializeComponent ();
//            Mask.ItemsSource = mask_list;
            Mask.Text = "*.*";
            Mask.SelectionStart = 2;
            Mask.SelectionLength = 1;
            FocusManager.SetFocusedElement (this, Mask);
        }

        private void Button_Click (object sender, RoutedEventArgs e)
        {
            this.DialogResult = true;
        }
    }
}
