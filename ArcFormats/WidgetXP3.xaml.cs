using System.Windows;
using System.Windows.Controls;
using GameRes.Formats.KiriKiri;
using GameRes.Formats.Properties;
using GameRes.Formats.Strings;


namespace GameRes.Formats.GUI
{
    /// <summary>
    /// Interaction logic for WidgetXP3.xaml
    /// </summary>
    public partial class WidgetXP3 : Grid
    {
        public WidgetXP3 ()
        {
            InitializeComponent();
            if (null == Scheme.SelectedItem)
                Scheme.SelectedItem = arcStrings.ArcNoEncryption;
        }

        public ICrypt GetScheme ()
        {
            return Xp3Opener.GetScheme (Scheme.SelectedItem as string);
        }
    }
}
