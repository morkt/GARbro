using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GameRes.Formats.Strings;
using Microsoft.Win32;

namespace GameRes.Formats.GUI
{
    /// <summary>
    /// Interaction logic for CreateAMIWidget.xaml
    /// </summary>
    public partial class CreateAMIWidget : Grid
    {
        public CreateAMIWidget ()
        {
            InitializeComponent ();
        }

        private void Browse_Click (object sender, RoutedEventArgs e)
        {
            string initial = BaseArchive.Text;
            string dir = ".";
            if (!string.IsNullOrEmpty (initial))
            {
                var parent = Directory.GetParent (initial);
                if (null != parent)
                {
                    dir = parent.FullName;
                    initial = Path.GetFileName (initial);
                }
            }
            dir = Path.GetFullPath (dir);
            var dlg = new OpenFileDialog {
                CheckFileExists = true,
                CheckPathExists = true,
                FileName = initial,
                InitialDirectory = dir,
                Multiselect = false,
                Title = arcStrings.AMIChooseBase,
            };
            var owner = FindVisualParent<Window> (this);
            if (dlg.ShowDialog (owner).Value && !string.IsNullOrEmpty (dlg.FileName))
                BaseArchive.Text = dlg.FileName;
        }

        static parentItem FindVisualParent<parentItem> (DependencyObject obj) where parentItem : DependencyObject
        {
            if (null == obj)
                return null;
            DependencyObject parent = VisualTreeHelper.GetParent (obj);
            while (parent != null && !(parent is parentItem))
            {
                parent = VisualTreeHelper.GetParent (parent);
            }
            return parent as parentItem;
        }
    }
}
