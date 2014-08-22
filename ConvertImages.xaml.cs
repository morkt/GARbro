using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using GameRes;
using GARbro.GUI.Properties;

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
            InitImageFormats (this.ImageConversionFormat);
        }

        void InitImageFormats (ComboBox image_format)
        {
            /*
            var formats = FormatCatalog.Instance.ImageFormats;
            var models = formats.Select (f => new ImageFormatModel (f)).ToList();
            var selected_format = Settings.Default.appLastImageFormat;
            var selected = models.FirstOrDefault (f => f.Tag.Equals (selected_format));
            image_format.ItemsSource = models;
            if (null != selected)
                image_format.SelectedItem = selected;
            else if (models.Any())
                image_format.SelectedIndex = 0;
            */
        }

        private void ConvertButton_Click (object sender, RoutedEventArgs e)
        {
            this.DialogResult = true;
        }
    }
}
