// Game Resource Browser
//
// Copyright (C) 2014 by morkt
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
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Diagnostics;
using Microsoft.WindowsAPICodePack.Dialogs;
using GARbro.GUI.Properties;
using GARbro.GUI.Strings;
using GameRes;

namespace GARbro.GUI
{
    /// <summary>
    /// Interaction logic for ExtractArchive.xaml
    /// </summary>
    public partial class ExtractArchiveDialog : ExtractDialog
    {
        public ExtractArchiveDialog (string filename, string destination)
        {
            InitializeComponent();
            ExtractLabel.Text = string.Format (guiStrings.LabelExtractAllTo, filename);
            DestinationDir.Text = destination;

            ExtractImages.IsChecked = Settings.Default.appExtractImages;
            ExtractText.IsChecked = Settings.Default.appExtractText;
            ExtractText.IsEnabled = false;
            TextEncoding.IsEnabled = false;

            InitImageFormats (ImageConversionFormat);
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
            Settings.Default.appExtractImages = this.ExtractImages.IsChecked.Value;
            Settings.Default.appExtractText = this.ExtractText.IsChecked.Value;
            ExportImageFormat (ImageConversionFormat);
        }

        public ImageFormat GetImageFormat ()
        {
            var selected = ImageConversionFormat.SelectedItem as ImageFormatModel;
            return null != selected ? selected.Source : null;
        }
    }
}
