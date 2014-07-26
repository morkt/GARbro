//! \file       GarCreate.cs
//! \date       Fri Jul 25 05:56:29 2014
//! \brief      Create archive frontend.
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
using GameRes;
using GARbro.GUI.Strings;
using Ookii.Dialogs.Wpf;

namespace GARbro.GUI
{
    public partial class MainWindow : Window
    {
        private void CreateArchiveExec (object sender, ExecutedRoutedEventArgs args)
        {
            StopWatchDirectoryChanges();
            try
            {
                Directory.SetCurrentDirectory (CurrentPath);
                var items = CurrentDirectory.SelectedItems.Cast<EntryViewModel>();
                string arc_name = Path.GetFileName (CurrentPath);
                if (!items.Skip (1).Any()) // items.Count() == 1
                {
                    var item = items.First();
                    if (item.IsDirectory)
                        arc_name = Path.GetFileNameWithoutExtension (item.Name);
                }

                var dialog = new CreateArchiveDialog (arc_name);
                dialog.Owner = this;
                if (!dialog.ShowDialog().Value)
                    return;
                if (string.IsNullOrEmpty (dialog.ArchiveName.Text))
                {
                    SetStatusText ("Archive name is empty");
                    return;
                }
                var format = dialog.ArchiveFormat.SelectedItem as ArchiveFormat;
                if (null == format)
                {
                    SetStatusText ("Format is not selected");
                    return;
                }

                IList<Entry> file_list;
                if (format.IsHierarchic)
                    file_list = BuildFileList (items, AddFilesRecursive);
                else
                    file_list = BuildFileList (items, AddFilesFromDir);

                arc_name = Path.GetFullPath (dialog.ArchiveName.Text);

                var createProgressDialog = new ProgressDialog ()
                {
                    WindowTitle = guiStrings.TextTitle,
                    Text        = string.Format (guiStrings.MsgCreatingArchive, Path.GetFileName (arc_name)),
                    Description = "",
                    MinimizeBox = true,
                };
                createProgressDialog.DoWork += (s, e) =>
                {
                    try
                    {
                        using (var tmp_file = new GARbro.Shell.TemporaryFile (Path.GetDirectoryName (arc_name),
                                                                            Path.GetRandomFileName ()))
                        {
                            int total = file_list.Count() + 1;
                            using (var file = File.Create (tmp_file.Name))
                            {
                                format.Create (file, file_list, dialog.ArchiveOptions, (i, entry, msg) =>
                                {
                                    if (createProgressDialog.CancellationPending)
                                        throw new OperationCanceledException();
                                    int progress = i*100/total;
                                    string notice = msg;
                                    if (null != entry)
                                    {
                                        if (null != msg)
                                            notice = string.Format ("{0} {1}", msg, entry.Name);
                                        else
                                            notice = entry.Name;
                                    }
                                    createProgressDialog.ReportProgress (progress, null, notice);
                                    return ArchiveOperation.Continue;
                                });
                            }
                            GARbro.Shell.File.Rename (tmp_file.Name, arc_name);
                        }
                    }
                    catch (OperationCanceledException X)
                    {
                        m_watcher.EnableRaisingEvents = true;
                        SetStatusText (X.Message);
                    }
                    catch (Exception X)
                    {
                        m_watcher.EnableRaisingEvents = true;
                        PopupError (X.Message, guiStrings.TextCreateArchiveError);
                    }
                };
                createProgressDialog.RunWorkerCompleted += (s, e) => {
                    createProgressDialog.Dispose();
                    Dispatcher.Invoke (() => SetCurrentPosition (new DirectoryPosition (arc_name)));
                };
                createProgressDialog.ShowDialog (this);
            }
            catch (Exception X)
            {
                m_watcher.EnableRaisingEvents = true;
                PopupError (X.Message, guiStrings.TextCreateArchiveError);
            }
        }

        delegate void AddFilesEnumerator (IList<Entry> list, string path, DirectoryInfo path_info);

        IList<Entry> BuildFileList (IEnumerable<EntryViewModel> files, AddFilesEnumerator add_files)
        {
            var list = new List<Entry>();
            foreach (var entry in files)
            {
                if (entry.IsDirectory)
                {
                    if (".." != entry.Name)
                    {
                        var dir = new DirectoryInfo (entry.Name);
                        add_files (list, entry.Name, dir);
                    }
                }
                else if (entry.Size < uint.MaxValue)
                {
                    list.Add (entry.Source);
                }
            }
            return list;
        }

        void AddFilesFromDir (IList<Entry> list, string path, DirectoryInfo dir)
        {
            foreach (var file in dir.EnumerateFiles())
            {
                if (0 != (file.Attributes & (FileAttributes.Hidden | FileAttributes.System)))
                    continue;
                if (file.Length >= uint.MaxValue)
                    continue;
                string name = Path.Combine (path, file.Name);
                var e = FormatCatalog.Instance.CreateEntry (name);
                e.Size = (uint)file.Length;
                list.Add (e);
            }
        }

        void AddFilesRecursive (IList<Entry> list, string path, DirectoryInfo info)
        {
            foreach (var dir in info.EnumerateDirectories())
            {
                string subdir = Path.Combine (path, dir.Name);
                var subdir_info = new DirectoryInfo (subdir);
                AddFilesRecursive (list, subdir, subdir_info);
            }
            AddFilesFromDir (list, path, info);
        }
    }
}
