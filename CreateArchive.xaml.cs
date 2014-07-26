using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Text;
using System.Linq;
using Microsoft.Win32;
using GARbro.GUI.Strings;
using GameRes;

namespace GARbro.GUI
{
    /// <summary>
    /// Interaction logic for CreateArchive.xaml
    /// </summary>
    public partial class CreateArchiveDialog : Window
    {
        public CreateArchiveDialog (string initial_name = "")
        {
            InitializeComponent ();

            if (!string.IsNullOrEmpty (initial_name))
            {
                var format = this.ArchiveFormat.SelectedItem as ArchiveFormat;
                if (null != format)
                    ArchiveName.Text = Path.ChangeExtension (initial_name, format.Extensions.First());
            }
        }

        public ResourceOptions ArchiveOptions { get; private set; }

        void Button_Click (object sender, RoutedEventArgs e)
        {
            string arc_name = Path.GetFullPath (ArchiveName.Text);
            if (File.Exists (arc_name))
            {
                string text = string.Format (guiStrings.MsgOverwrite, arc_name);
                var rc = MessageBox.Show (this, text, guiStrings.TextConfirmOverwrite, MessageBoxButton.YesNo,
                                          MessageBoxImage.Question);
                if (MessageBoxResult.Yes != rc)
                    return;
            }
            DialogResult = true;
        }

        void BrowseExec (object sender, ExecutedRoutedEventArgs e)
        {
            string file = ChooseFile (guiStrings.TextChooseArchive, ArchiveName.Text);
            if (!string.IsNullOrEmpty (file))
                ArchiveName.Text = file;
        }

        string GetFilters ()
        {
            var filters = new StringBuilder();

            var format = this.ArchiveFormat.SelectedItem as ArchiveFormat;
            if (null != format)
            {
                var patterns = format.Extensions.Select (ext => "*."+ext);
                filters.Append (format.Description);
                filters.Append (" (");
                filters.Append (string.Join (", ", patterns));
                filters.Append (")|");
                filters.Append (string.Join (";", patterns));
            }

            if (filters.Length > 0)
                filters.Append ('|');
            filters.Append (string.Format ("{0} (*.*)|*.*", guiStrings.TextAllFiles));
            return filters.ToString();
        }

        public string ChooseFile (string title, string initial)
        {
            string dir = ".";
            if (!string.IsNullOrEmpty (initial))
            {
                var parent = Directory.GetParent (initial);
                if (null != parent)
                    dir = parent.FullName;
            }
            dir = Path.GetFullPath (dir);
            var dlg = new OpenFileDialog {
                AddExtension = true,
                CheckFileExists = false,
                CheckPathExists = true,
                FileName = initial,
                Filter = GetFilters(),
                InitialDirectory = dir,
                Multiselect = false,
                Title = guiStrings.TextChooseArchive,
            };
            return dlg.ShowDialog (this).Value ? dlg.FileName : null;
        }

        void OnFormatSelect (object sender, SelectionChangedEventArgs e)
        {
            var format = this.ArchiveFormat.SelectedItem as ArchiveFormat;
            UIElement widget = null;
            if (null != format)
            {
                var options = format.GetOptions();
                ArchiveOptions = options;
                if (null != options)
                    widget = options.Widget as UIElement;
            }
            else
            {
                ArchiveOptions = null;
            }
            OptionsWidget.Content = widget;
            OptionsWidget.Visibility = null != widget ? Visibility.Visible : Visibility.Hidden;

            if (!string.IsNullOrEmpty (ArchiveName.Text))
            {
                ArchiveName.Text = Path.ChangeExtension (ArchiveName.Text, format.Extensions.First());
            }
        }

        void CanExecuteAlways (object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = true;
        }

        private void ArchiveName_TextChanged (object sender, RoutedEventArgs e)
        {
            this.ButtonOk.IsEnabled = ArchiveName.Text.Length > 0;
        }
    }
}
