using System.Globalization;
using System.Windows.Controls;
using System.Windows.Data;

namespace GameRes.Formats.GUI
{
    /// <summary>
    /// Interaction logic for CreateRPAWidget.xaml
    /// </summary>
    public partial class CreateRPAWidget : Grid
    {
        public CreateRPAWidget ()
        {
            InitializeComponent ();
        }
    }

    [ValueConversion(typeof(uint), typeof(string))]
    public class UInt32Converter : IValueConverter
    {
        public object Convert (object value, System.Type targetType, object parameter, CultureInfo culture)
        {
            if (null == value)
                return "";
            uint key = (uint)value;
            return key.ToString ("x");
        }

        public object ConvertBack (object value, System.Type targetType, object parameter, CultureInfo culture)
        {
            string strValue = value as string;
            uint result_key;
            if (uint.TryParse(strValue, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out result_key))
                return result_key;
            else
                return null;
        }
    }
}
