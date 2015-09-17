using System;
using System.Globalization;
using System.Windows.Controls;
using System.Windows.Data;
using GameRes.Formats.CatSystem;

namespace GameRes.Formats.GUI
{
    /// <summary>
    /// Interaction logic for WidgetINT.xaml
    /// </summary>
    public partial class WidgetINT : Grid
    {
        public WidgetINT ()
        {
            InitializeComponent();
            this.DataContext = GameRes.Formats.Properties.Settings.Default.INTEncryption ?? new IntEncryptionInfo();

            Passphrase.TextChanged += OnPassphraseChanged;
            EncScheme.SelectionChanged += OnSchemeChanged;
        }

        public IntEncryptionInfo Info { get { return this.DataContext as IntEncryptionInfo; } }

        void OnPasskeyChanged (object sender, TextChangedEventArgs e)
        {
        }

        void OnPassphraseChanged (object sender, TextChangedEventArgs e)
        {
            var widget = sender as TextBox;
            uint key = IntOpener.EncodePassPhrase (widget.Text);
            Passkey.Text = key.ToString ("X8");
        }

        void OnSchemeChanged (object sender, SelectionChangedEventArgs e)
        {
            var widget = sender as ComboBox;
            KeyData keydata;
            if (IntOpener.KnownSchemes.TryGetValue (widget.SelectedItem as string, out keydata))
            {
                Passphrase.TextChanged -= OnPassphraseChanged;
                try
                {
                    Passphrase.Text = keydata.Passphrase;
                    Passkey.Text = keydata.Key.ToString ("X8");
                }
                finally
                {
                    Passphrase.TextChanged += OnPassphraseChanged;
                }
            }
        }
    }

    [ValueConversion(typeof(uint?), typeof(string))]
    public class KeyConverter : IValueConverter
    {
        public object Convert (object value, Type targetType, object parameter, CultureInfo culture)
        {
            uint? key = (uint?)value;
            return null != key ? key.Value.ToString ("X") : "";
        }

        public object ConvertBack (object value, Type targetType, object parameter, CultureInfo culture)
        {
            string strValue = value as string;
            uint result_key;
            if (uint.TryParse(strValue, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out result_key))
                return new uint? (result_key);
            else
                return null;
        }
    }

    public class PasskeyRule : ValidationRule
    {
        public PasskeyRule()
        {
        }

        public override ValidationResult Validate (object value, CultureInfo cultureInfo)
        {
            uint key = 0;
            try
            {
                if (((string)value).Length > 0)
                    key = UInt32.Parse ((string)value, NumberStyles.HexNumber);
            }
            catch
            {
                return new ValidationResult (false, Strings.arcStrings.INTKeyRequirement);
            }
            return new ValidationResult (true, null);
        }
    }
}
