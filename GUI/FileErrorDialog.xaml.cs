using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace GARbro.GUI
{
    /// <summary>
    /// Interaction logic for FileErrorDialog.xaml
    /// </summary>
    public partial class FileErrorDialog : Rnd.Windows.ModalWindow
    {
        public FileErrorDialog (string title, string error_text)
        {
            InitializeComponent();
            this.DataContext = new ViewModel { Title = title, Text = error_text };
        }

        private void ContinueButton_Click (object sender, RoutedEventArgs e)
        {
            this.DialogResult = true;
        }

        private void AbortButton_Click (object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
        }

        private class ViewModel
        {
            public string         Title { get; set; }
            public string          Text { get; set; }
            public ICommand CopyCommand { get; private set; }

            public ViewModel ()
            {
                CopyCommand = new ActionCommand (CopyText);
            }

            private void CopyText ()
            {
                try
                {
                    Clipboard.SetText (Text);
                }
                catch (Exception X)
                {
                    System.Diagnostics.Trace.WriteLine (X.Message, "Clipboard error");
                }
            }
        }

        private class ActionCommand : ICommand
        {
            readonly Action     m_action;

            public ActionCommand (Action action)
            {
                m_action = action;
            }

            public void Execute (object parameter)
            {
                m_action();
            }

            public bool CanExecute (object parameter)
            {
                return true;
            }

            #pragma warning disable 67
            public event EventHandler CanExecuteChanged;
        }
    }
}
