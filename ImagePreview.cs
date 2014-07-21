//! \file       ImagePreview.cs
//! \date       Sun Jul 06 06:34:56 2014
//! \brief      preview images.
//

using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media.Imaging;
using GARbro.GUI.Strings;
using GameRes;

namespace GARbro.GUI
{
    public partial class MainWindow : Window
    {
        private readonly BackgroundWorker   m_preview_worker = new BackgroundWorker();
        private PreviewFile                 m_current_preview = new PreviewFile();
        private bool                        m_preview_pending = false;

        class PreviewFile
        {
            public string Path { get; set; }
            public string Name { get; set; }
            public Entry  Entry { get; set; }
            public ArcFile Archive { get; set; }

            public bool IsEqual (string path, string name)
            {
                return path.Equals (Path) && name.Equals (Name);
            }
        }

        private void InitPreviewPane ()
        {
            m_preview_worker.DoWork += (s, e) => LoadPreviewImage (e.Argument as PreviewFile);
            m_preview_worker.RunWorkerCompleted += (s, e) => {
                if (m_preview_pending)
                    RefreshPreviewPane();
            };
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
                PreviewPane.Source = null;
        }

        void UpdatePreviewPane (Entry entry)
        {
            SetStatusText ("");
            var vm = ViewModel;
            m_current_preview = new PreviewFile { Path = vm.Path, Name = entry.Name };
            PreviewPane.Source = null;
            if (entry.Type != "image")
                return;
            if (vm.IsArchive)
            {
                m_current_preview.Archive = m_app.CurrentArchive;
                m_current_preview.Entry = entry;
            }
            if (!m_preview_worker.IsBusy)
                m_preview_worker.RunWorkerAsync (m_current_preview);
            else
                m_preview_pending = true;
        }

        void LoadPreviewImage (PreviewFile preview)
        {
            try
            {
                Stream file;
                if (null == preview.Archive)
                {
                    string filename = Path.Combine (preview.Path, preview.Name);
                    file = new FileStream (filename, FileMode.Open, FileAccess.Read);
                }
                else
                {
                    file = preview.Archive.OpenEntry (preview.Entry);
                }
                using (file)
                {
                    var data = ReadImage (file);
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
                    PreviewPane.Source = bitmap;
                    SetStatusText (string.Format ("Image {0} x {1} x {2}bpp", bitmap.PixelWidth,
                                                  bitmap.PixelHeight, bitmap.Format.BitsPerPixel));
                }
            });
        }

        ImageData ReadImage (Stream file)
        {
            bool need_dispose = false;
            try
            {
                if (!file.CanSeek)
                {
                    var stream = new MemoryStream();
                    file.CopyTo (stream);
                    file = stream;
                    need_dispose = true;
                }
                var format = FindImageFormat (file);
                if (null == format)
                    return null;
                file.Position = 0;
                return format.Item1.Read (file, format.Item2);
            }
            finally
            {
                if (need_dispose)
                    file.Dispose();
            }
        }

        Tuple<ImageFormat, ImageMetaData> FindImageFormat (Stream file)
        {
            uint signature = FormatCatalog.ReadSignature (file);
            for (;;)
            {
                var range = FormatCatalog.Instance.LookupSignature<ImageFormat> (signature);
                foreach (var impl in range)
                {
                    try
                    {
                        file.Position = 0;
                        ImageMetaData metadata = impl.ReadMetaData (file);
                        if (null != metadata)
                            return new Tuple<ImageFormat, ImageMetaData> (impl, metadata);
                    }
                    catch { }
                }
                if (0 == signature)
                    break;
                signature = 0;
            }
            return null;
        }

        void ExtractImage (ArcFile arc, Entry entry, ImageFormat target_format)
        {
            using (var file = arc.OpenEntry (entry))
            {
                ImageData image = ReadImage (file);
                if (null == image)
                    throw new InvalidFormatException (string.Format ("{1}: {0}", guiStrings.MsgUnableInterpret, entry.Name));
                string target_ext = target_format.Extensions.First();
                string outname = Path.GetFileNameWithoutExtension (entry.Name)+'.'+target_ext;
                Trace.WriteLine (string.Format ("{0} => {1}", entry.Name, outname), "ExtractFileFromArchive");
                using (var outfile = File.Create (outname))
                {
                    target_format.Write (outfile, image);
                }
            }
        }
    }
}
