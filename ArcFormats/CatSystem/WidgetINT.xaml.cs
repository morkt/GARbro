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
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows;
using Microsoft.Win32;
using GameRes.Formats.CatSystem;
using GameRes.Formats.Strings;

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
            ViewModel = new IntEncryptionViewModel (GameRes.Formats.Properties.Settings.Default.INTEncryption);
            this.DataContext = ViewModel;
        }

        IntEncryptionViewModel ViewModel { get; set; }

        public IntEncryptionInfo Info { get { return ViewModel.Source; } }

        private void Check_Click (object sender, System.Windows.RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog {
                CheckFileExists = true,
                CheckPathExists = true,
                Multiselect = false,
                Title = arcStrings.INTChooseExe,
                Filter = arcStrings.INTExeFiles+"|*.exe;*.bin",
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
                    ViewModel.ExeMessage = arcStrings.INTMessage1;
                    ViewModel.Password = pass;
                }
                else
                    ViewModel.ExeMessage = string.Format (arcStrings.INTKeyNotFound, Path.GetFileName (dlg.FileName));
            }
            catch (Exception X)
            {
                ViewModel.ExeMessage = X.Message;
            }
        }
    }

    [ValueConversion(typeof(uint?), typeof(string))]
    public class KeyConverter : IValueConverter
    {
        public object Convert (object value, Type targetType, object parameter, CultureInfo culture)
        {
            uint? key = (uint?)value;
            return null != key ? key.Value.ToString ("X8") : "";
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

    internal class IntEncryptionViewModel : INotifyPropertyChanged
    {
        public IntEncryptionViewModel (IntEncryptionInfo src)
        {
            Source = src ?? new IntEncryptionInfo();
            KnownKeys = IntOpener.KnownSchemes;
            m_message = Strings.arcStrings.INTMessage1;
        }

        public IntEncryptionInfo Source { get; set; }
        public Dictionary<string, KeyData> KnownKeys { get; private set; }

        public string Scheme {
            get { return Source.Scheme; }
            set {
                if (Source.Scheme != value)
                {
                    Source.Scheme = value;
                    NotifyPropertyChanged();
                    KeyData keydata;
                    if (!string.IsNullOrEmpty (value)
                        && KnownKeys.TryGetValue (value, out keydata))
                    {
                        Source.Password = keydata.Passphrase;
                        NotifyPropertyChanged ("Password");
                        Key = keydata.Key;
                    }
                }
            }
        }
        public string Password {
            get { return Source.Password; }
            set {
                if (Source.Password != value)
                {
                    Source.Password = value;
                    NotifyPropertyChanged();
                    var scheme = KnownKeys.FirstOrDefault (s => s.Value.Passphrase == value);
                    Scheme = scheme.Key;
                    Key = KeyData.EncodePassPhrase (value);
                }
            }
        }
        public uint? Key {
            get { return Source.Key; }
            set {
                if (Source.Key != value)
                {
                    Source.Key = value;
                    NotifyPropertyChanged();
                }
            }
        }
        string  m_message;
        public string ExeMessage {
            get { return m_message; }
            set {
                if (m_message != value)
                {
                    m_message = value;
                    NotifyPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void NotifyPropertyChanged ([CallerMemberName] string propertyName = "")
        {
            if (PropertyChanged != null)
                PropertyChanged (this, new PropertyChangedEventArgs (propertyName));
        }
    }
}
