//! \file       GarConvert.cs
//! \date       Fri Aug 22 08:22:47 2014
//! \brief      Game resources conversion methods.
//
// Copyright (C) 2014-2016 by morkt
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
using System.Windows;
using System.Windows.Input;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using GameRes;
using GARbro.GUI.Strings;
using GARbro.GUI.Properties;
using System.Runtime.InteropServices;

namespace GARbro.GUI
{
    public partial class MainWindow : Window
    {
        /// <summary>
        /// Convert selected images to another format.
        /// </summary>
        void ConvertMediaExec (object sender, ExecutedRoutedEventArgs e)
        {
            if (ViewModel.IsArchive)
                return;
            var source = from entry in CurrentDirectory.SelectedItems.Cast<EntryViewModel>()
                         where entry.Type == "image" || entry.Type == "audio"
                         select entry.Source;
            if (!source.Any())
            {
                PopupError (guiStrings.MsgNoMediaFiles, guiStrings.TextMediaConvertError);
                return;
            }
            var convert_dialog = new ConvertMedia();

            string destination = ViewModel.Path.First();
            if (ViewModel.IsArchive)
                destination = Path.GetDirectoryName (destination);
            if (!IsWritableDirectory (destination) && Directory.Exists (Settings.Default.appLastDestination))
                destination = Settings.Default.appLastDestination;
            convert_dialog.DestinationDir.Text = destination;

            convert_dialog.Owner = this;
            var result = convert_dialog.ShowDialog() ?? false;
            if (!result)
                return;
            var format = convert_dialog.ImageConversionFormat.SelectedItem as ImageFormat;
            if (null == format)
            {
                Trace.WriteLine ("Format is not selected", "ConvertMediaExec");
                return;
            }
            try
            {
                destination = convert_dialog.DestinationDir.Text;
                Directory.SetCurrentDirectory (destination);
                var converter = new GarConvertMedia (this);
                converter.IgnoreErrors = convert_dialog.IgnoreErrors.IsChecked ?? false;
                converter.Convert (source, format);
                Settings.Default.appLastDestination = destination;
            }
            catch (Exception X)
            {
                PopupError (X.Message, guiStrings.TextMediaConvertError);
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        static extern bool GetVolumeInformation (string rootName, string volumeName, uint volumeNameSize,
                                                 IntPtr serialNumber, IntPtr maxComponentLength, 
                                                 out uint flags, string fs, uint fs_size);

        bool IsWritableDirectory (string path)
        {
            var root = Path.GetPathRoot (path);
            if (null == root)
                return false;
            uint flags;
            if (!GetVolumeInformation (root, null, 0, IntPtr.Zero, IntPtr.Zero, out flags, null, 0))
                return false;
            return (flags & 0x00080000) == 0; // FILE_READ_ONLY_VOLUME
        }
    }

    internal class GarConvertMedia
    {
        private MainWindow      m_main;
        private ProgressDialog  m_progress_dialog;
        private IEnumerable<Entry> m_source;
        private ImageFormat     m_image_format;
        private Exception       m_pending_error;
        private List<Tuple<string,string>> m_failed = new List<Tuple<string,string>>();

        public bool IgnoreErrors { get; set; }
        public IEnumerable<Tuple<string,string>> FailedFiles { get { return m_failed; } }

        public GarConvertMedia (MainWindow parent)
        {
            m_main = parent;
        }

        public void Convert (IEnumerable<Entry> images, ImageFormat format)
        {
            m_main.StopWatchDirectoryChanges();
            m_source = images;
            m_image_format = format;
            m_progress_dialog = new ProgressDialog ()
            {
                WindowTitle = guiStrings.TextTitle,
                Text        = "Converting image",
                Description = "",
                MinimizeBox = true,
            };
            m_progress_dialog.DoWork += ConvertWorker;
            m_progress_dialog.RunWorkerCompleted += OnConvertComplete;
            m_progress_dialog.ShowDialog (m_main);
        }

        void ConvertWorker (object sender, DoWorkEventArgs e)
        {
            m_pending_error = null;
            int total = m_source.Count();
            int i = 0;
            foreach (var entry in m_source)
            {
                if (m_progress_dialog.CancellationPending)
                {
                    m_pending_error = new OperationCanceledException();
                    break;
                }
                var filename = entry.Name;
                int progress = i++*100/total;
                m_progress_dialog.ReportProgress (progress, string.Format (guiStrings.MsgConvertingFile,
                    Path.GetFileName (filename)), null);
                try
                {
                    if ("image" == entry.Type)
                        ConvertImage (filename);
                    else if ("audio" == entry.Type)
                        ConvertAudio (filename);
                }
                catch (Exception X)
                {
                    if (!IgnoreErrors)
                    {
                        var error_text = string.Format (guiStrings.TextErrorConverting, entry.Name, X.Message);
                        var result = m_main.Dispatcher.Invoke (() => m_main.ShowErrorDialog (guiStrings.TextMediaConvertError, error_text, m_progress_dialog.GetWindowHandle()));
                        if (!result.Continue)
                            break;
                        IgnoreErrors = result.IgnoreErrors;
                    }
                    m_failed.Add (Tuple.Create (Path.GetFileName (filename), X.Message));
                }
            }
        }

        public static readonly HashSet<string> CommonAudioFormats = new HashSet<string> { "wav", "mp3", "ogg" };

        void ConvertAudio (string filename)
        {
            using (var file = BinaryStream.FromFile (filename))
            using (var input = AudioFormat.Read (file))
            {
                if (null == input)
                    return;
                string output_name = Path.GetFileName (filename);
                var source_ext = Path.GetExtension (filename).TrimStart ('.').ToLowerInvariant();
                string source_format = input.SourceFormat;
                if (CommonAudioFormats.Contains (source_format))
                {
                    if (source_ext == source_format)
                        return;
                    output_name = Path.ChangeExtension (output_name, source_format);
                    using (var output = CreateNewFile (output_name))
                    {
                        input.Source.Position = 0;
                        input.Source.CopyTo (output);
                    }
                }
                else
                {
                    if (source_ext == "wav")
                        return;
                    output_name = Path.ChangeExtension (output_name, "wav");
                    using (var output = CreateNewFile (output_name))
                        AudioFormat.Wav.Write (input, output);
                }
            }
        }

        void ConvertImage (string filename)
        {
            string source_ext = Path.GetExtension (filename).TrimStart ('.').ToLowerInvariant();
            string target_name = Path.GetFileName (filename);
            string target_ext = m_image_format.Extensions.FirstOrDefault();
            target_name = Path.ChangeExtension (target_name, target_ext);
            using (var file = BinaryStream.FromFile (filename))
            {
                var src_format = ImageFormat.FindFormat (file);
                if (null == src_format)
                    return;
                if (src_format.Item1 == m_image_format && m_image_format.Extensions.Any (ext => ext == source_ext))
                    return;
                file.Position = 0;
                var image = src_format.Item1.Read (file, src_format.Item2);
                try
                {
                    using (var output = CreateNewFile (target_name))
                        m_image_format.Write (output, image);
                }
                catch // delete destination file on conversion failure
                {
                    File.Delete (target_name);
                    throw;
                }
            }
        }

        /// <summary>
        /// Creates new file with specified filename, or, if it's already exists, tries to open
        /// files named "FILENAME.1.EXT", "FILENAME.2.EXT" and so on.
        /// <exception cref="System.IOException">Throws exception after 100th failed attempt.</exception>
        /// </summary>

        public static Stream CreateNewFile (string filename)
        {
            string name = filename;
            var ext = new Lazy<string> (() => Path.GetExtension (filename));
            for (int attempt = 1; ; ++attempt)
            {
                try
                {
                    return File.Open (name, FileMode.CreateNew);
                }
                catch (IOException) // file already exists
                {
                    if (100 == attempt) // limit number of attempts
                        throw;
                }
                name = Path.ChangeExtension (filename, attempt.ToString()+ext.Value);
            }
        }

        void OnConvertComplete (object sender, RunWorkerCompletedEventArgs e)
        {
            m_main.ResumeWatchDirectoryChanges();
            m_progress_dialog.Dispose();
            if (null != m_pending_error)
            {
                if (m_pending_error is OperationCanceledException)
                    m_main.SetStatusText (m_pending_error.Message);
                else
                    m_main.PopupError (m_pending_error.Message, guiStrings.TextMediaConvertError);
            }
            m_main.Activate();
            m_main.RefreshView();
        }
    }
}
