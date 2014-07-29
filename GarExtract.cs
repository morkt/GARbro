//! \file       GarExtract.cs
//! \date       Fri Jul 25 05:52:27 2014
//! \brief      Extract archive frontend.
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
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using Ookii.Dialogs.Wpf;
using GameRes;
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
                return;
            try
            {
                if (!ViewModel.IsArchive)
                {
                    if (!entry.IsDirectory)
                    {
                        var arc_dir = CurrentPath;
                        var source = Path.Combine (arc_dir, entry.Name);
                        string destination = arc_dir;
                        // extract into directory named after archive
                        if (!string.IsNullOrEmpty (Path.GetExtension (entry.Name)))
                            destination = Path.GetFileNameWithoutExtension (source);
                        ExtractArchive (source, destination);
                    }
                }
                else if (null != m_app.CurrentArchive)
                {
                    var vm = ViewModel as ArchiveViewModel;
                    string destination = Path.GetDirectoryName (vm.Path);
                    string arc_name = Path.GetFileName (vm.Path);
                    if (null == entry || (entry.Name == ".." && vm.SubDir == "")) // root entry
                    {
                        ExtractArchive (m_app.CurrentArchive, arc_name, destination);
                    }
                    else
                    {
                        ExtractFileFromArchive (entry, destination);
                    }
                }
            }
            catch (Exception X)
            {
                PopupError (X.Message, guiStrings.MsgErrorExtracting);
            }
        }

        private void ExtractArchive (string path, string destination)
        {
            string arc_name = Path.GetFileName (path);
            FormatCatalog.Instance.LastError = null;
            var arc = ArcFile.TryOpen (path);
            if (null != arc)
            {
                ExtractArchive (arc, arc_name, destination);
            }
            else
            {
                string error_message;
                if (FormatCatalog.Instance.LastError != null)
                    error_message = FormatCatalog.Instance.LastError.Message;
                else
                    error_message = guiStrings.MsgUnknownFormat;
                SetStatusText (string.Format ("{1}: {0}", error_message, arc_name));
            }
        }

        private void ExtractArchive (ArcFile arc, string arc_name, string destination)
        {
            if (0 == arc.Dir.Count)
            {
                SetStatusText (string.Format ("{1}: {0}", guiStrings.MsgEmptyArchive, arc_name));
                return;
            }
            var extractDialog = new ExtractArchiveDialog (arc_name, destination);
            extractDialog.Owner = this;
            var result = extractDialog.ShowDialog();
            if (!result.Value)
                return;

            destination = extractDialog.Destination;
            if (!string.IsNullOrEmpty (destination))
            {
                destination = Path.GetFullPath (destination);
                Trace.WriteLine (destination, "Extract destination");
                StopWatchDirectoryChanges();
                try
                {
                    Directory.CreateDirectory (destination);
                    Directory.SetCurrentDirectory (destination);
                }
                finally
                {
                    ResumeWatchDirectoryChanges();
                }
            }
            IEnumerable<Entry> file_list = arc.Dir;
            bool skip_images = !extractDialog.ExtractImages.IsChecked.Value;
            bool skip_script = !extractDialog.ExtractText.IsChecked.Value;
            if (skip_images || skip_script)
                file_list = file_list.Where (f => !(skip_images && f.Type == "image") && !(skip_script && f.Type == "script"));

            if (!file_list.Any())
            {
                SetStatusText (string.Format ("{1}: {0}", guiStrings.MsgNoFiles, arc_name));
                return;
            }
            ImageFormat image_format = null;
            if (!skip_images)
                image_format = extractDialog.GetImageFormat (extractDialog.ImageConversionFormat);

            SetStatusText (string.Format(guiStrings.MsgExtractingTo, arc_name, destination));
            ExtractFilesFromArchive (string.Format (guiStrings.MsgExtractingArchive, arc_name),
                                     arc, file_list, image_format);
        }

        private void ExtractFileFromArchive (EntryViewModel entry, string destination)
        {
            var vm = ViewModel as ArchiveViewModel;
            var selected = CurrentDirectory.SelectedItems;
            IEnumerable<Entry> file_list = new Entry[0];
            foreach (var e in selected.Cast<EntryViewModel>())
            {
                file_list = file_list.Concat (vm.GetFiles (e));
            }

            string arc_name = Path.GetFileName (CurrentPath);
            ExtractDialog extractDialog;
            if (file_list.Skip (1).Any())
                extractDialog = new ExtractArchiveDialog (arc_name, destination);
            else
                extractDialog = new ExtractFile (entry, destination);
            extractDialog.Owner = this;
            var result = extractDialog.ShowDialog();
            if (!result.Value)
                return;

            destination = extractDialog.Destination;
            if (!string.IsNullOrEmpty (destination))
            {
                destination = Path.GetFullPath (destination);
                Directory.CreateDirectory (destination);
                Directory.SetCurrentDirectory (destination);
            }
            ImageFormat format = FormatCatalog.Instance.ImageFormats.FirstOrDefault (f => f.Tag.Equals (Settings.Default.appImageFormat));

            ExtractFilesFromArchive (string.Format (guiStrings.MsgExtractingFile, arc_name),
                                     m_app.CurrentArchive, file_list, format);
        }

        private void ExtractFilesFromArchive (string text, ArcFile arc, IEnumerable<Entry> file_list,
                                              ImageFormat image_format = null)
        {
            file_list = file_list.OrderBy (e => e.Offset);
            var extractProgressDialog = new ProgressDialog ()
            {
                WindowTitle = guiStrings.TextTitle,
                Text        = text,
                Description = "",
                MinimizeBox = true,
            };
            if (!file_list.Skip (1).Any()) // 1 == file_list.Count()
            {
                extractProgressDialog.Description = file_list.First().Name;
                extractProgressDialog.ProgressBarStyle = ProgressBarStyle.MarqueeProgressBar;
            }
            extractProgressDialog.DoWork += (s, e) =>
            {
                try
                {
                    int total = file_list.Count();
                    int i = 0;
                    foreach (var entry in file_list)
                    {
                        if (extractProgressDialog.CancellationPending)
                            break;
                        if (total > 1)
                            extractProgressDialog.ReportProgress (i*100/total, null, entry.Name);
                        if (null != image_format && entry.Type == "image")
                            ExtractImage (arc, entry, image_format);
                        else
                            arc.Extract (entry);
                        ++i;
                    }
                    SetStatusText (string.Format (guiStrings.MsgExtractCompletePlural, i,
                                                  Localization.Plural (i, "file")));
                }
                catch (Exception X)
                {
                    SetStatusText (X.Message);
                }
            };
            extractProgressDialog.RunWorkerCompleted += (s, e) => {
                extractProgressDialog.Dispose();
                if (!ViewModel.IsArchive)
                {
                    arc.Dispose();
                    Dispatcher.Invoke (RefreshView);
                }
            };
            extractProgressDialog.ShowDialog (this);
        }
    }
}
