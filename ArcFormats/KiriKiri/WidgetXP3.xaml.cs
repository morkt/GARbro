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
            InitializeComponent();
            var keys = new[] { new KeyValuePair<string, ICrypt> (arcStrings.ArcNoEncryption, Xp3Opener.NoCryptAlgorithm) };
            this.DataContext = keys.Concat (Xp3Opener.KnownSchemes.OrderBy (x => x.Key));
            this.Loaded += (s, e) => {
                if (-1 == this.Scheme.SelectedIndex)
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
