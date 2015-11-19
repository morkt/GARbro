//! \brief      Code-behind for INT encryption query widget.
//
// Copyright (C) 2014-2015 by morkt
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to
// deal in the Software without restriction, including without limitation the
// rights to use, copy, modify, merge, publish, distribute, sublicense, and/or
// sell copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
// IN THE SOFTWARE.
//

using System;
using System.Globalization;
using System.Windows.Controls;
using System.Windows.Data;
using GameRes.Formats.CatSystem;
using GameRes.Formats.Strings;
using Microsoft.Win32;
using System.Windows;
using System.IO;

namespace GameRes.Formats.GUI
{
    /// <summary>
    /// Interaction logic for WidgetINT.xaml
    /// </summary>
    public partial class WidgetINT : StackPanel
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
            uint key = KeyData.EncodePassPhrase (widget.Text);
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

        private void Check_Click (object sender, System.Windows.RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog {
                CheckFileExists = true,
                CheckPathExists = true,
                Multiselect = false,
                Title = arcStrings.INTChooseExe,
                Filter = arcStrings.INTExeFiles+"|*.exe",
                FilterIndex = 1,
                InitialDirectory = Directory.GetCurrentDirectory(),
            };
            if (!dlg.ShowDialog (Window.GetWindow (this)).Value)
                return;
            try
            {
                var pass = IntOpener.GetPassFromExe (dlg.FileName);
                if (null != pass)
                {
                    this.ExeMessage.Text = arcStrings.INTMessage1;
                    Passphrase.Text = pass;
                }
                else
                    this.ExeMessage.Text = string.Format (arcStrings.INTKeyNotFound, Path.GetFileName (dlg.FileName));
            }
            catch (Exception X)
            {
                this.ExeMessage.Text = X.Message;
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
