using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Data;
using GameRes.Formats.LiveMaker;
using GameRes.Formats.Properties;
using GameRes.Formats.Strings;

namespace GameRes.Formats.GUI
{
    /// <summary>
    /// Interaction logic for WidgetGAL.xaml
    /// </summary>
    public partial class WidgetGAL : StackPanel
    {
        public WidgetGAL()
        {
            InitializeComponent();
            var first_item = new KeyValuePair<string, uint> (arcStrings.ArcIgnoreEncryption, 0u);
            var items = new KeyValuePair<string, uint>[] { first_item };
            this.Title.ItemsSource = items.Concat (GalFormat.KnownKeys.OrderBy (x => x.Key));
        }
    }

    [ValueConversion(typeof(uint), typeof(string))]
    public class GaleKeyConverter : IValueConverter
    {
        public object Convert (object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is uint)
                return ((uint)value).ToString ("X");
            else if (value is string)
                return ConvertBack (value, targetType, parameter, culture);
            else
                return "";
        }

        public object ConvertBack (object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is uint)
                return Convert (value, targetType, parameter, culture);
            string strValue = value as string;
            uint result_key;
            if (!string.IsNullOrWhiteSpace (strValue)
                && uint.TryParse (strValue, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out result_key))
                return result_key;
            else
                return null;
        }
    }

    public class GaleKeyRule : ValidationRule
    {
        public override ValidationResult Validate (object value, CultureInfo cultureInfo)
        {
            uint key = 0;
            try
            {
                if (value is uint)
                    key = (uint)value;
                else if (!string.IsNullOrWhiteSpace (value as string))
                    key = UInt32.Parse ((string)value, NumberStyles.HexNumber);
            }
            catch
            {
                return new ValidationResult (false, arcStrings.INTKeyRequirement);
            }
            return new ValidationResult (true, null);
        }
    }
}
