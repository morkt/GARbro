using System;
using System.Globalization;
using System.Windows.Controls;
using System.Windows.Data;

namespace GameRes.Formats.GUI
{
    /// <summary>
    /// Interaction logic for CreateYPFWidget.xaml
    /// </summary>
    public partial class CreateYPFWidget : Grid
    {
        public CreateYPFWidget ()
        {
            InitializeComponent ();
        }
    }

    [ValueConversion (typeof (uint), typeof (string))]
    class VersionToStringConverter : IValueConverter
    {
        public object Convert (object value, Type targetType, object parameter, CultureInfo culture)
        {
            uint version = (uint)value;
            return version.ToString ("X");
        }

        public object ConvertBack (object value, Type targetType, object parameter, CultureInfo culture)
        {
            string s = value as string;
            if (string.IsNullOrEmpty (s))
                return null;
            uint version;
            if (!uint.TryParse (s, NumberStyles.HexNumber, culture, out version))
                return null;
            return version;
        }
    }

    [ValueConversion (typeof (uint), typeof (string))]
    class KeyToStringConverter : IValueConverter
    {
        public object Convert (object value, Type targetType, object parameter, CultureInfo culture)
        {
            uint key = (uint)value;
            if (key > 0xff)
                return "";
            return key.ToString ("X2");
        }

        public object ConvertBack (object value, Type targetType, object parameter, CultureInfo culture)
        {
            string s = value as string;
            if (string.IsNullOrEmpty (s))
                return uint.MaxValue;
            uint key;
            if (!uint.TryParse (s, NumberStyles.HexNumber, culture, out key) || key > 0xff)
                return uint.MaxValue;
            return key;
        }
    }
}
