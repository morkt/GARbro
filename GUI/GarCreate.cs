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
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using GameRes;
using GARbro.GUI.Strings;
using GARbro.GUI.Properties;

namespace GARbro.GUI
{
    public partial class MainWindow : Window
    {
        private void CreateArchiveExec (object sender, ExecutedRoutedEventArgs args)
        {
            StopWatchDirectoryChanges();
            try
            {
                var archive_creator = new GarCreate (this);
                if (!archive_creator.Run())
                    ResumeWatchDirectoryChanges();
            }
            catch (Exception X)
            {
                ResumeWatchDirectoryChanges();
                PopupError (X.Message, guiStrings.TextCreateArchiveError);
            }
        }
    }

    internal class GarCreate : GarOperation
    {
        private string          m_arc_name;
        private IList<Entry>    m_file_list;
        private ArchiveFormat   m_format;
        private ResourceOptions m_options;

        delegate void AddFilesEnumerator (IList<Entry> list, string path, DirectoryInfo path_info);

        public GarCreate (MainWindow parent) : base (parent, guiStrings.TextCreateArchiveError)
        {
            m_arc_name = Settings.Default.appLastCreatedArchive;
        }

        public bool Run ()
        {
            Directory.SetCurrentDirectory (m_main.CurrentPath);
            var items = m_main.CurrentDirectory.SelectedItems.Cast<EntryViewModel> ();
            if (string.IsNullOrEmpty (m_arc_name))
            {
                m_arc_name = Path.GetFileName (m_main.CurrentPath);
                if (!items.Skip (1).Any()) // items.Count() == 1
                {
                    var item = items.First();
                    if (item.IsDirectory)
                        m_arc_name = Path.GetFileNameWithoutExtension (item.Name);
                }
            }

            var dialog = new CreateArchiveDialog (m_arc_name);
            dialog.Owner = m_main;
            if (!dialog.ShowDialog().Value)
            {
                return false;
            }
            if (string.IsNullOrEmpty (dialog.ArchiveName.Text))
            {
                m_main.SetStatusText ("Archive name is empty");
                return false;
            }
            m_format = dialog.ArchiveFormat.SelectedItem as ArchiveFormat;
            if (null == m_format)
            {
                m_main.SetStatusText ("Format is not selected");
                return false;
            }
            m_options = dialog.ArchiveOptions;

            if (m_format.IsHierarchic)
                m_file_list = BuildFileList (items, AddFilesRecursive);
            else
                m_file_list = BuildFileList (items, AddFilesFromDir);

            m_arc_name = Path.GetFullPath (dialog.ArchiveName.Text);

            m_progress_dialog = new ProgressDialog ()
            {
                WindowTitle = guiStrings.TextTitle,
                Text        = string.Format (guiStrings.MsgCreatingArchive, Path.GetFileName (m_arc_name)),
                Description = "",
                MinimizeBox = true,
            };
            m_progress_dialog.DoWork += CreateWorker;
            m_progress_dialog.RunWorkerCompleted += OnCreateComplete;
            m_progress_dialog.ShowDialog (m_main);
            return true;
        }

        private int m_total = 1;

        ArchiveOperation CreateEntryCallback (int i, Entry entry, string msg)
        {
            if (m_progress_dialog.CancellationPending)
                throw new OperationCanceledException();
            if (null == entry && null == msg && 0 != i)
            {
                m_total = i;
                m_progress_dialog.ReportProgress (0);
                return ArchiveOperation.Continue;
            }
            int progress = i*100/m_total;
            if (progress > 100)
                progress = 100;
            string notice = msg;
            if (null != entry)
            {
                if (null != msg)
                    notice = string.Format ("{0} {1}", msg, entry.Name);
                else
                    notice = entry.Name;
            }
            m_progress_dialog.ReportProgress (progress, null, notice);
            return ArchiveOperation.Continue;
        }

        void CreateWorker (object sender, DoWorkEventArgs e)
        {
            m_pending_error = null;
            try
            {
                using (var tmp_file = new GARbro.Shell.TemporaryFile (Path.GetDirectoryName (m_arc_name),
                                                                    Path.GetRandomFileName ()))
                {
                    m_total = m_file_list.Count() + 1;
                    using (var file = File.Create (tmp_file.Name))
                    {
                        m_format.Create (file, m_file_list, m_options, CreateEntryCallback);
                    }
                    if (!GARbro.Shell.File.Rename (tmp_file.Name, m_arc_name))
                    {
                        throw new Win32Exception (GARbro.Shell.File.GetLastError());
                    }
                }
            }
            catch (Exception X)
            {
                m_pending_error = X;
            }
        }

        void OnCreateComplete (object sender, RunWorkerCompletedEventArgs e)
        {
            m_progress_dialog.Dispose();
            m_main.Activate();
            if (null == m_pending_error)
            {
                Settings.Default.appLastCreatedArchive = m_arc_name;
                m_main.Dispatcher.Invoke (() => {
                    m_main.ChangePosition (new DirectoryPosition (m_arc_name));
                });
            }
            else
            {
                if (m_pending_error is OperationCanceledException)
                    m_main.SetStatusText (m_pending_error.Message);
                else
                    m_main.PopupError (m_pending_error.Message, guiStrings.TextCreateArchiveError);
            }
        }

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
                    var e = new Entry {
                        Name = entry.Name,
                        Type = entry.Source.Type,
                        Size = entry.Source.Size,
                    };
                    list.Add (e);
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
                var e = FormatCatalog.Instance.Create<Entry> (name);
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
