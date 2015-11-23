using GameRes.Formats.FC01;
using GameRes.Formats.Strings;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Data;

namespace GameRes.Formats.GUI
{
    /// <summary>
    /// Interaction logic for WidgetMCG.xaml
    /// </summary>
    public partial class WidgetMCG : StackPanel
    {
        public WidgetMCG ()
        {
            InitializeComponent();
            var none = new KeyValuePair<string, byte>[] { new KeyValuePair<string, byte> (arcStrings.ArcIgnoreEncryption, 0) };
            Title.ItemsSource = none.Concat (McgFormat.KnownKeys.OrderBy (x => x.Key));
        }

        public byte GetKey ()
        {
            return ByteKeyConverter.StringToByte (this.Passkey.Text);
        }
    }

    internal static class ByteKeyConverter
    {
        public static string ByteToString (object value)
        {
            byte key = (byte)value;
            return key.ToString ("X2", CultureInfo.InvariantCulture);
        }

        public static byte StringToByte (object value)
        {
            string s = value as string;
            if (string.IsNullOrEmpty (s))
                return 0;
            byte key;
            if (!byte.TryParse (s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out key))
                return 0;
            return key;
        } 
    }

    [ValueConversion (typeof (byte), typeof (string))]
    class ByteToStringConverter : IValueConverter
    {
        public object Convert (object value, Type targetType, object parameter, CultureInfo culture)
        {
            return ByteKeyConverter.ByteToString (value);
        }

        public object ConvertBack (object value, Type targetType, object parameter, CultureInfo culture)
        {
            return ByteKeyConverter.StringToByte (value);
        }
    }

    [ValueConversion (typeof (string), typeof (byte))]
    class StringToByteConverter : IValueConverter
    {
        public object Convert (object value, Type targetType, object parameter, CultureInfo culture)
        {
            return ByteKeyConverter.StringToByte (value);
        }

        public object ConvertBack (object value, Type targetType, object parameter, CultureInfo culture)
        {
            return ByteKeyConverter.ByteToString (value);
        }
    }
}
