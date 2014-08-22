//! \file       GarConvert.cs
//! \date       Fri Aug 22 08:22:47 2014
//! \brief      Game resources conversion methods.
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
using Ookii.Dialogs.Wpf;

namespace GARbro.GUI
{
    public partial class MainWindow : Window
    {
        /// <summary>
        /// Convert selected images to another format.
        /// </summary>
        void ConvertImageExec (object sender, ExecutedRoutedEventArgs e)
        {
            if (ViewModel.IsArchive)
                return;
            var source = CurrentDirectory.SelectedItems.Cast<EntryViewModel>()
                         .Where (f => f.Type == "image").Select (f => f.Name);
            var convert_dialog = new ConvertImages();
            convert_dialog.Owner = this;
            var result = convert_dialog.ShowDialog() ?? false;
            if (!result)
                return;
            var selected = convert_dialog.ImageConversionFormat.SelectedValue as string;
            var format = FormatCatalog.Instance.ImageFormats.FirstOrDefault (f => f.Tag == selected);
            if (null == format)
            {
                Trace.WriteLine ("Format is not selected", "ConvertImageExec");
                return;
            }
            try
            {
                Directory.SetCurrentDirectory (ViewModel.Path);
                var converter = new GarConvertImages (this);
                converter.Convert (source, format);
            }
            catch (Exception X)
            {
                PopupError (X.Message, guiStrings.TextImageConvertError);
            }
        }
    }

    internal class GarConvertImages
    {
        private MainWindow      m_main;
        private ProgressDialog  m_progress_dialog;
        private IEnumerable<string> m_source;
        private ImageFormat     m_image_format;
        private Exception       m_pending_error;

        public bool IgnoreErrors { get; set; }

        public GarConvertImages (MainWindow parent)
        {
            m_main = parent;
        }

        public void Convert (IEnumerable<string> images, ImageFormat format)
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
            try
            {
                string target_ext = m_image_format.Extensions.First();
                int total = m_source.Count();
                int i = 0;
                foreach (var filename in m_source)
                {
                    if (m_progress_dialog.CancellationPending)
                        throw new OperationCanceledException();
                    int progress = i++*100/total;
                    string target_name = Path.ChangeExtension (filename, target_ext);
                    if (filename == target_name)
                        continue;
                    string source_ext = Path.GetExtension (filename).TrimStart ('.').ToLowerInvariant();
                    if (m_image_format.Extensions.Any (ext => ext == source_ext))
                        continue;
                    try
                    {
                        using (var file = File.OpenRead (filename))
                        {
                            m_progress_dialog.ReportProgress (progress, string.Format (guiStrings.MsgConvertingImage,
                                filename), target_name);
                            var image = ImageFormat.Read (file);
                            if (null == image)
                                continue;
                            try
                            {
                                using (var output = File.Create (target_name))
                                    m_image_format.Write (output, image);
                            }
                            catch // delete destination file on conversion failure
                            {
                                File.Delete (target_name);
                                throw;
                            }
                        }
                    }
                    catch (Exception X)
                    {
                        if (!IgnoreErrors)
                            throw;
                        m_pending_error = X;
                    }
                }
            }
            catch (Exception X)
            {
                m_pending_error = X;
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
                    m_main.PopupError (m_pending_error.Message, guiStrings.TextImageConvertError);
            }
            m_main.Activate();
            m_main.RefreshView();
        }
    }
}
