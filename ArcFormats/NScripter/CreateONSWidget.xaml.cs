using System;
using System.Globalization;
using System.Windows.Controls;
using System.Windows.Data;
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

    [ValueConversion (typeof (NScripter.Compression), typeof (string))]
    class CompressionToStringConverter : IValueConverter
    {
        public object Convert (object value, Type targetType, object parameter, CultureInfo culture)
        {
            switch ((NScripter.Compression)value)
            {
            case NScripter.Compression.SPB:   return "SPB";
            case NScripter.Compression.LZSS:  return "LZSS";
            case NScripter.Compression.NBZ:   return "NBZ";
            default: return arcStrings.ONSCompressionNone;
            }
        }

        public object ConvertBack (object value, Type targetType, object parameter, CultureInfo culture)
        {
            string s = value as string;
            if (!string.IsNullOrEmpty (s))
            {
                if ("SPB" == s)
                    return NScripter.Compression.SPB;
                else if ("LZSS" == s)
                    return NScripter.Compression.LZSS;
                else if ("NBZ" == s)
                    return NScripter.Compression.NBZ;
            }
            return NScripter.Compression.None;
        }
    }
}
