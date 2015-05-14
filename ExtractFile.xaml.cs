// Game Resource Browser
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
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.WindowsAPICodePack.Dialogs;
using GARbro.GUI.Properties;
using GARbro.GUI.Strings;

namespace GARbro.GUI
{
    /// <summary>
    /// Interaction logic for ExtractFile.xaml
    /// </summary>
    public partial class ExtractFile : ExtractDialog
    {
        public ExtractFile (EntryViewModel entry, string destination)
        {
            InitializeComponent();
            ExtractLabel.Text = string.Format (guiStrings.LabelExtractFileTo, entry.Name);
            Destination = destination;
            DestinationDir.EnterKeyDown += acb_OnEnterKeyDown;
            if ("image" == entry.Type)
            {
                ActiveOption = ImageConversionOptions;
                InitImageFormats (ImageConversionFormat);
            }
            else if ("script" == entry.Type)
            {
                ActiveOption = TextConversionOptions;
                TextEncoding.IsEnabled = false;
            }
            else if ("audio" == entry.Type)
            {
                ActiveOption = AudioConversionOptions;
            }
            else
            {
                ActiveOption = null;
            }
        }

        private UIElement m_active_option;
        public UIElement ActiveOption
        {
            get { return m_active_option; }
            set
            {
                if (value == m_active_option)
                    return;
                m_active_option = value;
                if (null != m_active_option)
                    m_active_option.Visibility = Visibility.Visible;
                foreach (var c in ConversionTypePanel.Children.Cast<UIElement>())
                {
                    if (c != m_active_option)
                        c.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void BrowseExec (object sender, ExecutedRoutedEventArgs e)
        {
            string folder = ChooseFolder (guiStrings.TextChooseDestDir, DestinationDir.Text);
            if (null != folder)
                DestinationDir.Text = folder;
        }

        void ExtractButton_Click (object sender, RoutedEventArgs e)
        {
            this.DialogResult = true;
            if (ImageConversionOptions == ActiveOption)
            {
                ExportImageFormat (ImageConversionFormat);
            }
        }
    }
}
