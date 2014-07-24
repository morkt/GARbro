using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Text;
using System.Linq;
using Microsoft.Win32;
using GARbro.GUI.Strings;
using GameRes;
using System.Windows.Controls;

namespace GARbro.GUI
{
    /// <summary>
    /// Interaction logic for CreateArchive.xaml
    /// </summary>
    public partial class CreateArchiveDialog : Window
    {
        public CreateArchiveDialog ()
        {
            InitializeComponent ();

            this.ArchiveFormat.ItemsSource = FormatCatalog.Instance.ArcFormats;
        }

        public ResourceOptions ArchiveOptions { get; private set; }

        void Button_Click (object sender, RoutedEventArgs e)
        {
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
                InitialDirectory = dir,
                Multiselect = false,
                Filter = GetFilters(),
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
        }

        void CanExecuteAlways (object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = true;
        }
    }
}
