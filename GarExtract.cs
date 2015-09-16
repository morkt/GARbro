//! \file       GarExtract.cs
//! \date       Fri Jul 25 05:52:27 2014
//! \brief      Extract archive frontend.
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
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Ookii.Dialogs.Wpf;
using GameRes;
using GameRes.Strings;
using GARbro.GUI.Strings;
using GARbro.GUI.Properties;

namespace GARbro.GUI
{
    public partial class MainWindow : Window
    {
        /// <summary>
        /// Handle "Extract item" command.
        /// </summary>
        private void ExtractItemExec (object sender, ExecutedRoutedEventArgs e)
        {
            var entry = CurrentDirectory.SelectedItem as EntryViewModel;
            if (null == entry && !ViewModel.IsArchive)
            {
                SetStatusText (guiStrings.MsgChooseFiles);
                return;
            }
            GarExtract extractor = null;
            try
            {
                string destination = Settings.Default.appLastDestination;
                if (!Directory.Exists (destination))
                    destination = "";
                var vm = ViewModel;
                if (!vm.IsArchive)
                {
                    if (!entry.IsDirectory)
                    {
                        var source = entry.Source.Name;
                        SetBusyState();
                        if (string.IsNullOrEmpty (destination))
                        {
                            // extract into directory named after archive
                            if (!string.IsNullOrEmpty (Path.GetExtension (entry.Name)))
                                destination = Path.GetFileNameWithoutExtension (source);
                            else
                                destination = vm.Path.First();
                        }
                        extractor = new GarExtract (this, source);
                        extractor.ExtractAll (destination);
                    }
                }
                else if (vm.Path.Count > 1)
                {
                    if (string.IsNullOrEmpty (destination))
                        destination = Path.GetDirectoryName (vm.Path.First());
                    var archive_name = vm.Path[vm.Path.Count-2];
                    extractor = new GarExtract (this, archive_name, VFS.Top as ArchiveFileSystem);
                    if (null == entry || (entry.Name == ".." && string.IsNullOrEmpty (vm.Path.Last()))) // root entry
                        extractor.ExtractAll (destination);
                    else
                        extractor.Extract (entry, destination);
                }
            }
            catch (OperationCanceledException X)
            {
                SetStatusText (X.Message);
            }
            catch (Exception X)
            {
                PopupError (X.Message, guiStrings.MsgErrorExtracting);
            }
            finally
            {
                if (null != extractor && !extractor.IsActive)
                    extractor.Dispose();
            }
        }
    }

    sealed internal class GarExtract : IDisposable
    {
        private MainWindow          m_main;
        private string              m_arc_name;
        private ArchiveFileSystem   m_fs;
        private readonly bool       m_should_ascend;
        private bool                m_skip_images = false;
        private bool                m_skip_script = false;
        private bool                m_skip_audio  = false;
        private bool                m_adjust_image_offset = false;
        private bool                m_convert_audio;
        private ImageFormat         m_image_format;
        private int                 m_extract_count;
        private bool                m_extract_in_progress = false;
        private ProgressDialog      m_progress_dialog;
        private Exception           m_pending_error;

        public bool IsActive { get { return m_extract_in_progress; } }

        public GarExtract (MainWindow parent, string source)
        {
            m_arc_name = Path.GetFileName (source);
            try
            {
                VFS.ChDir (source);
                m_should_ascend = true;
            }
            catch (Exception X)
            {
                throw new OperationCanceledException (string.Format ("{1}: {0}", X.Message, m_arc_name));
            }
            m_fs = VFS.Top as ArchiveFileSystem;
            m_main = parent;
        }

        public GarExtract (MainWindow parent, string source, ArchiveFileSystem fs)
        {
            if (null == fs)
                throw new UnknownFormatException();
            m_fs = fs;
            m_main = parent;
            m_arc_name = Path.GetFileName (source);
            m_should_ascend = false;
        }

        private void PrepareDestination (string destination)
        {
            bool stop_watch = !m_main.ViewModel.IsArchive;
            if (stop_watch)
                m_main.StopWatchDirectoryChanges();
            try
            {
                Directory.CreateDirectory (destination);
                Directory.SetCurrentDirectory (destination);
                Settings.Default.appLastDestination = destination;
            }
            finally
            {
                if (stop_watch)
                    m_main.ResumeWatchDirectoryChanges();
            }
        }

        public void ExtractAll (string destination)
        {
            var file_list = m_fs.GetFilesRecursive();
            if (!file_list.Any())
            {
                m_main.SetStatusText (string.Format ("{1}: {0}", guiStrings.MsgEmptyArchive, m_arc_name));
                return;
            }
            var extractDialog = new ExtractArchiveDialog (m_arc_name, destination);
            extractDialog.Owner = m_main;
            var result = extractDialog.ShowDialog();
            if (!result.Value)
                return;

            destination = extractDialog.Destination;
            if (!string.IsNullOrEmpty (destination))
            {
                destination = Path.GetFullPath (destination);
                PrepareDestination (destination);
            }
            else
                destination = ".";
            m_skip_images = !extractDialog.ExtractImages.IsChecked.Value;
            m_skip_script = !extractDialog.ExtractText.IsChecked.Value;
            m_skip_audio  = !extractDialog.ExtractAudio.IsChecked.Value;
            if (!m_skip_images)
                m_image_format = extractDialog.GetImageFormat (extractDialog.ImageConversionFormat);

            m_main.SetStatusText (string.Format(guiStrings.MsgExtractingTo, m_arc_name, destination));
            ExtractFilesFromArchive (string.Format (guiStrings.MsgExtractingArchive, m_arc_name), file_list);
        }

        public void Extract (EntryViewModel entry, string destination)
        {
            var view_model = m_main.ViewModel;
            var selected = m_main.CurrentDirectory.SelectedItems.Cast<EntryViewModel>();
            if (!selected.Any() && entry.Name == "..")
                selected = view_model;

            IEnumerable<Entry> file_list = selected.Select (e => e.Source);
            if (m_fs is TreeArchiveFileSystem)
                file_list = (m_fs as TreeArchiveFileSystem).GetFilesRecursive (file_list);

            if (!file_list.Any())
            {
                m_main.SetStatusText (guiStrings.MsgChooseFiles);
                return;
            }

            ExtractDialog extractDialog;
            bool multiple_files = file_list.Skip (1).Any();
            if (multiple_files)
                extractDialog = new ExtractArchiveDialog (m_arc_name, destination);
            else
                extractDialog = new ExtractFile (entry, destination);
            extractDialog.Owner = m_main;
            var result = extractDialog.ShowDialog();
            if (!result.Value)
                return;
            if (multiple_files)
            {
                m_skip_images = !Settings.Default.appExtractImages;
                m_skip_script = !Settings.Default.appExtractText;
                m_skip_audio  = !Settings.Default.appExtractAudio;
            }
            destination = extractDialog.Destination;
            if (!string.IsNullOrEmpty (destination))
            {
                destination = Path.GetFullPath (destination);
                PrepareDestination (destination);
            }
            if (!m_skip_images)
                m_image_format = FormatCatalog.Instance.ImageFormats.FirstOrDefault (f => f.Tag.Equals (Settings.Default.appImageFormat));

            ExtractFilesFromArchive (string.Format (guiStrings.MsgExtractingFile, m_arc_name), file_list);
        }

        private void ExtractFilesFromArchive (string text, IEnumerable<Entry> file_list)
        {
            if (file_list.Skip (1).Any() // file_list.Count() > 1
                && (m_skip_images || m_skip_script || m_skip_audio))
                file_list = file_list.Where (f => !(m_skip_images && f.Type == "image") && 
                                                  !(m_skip_script && f.Type == "script") &&
                                                  !(m_skip_audio  && f.Type == "audio"));
            if (!file_list.Any())
            {
                m_main.SetStatusText (string.Format ("{1}: {0}", guiStrings.MsgNoFiles, m_arc_name));
                return;
            }
            file_list = file_list.OrderBy (e => e.Offset);
            m_progress_dialog = new ProgressDialog ()
            {
                WindowTitle = guiStrings.TextTitle,
                Text        = text,
                Description = "",
                MinimizeBox = true,
            };
            if (!file_list.Skip (1).Any()) // 1 == file_list.Count()
            {
                m_progress_dialog.Description = file_list.First().Name;
                m_progress_dialog.ProgressBarStyle = ProgressBarStyle.MarqueeProgressBar;
            }
            m_convert_audio = !m_skip_audio && Settings.Default.appConvertAudio;
            m_extract_count = 0;
            m_pending_error = null;
            m_progress_dialog.DoWork += (s, e) => ExtractWorker (file_list);
            m_progress_dialog.RunWorkerCompleted += OnExtractComplete;
            m_progress_dialog.ShowDialog (m_main);
            m_extract_in_progress = true;
        }

        void ExtractWorker (IEnumerable<Entry> file_list)
        {
            try
            {
                var arc = m_fs.Source;
                int total = file_list.Count();
                foreach (var entry in file_list)
                {
                    if (m_progress_dialog.CancellationPending)
                        break;
                    if (total > 1)
                        m_progress_dialog.ReportProgress (m_extract_count*100/total, null, entry.Name);
                    if (null != m_image_format && entry.Type == "image")
                        ExtractImage (arc, entry, m_image_format);
                    else if (m_convert_audio && entry.Type == "audio")
                        ExtractAudio (arc, entry);
                    else
                        arc.Extract (entry);
                    ++m_extract_count;
                }
            }
            catch (Exception X)
            {
                m_pending_error = X;
            }
        }

        void ExtractImage (ArcFile arc, Entry entry, ImageFormat target_format)
        {
            using (var file = arc.OpenSeekableEntry (entry))
            {
                var src_format = ImageFormat.FindFormat (file, entry.Name);
                if (null == src_format)
                    throw new InvalidFormatException (string.Format ("{1}: {0}", guiStrings.MsgUnableInterpretImage, entry.Name));
                file.Position = 0;
                string target_ext = target_format.Extensions.First();
                string outname = FindUniqueFileName (entry.Name, target_ext);
                if (src_format.Item1 == target_format)
                {
                    // source format is the same as a target, copy file as is
                    using (var output = ArchiveFormat.CreateFile (outname))
                        file.CopyTo (output);
                    return;
                }
                ImageData image = src_format.Item1.Read (file, src_format.Item2);
                if (m_adjust_image_offset)
                {
                    image = AdjustImageOffset (image);
                }
                using (var outfile = ArchiveFormat.CreateFile (outname))
                {
                    target_format.Write (outfile, image);
                }
            }
        }

        static ImageData AdjustImageOffset (ImageData image)
        {
            if (0 == image.OffsetX && 0 == image.OffsetY)
                return image;
            int width = (int)image.Width + image.OffsetX;
            int height = (int)image.Height + image.OffsetY;
            if (width <= 0 || height <= 0)
                return image;

            int x = Math.Max (image.OffsetX, 0);
            int y = Math.Max (image.OffsetY, 0);
            int src_x = image.OffsetX < 0 ? Math.Abs (image.OffsetX) : 0;
            int src_y = image.OffsetY < 0 ? Math.Abs (image.OffsetY) : 0;
            int src_stride = (int)image.Width * (image.BPP+7) / 8;
            int dst_stride = width * (image.BPP+7) / 8;
            var pixels = new byte[height*dst_stride];
            int offset = y * dst_stride + x * image.BPP / 8;
            Int32Rect rect = new Int32Rect (src_x, src_y, (int)image.Width - src_x, 1);
            for (int row = src_y; row < image.Height; ++row)
            {
                rect.Y = row;
                image.Bitmap.CopyPixels (rect, pixels, src_stride, offset);
                offset += dst_stride;
            }
            var bitmap = BitmapSource.Create (width, height, image.Bitmap.DpiX, image.Bitmap.DpiY,
                image.Bitmap.Format, image.Bitmap.Palette, pixels, dst_stride);
            return new ImageData (bitmap);
        }

        static void ExtractAudio (ArcFile arc, Entry entry)
        {
            using (var file = arc.OpenEntry (entry))
            using (var sound = AudioFormat.Read (file))
            {
                if (null == sound)
                    throw new InvalidFormatException (string.Format ("{1}: {0}", guiStrings.MsgUnableInterpretAudio, entry.Name));
                ConvertAudio (entry.Name, sound);
            }
        }

        public static void ConvertAudio (string entry_name, SoundInput input)
        {
            string source_format = input.SourceFormat;
            if (GarConvertMedia.CommonAudioFormats.Contains (source_format))
            {
                string output_name = FindUniqueFileName (entry_name, source_format);
                using (var output = ArchiveFormat.CreateFile (output_name))
                {
                    input.Source.Position = 0;
                    input.Source.CopyTo (output);
                }
            }
            else
            {
                string output_name = FindUniqueFileName (entry_name, "wav");
                using (var output = ArchiveFormat.CreateFile (output_name))
                    AudioFormat.Wav.Write (input, output);
            }
        }

        public static string FindUniqueFileName (string source_filename, string target_ext)
        {
            string ext = target_ext;
            for (int attempt = 1; attempt < 100; ++attempt)
            {
                string filename = Path.ChangeExtension (source_filename, ext);
                if (!File.Exists (filename))
                    return filename;
                ext = string.Format ("{0}.{1}", attempt, target_ext);
            }
            throw new IOException ("File aready exists");
        }

        void OnExtractComplete (object sender, RunWorkerCompletedEventArgs e)
        {
            m_extract_in_progress = false;
            m_progress_dialog.Dispose();
            m_main.Activate();
            if (!m_main.ViewModel.IsArchive)
            {
                m_main.Dispatcher.Invoke (m_main.RefreshView);
            }
            m_main.SetStatusText (Localization.Format ("MsgExtractedFiles", m_extract_count));
            if (null != m_pending_error)
            {
                if (m_pending_error is OperationCanceledException)
                    m_main.SetStatusText (m_pending_error.Message);
                else
                {
                    string message = string.Format (guiStrings.TextErrorExtracting,
                                                    m_progress_dialog.Description, m_pending_error.Message);
                    m_main.PopupError (message, guiStrings.MsgErrorExtracting);
                }
            }
            this.Dispose();
        }
        
        #region IDisposable Members
        bool disposed = false;

        public void Dispose ()
        {
            if (!disposed)
            {
                if (m_should_ascend)
                {
                    VFS.ChDir ("..");
                }
                disposed = true;
            }
            GC.SuppressFinalize (this);
        }
        #endregion
    }
}
