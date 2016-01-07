//! \file       ImagePreview.cs
//! \date       Sun Jul 06 06:34:56 2014
//! \brief      preview images.
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
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using GARbro.GUI.Strings;
using GARbro.GUI.Properties;
using GameRes;
using System.Text;
using System.Windows.Documents;
using System.Windows.Media;
using System.Globalization;

namespace GARbro.GUI
{
    public partial class MainWindow : Window
    {
        private readonly BackgroundWorker   m_preview_worker = new BackgroundWorker();
        private PreviewFile                 m_current_preview = new PreviewFile();
        private bool                        m_preview_pending = false;

        private UIElement m_active_viewer;
        public UIElement ActiveViewer
        {
            get { return m_active_viewer;  }
            set
            {
                if (value == m_active_viewer)
                    return;
                m_active_viewer = value;
                m_active_viewer.Visibility = Visibility.Visible;
                bool exists = false;
                foreach (var c in PreviewPane.Children.Cast<UIElement>())
                {
                    if (c != m_active_viewer)
                        c.Visibility = Visibility.Collapsed;
                    else
                        exists = true;
                }
                if (!exists)
                    PreviewPane.Children.Add (m_active_viewer);
            }
        }

        class PreviewFile
        {
            public IEnumerable<string> Path { get; set; }
            public string Name { get; set; }
            public Entry Entry { get; set; }

            public bool IsEqual (IEnumerable<string> path, string name)
            {
                return Path != null && path.SequenceEqual (Path) && name.Equals (Name);
            }
        }

        private void InitPreviewPane ()
        {
            m_preview_worker.DoWork += (s, e) => LoadPreviewImage (e.Argument as PreviewFile);
            m_preview_worker.RunWorkerCompleted += (s, e) => {
                if (m_preview_pending)
                    RefreshPreviewPane();
            };
            ActiveViewer = ImageView;
            TextView.IsWordWrapEnabled = true;
        }

        private IEnumerable<Encoding> m_encoding_list = GetEncodingList();
        public IEnumerable<Encoding> TextEncodings { get { return m_encoding_list; } }

        private static IEnumerable<Encoding> GetEncodingList ()
        {
            var list = new HashSet<Encoding>();
            list.Add (Encoding.Default);
            var oem = CultureInfo.CurrentCulture.TextInfo.OEMCodePage;
            list.Add (Encoding.GetEncoding (oem));
            list.Add (Encoding.GetEncoding (932));
            list.Add (Encoding.UTF8);
            list.Add (Encoding.Unicode);
            list.Add (Encoding.BigEndianUnicode);
            return list;
        }
 
        private void OnEncodingSelect (object sender, SelectionChangedEventArgs e)
        {
            var enc = this.EncodingChoice.SelectedItem as Encoding;
            if (null == enc || null == CurrentTextInput)
                return;
            TextView.CurrentEncoding = enc;
        }

        /// <summary>
        /// Display entry in preview panel
        /// </summary>
        private void PreviewEntry (Entry entry)
        {
            if (m_current_preview.IsEqual (ViewModel.Path, entry.Name))
                return;
            UpdatePreviewPane (entry);
        }

        void RefreshPreviewPane ()
        {
            m_preview_pending = false;
            var current = CurrentDirectory.SelectedItem as EntryViewModel;
            if (null != current)
                UpdatePreviewPane (current.Source);
            else
                ResetPreviewPane();
        }

        void ResetPreviewPane ()
        {
            ActiveViewer = ImageView;
            ImageCanvas.Source = null;
            TextView.Clear();
            CurrentTextInput = null;
        }

        bool IsPreviewPossible (Entry entry)
        {
            return "image" == entry.Type || "script" == entry.Type
                || (string.IsNullOrEmpty (entry.Type) && entry.Size < 0x100000);
        }

        void UpdatePreviewPane (Entry entry)
        {
            SetStatusText ("");
            var vm = ViewModel;
            m_current_preview = new PreviewFile { Path = vm.Path, Name = entry.Name, Entry = entry };
            ImageCanvas.Source = null;
            TextView.Clear();
            if (!IsPreviewPossible (entry))
            {
                ActiveViewer = ImageView;
                return;
            }
            if ("image" != entry.Type)
                LoadPreviewText (m_current_preview);
            else if (!m_preview_worker.IsBusy)
                m_preview_worker.RunWorkerAsync (m_current_preview);
            else
                m_preview_pending = true;
        }

        private Stream m_current_text;
        private Stream CurrentTextInput
        {
            get { return m_current_text; }
            set
            {
                if (value == m_current_text)
                    return;
                if (null != m_current_text)
                    m_current_text.Dispose();
                m_current_text = value;
            }
        }

        Stream OpenPreviewStream (PreviewFile preview)
        {
            return VFS.OpenSeekableStream (preview.Entry);
        }

        void LoadPreviewText (PreviewFile preview)
        {
            Stream file = null;
            try
            {
                file = OpenPreviewStream (preview);
                if (!TextView.IsTextFile (file))
                {
                    ResetPreviewPane();
                    return;
                }
                var enc = EncodingChoice.SelectedItem as Encoding;
                if (null == enc)
                {
                    enc = TextView.GuessEncoding (file);
                    EncodingChoice.SelectedItem = enc;
                }
                TextView.DisplayStream (file, enc);
                ActiveViewer = TextView;
                CurrentTextInput = file;
                file = null;
            }
            catch (Exception X)
            {
                SetStatusText (X.Message);
            }
            finally
            {
                if (file != null)
                    file.Dispose();
            }
        }

        void LoadPreviewImage (PreviewFile preview)
        {
            try
            {
                using (var file = OpenPreviewStream (preview))
                {
                    var data = ImageFormat.Read (preview.Name, file);
                    if (null != data)
                        SetPreviewImage (preview, data.Bitmap);
                    else
                        Trace.WriteLine ("Cannot parse image format", preview.Name);
                }
            }
            catch (Exception X)
            {
                SetStatusText (X.Message);
            }
        }

        void SetPreviewImage (PreviewFile preview, BitmapSource bitmap)
        {
            if (bitmap.DpiX != Desktop.DpiX || bitmap.DpiY != Desktop.DpiY)
            {
                int stride = bitmap.PixelWidth * ((bitmap.Format.BitsPerPixel + 7) / 8); 
                var pixels = new byte[stride*bitmap.PixelHeight];
                bitmap.CopyPixels (pixels, stride, 0);
                var fixed_bitmap = BitmapSource.Create (bitmap.PixelWidth, bitmap.PixelHeight,
                    Desktop.DpiX, Desktop.DpiY, bitmap.Format, bitmap.Palette, pixels, stride);
                bitmap = fixed_bitmap;
            }
            if (!bitmap.IsFrozen)
                bitmap.Freeze();
            Dispatcher.Invoke (() =>
            {
                if (m_current_preview == preview) // compare by reference
                {
                    ActiveViewer = ImageView;
                    ImageCanvas.Source = bitmap;
                    SetStatusText (string.Format (guiStrings.MsgImageSize, bitmap.PixelWidth,
                                                  bitmap.PixelHeight, bitmap.Format.BitsPerPixel));
                }
            });
        }

        /// <summary>
        /// Fit window size to image.
        /// </summary>
        private void FitWindowExec (object sender, ExecutedRoutedEventArgs e)
        {
            var image = ImageCanvas.Source;
            if (null == image)
                return;
            var width = image.Width + Settings.Default.lvPanelWidth.Value + 1;
            var height = image.Height;
            width = Math.Max (ContentGrid.ActualWidth, width);
            height = Math.Max (ContentGrid.ActualHeight, height);
            if (width > ContentGrid.ActualWidth || height > ContentGrid.ActualHeight)
            {
                ContentGrid.Width = width;
                ContentGrid.Height = height;
                this.SizeToContent = SizeToContent.WidthAndHeight;
                Dispatcher.InvokeAsync (() => {
                    this.SizeToContent = SizeToContent.Manual;
                    ContentGrid.Width = double.NaN;
                    ContentGrid.Height = double.NaN;
                }, DispatcherPriority.ContextIdle);
            }
        }
    }
}
