using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace GameRes.Formats.GUI
{
    /// <summary>
    /// Interaction logic for CreateXP3Widget.xaml
    /// </summary>
    public partial class CreateXP3Widget : Grid
    {
        public CreateXP3Widget ()
        {
            InitializeComponent ();
        }
    }

    public class Xp3VersionConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var val = (int)value;
            switch (val)
            {
                case 1: return "1";
                case 2: return "2";
                case 3: return "Z";
                default: throw new NotImplementedException();
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var str = value.ToString();
            switch (str)
            {
                case "1": return 1;
                case "2": return 2;
                case "Z": return 3;
                default: throw new NotImplementedException();
            }
        }
    }
}
