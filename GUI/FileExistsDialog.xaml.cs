using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace GARbro.GUI
{
    /// <summary>
    /// Interaction logic for FileExistsDialog.xaml
    /// </summary>
    public partial class FileExistsDialog : Rnd.Windows.ModalWindow
    {
        public FileExistsDialog (string title, string text)
        {
            InitializeComponent ();
            this.Title = title;
            this.Notice.Text = text;
        }

        new public FileExistsDialogResult ShowDialog ()
        {
            bool dialog_result = base.ShowDialog() ?? false;
            if (!dialog_result)
                FileAction = ExistingFileAction.Abort;
            return new FileExistsDialogResult
            {
                Action      = FileAction,
                ApplyToAll  = ApplyToAll.IsChecked ?? false
            };
        }

        public ExistingFileAction FileAction { get; set; }

        private void SkipButton_Click (object sender, RoutedEventArgs e)
        {
            this.DialogResult = true;
            this.FileAction = ExistingFileAction.Skip;
        }

        private void OverwriteButton_Click (object sender, RoutedEventArgs e)
        {
            this.DialogResult = true;
            this.FileAction = ExistingFileAction.Overwrite;
        }

        private void RenameButton_Click (object sender, RoutedEventArgs e)
        {
            this.DialogResult = true;
            this.FileAction = ExistingFileAction.Rename;
        }

        private void AbortButton_Click (object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.FileAction = ExistingFileAction.Abort;
        }
    }

    public enum ExistingFileAction
    {
        Skip,
        Overwrite,
        Rename,
        Abort
    }

    public struct FileExistsDialogResult
    {
        public ExistingFileAction Action;
        public bool ApplyToAll;
    }
}
