//! \file       ExtractDialog.cs
//! \date       Wed Jul 09 11:26:08 2014
//! \brief      Extract dialog window.
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

using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.WindowsAPICodePack.Dialogs;
using GARbro.GUI.Properties;
using GARbro.GUI.Strings;
using GameRes;
using System.Windows.Input;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;

namespace GARbro.GUI
{
    public partial class ExtractDialog : Window
    {
        public string Destination { get; set; }

        public void InitImageFormats (ComboBox image_format)
        {
            var default_format = Settings.Default.appImageFormat;
            var formats = FormatCatalog.Instance.ImageFormats.Where (f => f.IsBuiltin);
            ImageFormatModel[] default_model = { new ImageFormatModel() };
            var models = default_model.Concat (formats.Select (f => new ImageFormatModel (f))).ToList();

            var selected = models.FirstOrDefault (f => f.Tag.Equals (default_format));
            image_format.ItemsSource = models;
            if (null != selected)
                image_format.SelectedItem = selected;
            else if (models.Any())
                image_format.SelectedIndex = 0;
        }

        public ImageFormat GetImageFormat (ComboBox image_format)
        {
            var selected = image_format.SelectedItem as ImageFormatModel;
            if (null != selected)
                return selected.Source;
            else
                return null;
        }

        public void ExportImageFormat (ComboBox image_format)
        {
            var format = GetImageFormat (image_format);
            if (null != format)
                Settings.Default.appImageFormat = format.Tag;
            else
                Settings.Default.appImageFormat = "";
        }

        public string ChooseFolder (string title, string initial)
        {
            var dlg = new CommonOpenFileDialog();
            dlg.Title = title;
            dlg.IsFolderPicker = true;
            dlg.InitialDirectory = initial;

            dlg.AddToMostRecentlyUsedList = false;
            dlg.AllowNonFileSystemItems = false;
            dlg.EnsureFileExists = true;
            dlg.EnsurePathExists = true;
            dlg.EnsureReadOnly = false;
            dlg.EnsureValidNames = true;
            dlg.Multiselect = false;
            dlg.ShowPlacesList = true;

            if (dlg.ShowDialog (this) == CommonFileDialogResult.Ok)
                return dlg.FileName;
            else
                return null;
        }

        protected void acb_OnEnterKeyDown (object sender, KeyEventArgs e)
        {
            string path = (sender as AutoCompleteBox).Text;
            if (!string.IsNullOrEmpty (path))
                this.DialogResult = true;
        }

        public void CanExecuteAlways (object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = true;
        }
    }
}
