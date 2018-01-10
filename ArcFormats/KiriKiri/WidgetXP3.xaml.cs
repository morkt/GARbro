using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Data;
using GameRes.Formats.KiriKiri;
using GameRes.Formats.Strings;

namespace GameRes.Formats.GUI
{
    /// <summary>
    /// Interaction logic for WidgetXP3.xaml
    /// </summary>
    public partial class WidgetXP3 : StackPanel
    {
        public WidgetXP3 ()
        {
            var last_selected = Properties.Settings.Default.XP3Scheme;
            InitializeComponent();
            var keys = new[] { new KeyValuePair<string, ICrypt> (arcStrings.ArcNoEncryption, Xp3Opener.NoCryptAlgorithm) };
            this.DataContext = keys.Concat (Xp3Opener.KnownSchemes.OrderBy (x => x.Key));
            this.Loaded += (s, e) => {
                if (!string.IsNullOrEmpty (last_selected))
                    this.Scheme.SelectedValue = last_selected;
                else
                    this.Scheme.SelectedIndex = 0;
            };
        }

        public ICrypt GetScheme ()
        {
            return Xp3Opener.GetScheme (Scheme.SelectedValue as string);
        }
    }

    internal class ClassNameConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value != null)
                return value.GetType().Name;
            else
                return "";
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
