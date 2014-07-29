using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using GameRes.Formats.ONScripter;
using GameRes.Formats.Strings;

namespace GameRes.Formats.GUI
{
    /// <summary>
    /// Interaction logic for CreateONSWidget.xaml
    /// </summary>
    public partial class CreateONSWidget : Grid
    {
        public CreateONSWidget ()
        {
            InitializeComponent ();
        }
    }

    [ValueConversion (typeof (Compression), typeof (string))]
    class CompressionToStringConverter : IValueConverter
    {
        public object Convert (object value, Type targetType, object parameter, CultureInfo culture)
        {
            switch ((Compression)value)
            {
            case Compression.SPB:   return "SPB";
            case Compression.LZSS:  return "LZSS";
            case Compression.NBZ:   return "NBZ";
            default:                return arcStrings.ONSCompressionNone;
            }
        }

        public object ConvertBack (object value, Type targetType, object parameter, CultureInfo culture)
        {
            string s = value as string;
            if (!string.IsNullOrEmpty (s))
            {
                if ("SPB" == s)
                    return Compression.SPB;
                else if ("LZSS" == s)
                    return Compression.LZSS;
                else if ("NBZ" == s)
                    return Compression.NBZ;
            }
            return Compression.None;
        }
    }
}
