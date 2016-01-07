using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
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
            Mask.ItemsSource = mask_list;
            Mask.Text = "*.*";
        }

        private void Button_Click (object sender, RoutedEventArgs e)
        {
            this.DialogResult = true;
        }
        
        private void Mask_Loaded (object sender, RoutedEventArgs e)
        {
            var text_box = (TextBox)Mask.Template.FindName ("PART_EditableTextBox", Mask);
            FocusManager.SetFocusedElement (this, text_box);
            text_box.SelectionStart = 2;
            text_box.SelectionLength = 1;
        }
    }
}
