//! \file       ViewModel.cs
//! \date       Wed Jul 02 07:29:11 2014
//! \brief      GARbro directory list.
//

using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Text.RegularExpressions;
using System.Globalization;
using GameRes;
using GARbro.GUI.Strings;

namespace GARbro.GUI
{
    public class SubDirEntry : GameRes.Entry
    {
        public override string Type  { get { return guiStrings.TextDirType; } }

        public SubDirEntry (string name)
        {
            Name = name;
            Size = 0;
        }
    }

    public class DirectoryViewModel : ObservableCollection<EntryViewModel>
    {
        public string Path { get; private set; }
        public ICollection<Entry> Source { get; private set; }
        public virtual bool IsArchive { get { return false; } }

        public DirectoryViewModel (string path, ICollection<Entry> filelist)
        {
            Path = path;
            Source = filelist;
            ImportFromSource();
        }

        protected virtual void ImportFromSource ()
        {
            if (!string.IsNullOrEmpty (Path) && null != Directory.GetParent (Path))
            {
                Add (new EntryViewModel (new SubDirEntry (".."), -2));
            }
            foreach (var entry in Source)
            {
                int prio = null == entry as SubDirEntry ? 0 : -1;
                Add (new EntryViewModel (entry, prio));
            }
        }

        public EntryViewModel Find (string name)
        {
            return this.FirstOrDefault (e => e.Name.Equals (name, System.StringComparison.OrdinalIgnoreCase));
        }

        public virtual void SetPosition (DirectoryPosition pos)
        {
        }
    }

    public class ArchiveViewModel : DirectoryViewModel
    {
        public override bool IsArchive { get { return true; } }
        public string SubDir { get; protected set; }

        public ArchiveViewModel (string path, ArcFile arc)
            : base (path, arc.Dir)
        {
        }

        protected override void ImportFromSource ()
        {
            UpdateModel ("");
        }

        private string m_delimiter = "/";
        private static readonly char[] m_path_delimiters = { '/', '\\' };

        public void ChDir (string subdir)
        {
            string new_path;
            if (".." == subdir)
            {
                if (0 == SubDir.Length)
                    return;
                var path = SubDir.Split (m_path_delimiters);
                if (path.Length > 1)
                    new_path = string.Join (m_delimiter, path, 0, path.Length-1);
                else
                    new_path = "";
            }
            else
            {
                var entry = this.FirstOrDefault (e => e.Name.Equals (subdir, StringComparison.OrdinalIgnoreCase));
                if (null == entry)
                    throw new DirectoryNotFoundException (string.Format ("{1}: {0}", guiStrings.MsgDirectoryNotFound, subdir));
                if (SubDir.Length > 0)
                    new_path = SubDir + m_delimiter + entry.Name;
                else
                    new_path = entry.Name;
            }
            UpdateModel (new_path);
        }

        static readonly Regex path_re = new Regex (@"\G[/\\]?([^/\\]+)([/\\])");

        private void UpdateModel (string root_path)
        {
            IEnumerable<Entry> dir = Source;
            if (!string.IsNullOrEmpty (root_path))
                dir = from entry in dir
                      where entry.Name.StartsWith (root_path+m_delimiter)
                      select entry;
            if (!dir.Any())
            {
                throw new DirectoryNotFoundException (string.Format ("{1}: {0}", guiStrings.MsgDirectoryNotFound, root_path));
            }
            m_suppress_notification = true;
            try
            {
                this.Clear();
                SubDir = root_path;
                Add (new EntryViewModel (new SubDirEntry (".."), -2));
                var subdirs = new HashSet<string>();
                foreach (var entry in dir)
                {
                    var match = path_re.Match (entry.Name, root_path.Length);
                    if (match.Success)
                    {
                        string name = match.Groups[1].Value;
                        if (subdirs.Add (name))
                        {
                            m_delimiter = match.Groups[2].Value;
                            Add (new EntryViewModel (new SubDirEntry (name), -1));
                        }
                    }
                    else
                    {
                        Add (new EntryViewModel (entry, 0));
                    }
                }
            }
            finally
            {
                m_suppress_notification = false;
                OnCollectionChanged (new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
            }
        }

        public override void SetPosition (DirectoryPosition pos)
        {
            UpdateModel (pos.ArchivePath);
        }

        public IEnumerable<Entry> GetFiles (EntryViewModel entry)
        {
            if (!entry.IsDirectory)
                return new Entry[] { entry.Source };

            string path = GetPath (entry.Name);
            return from file in Source
                   where file.Name.StartsWith (path)
                   select file;
        }

        string GetPath (string dir)
        {
            if (SubDir.Length > 0)
                return SubDir + m_delimiter + dir + m_delimiter;
            else
                return dir + m_delimiter;
        }

        private bool m_suppress_notification = false;

        protected override void OnCollectionChanged (NotifyCollectionChangedEventArgs e)
        {
            if (!m_suppress_notification)
                base.OnCollectionChanged(e);
        }
    }

    public class EntryViewModel
    {
        public EntryViewModel (Entry entry, int priority)
        {
            Source = entry;
            Name = Path.GetFileName (entry.Name);
            Priority = priority;
        }

        public Entry Source { get; private set; }

        public string Name { get; private set; }
        public string Type { get { return Source.Type; } }
        public uint   Size { get { return Source.Size; } }
        public int    Priority { get; private set; }
        public bool   IsDirectory { get { return Priority < 0; } }
    }

    public sealed class FileSystemComparer : IComparer
    {
        private string              m_property;
        private int                 m_direction;
        private static Comparer     s_default_comparer = new Comparer (CultureInfo.CurrentUICulture);

        public FileSystemComparer (string property, ListSortDirection direction)
        {
            m_property = property;
            m_direction = direction == ListSortDirection.Ascending ? 1 : -1;
        }

        public int Compare (object a, object b)
        {
            var v_a = a as EntryViewModel;
            var v_b = b as EntryViewModel;
            if (null == v_a || null == v_b)
                return s_default_comparer.Compare (a, b) * m_direction;

            if (v_a.Priority < v_b.Priority)
                return -1;
            if (v_a.Priority > v_b.Priority)
                return 1;
            if (string.IsNullOrEmpty (m_property))
                return 0;
            int order;
            if (m_property != "Name")
            {
                if ("Type" == m_property)
                {
                    // empty strings placed in the end
                    if (string.IsNullOrEmpty (v_a.Type))
                        order = string.IsNullOrEmpty (v_b.Type) ? 0 : m_direction;
                    else if (string.IsNullOrEmpty (v_b.Type))
                        order = -m_direction;
                    else
                        order = string.Compare (v_a.Type, v_b.Type, true) * m_direction;
                }
                else
                {
                    var prop_a = a.GetType ().GetProperty (m_property).GetValue (a);
                    var prop_b = b.GetType ().GetProperty (m_property).GetValue (b);
                    order = s_default_comparer.Compare (prop_a, prop_b) * m_direction;
                }
                if (0 == order)
                    order = NativeMethods.StrCmpLogicalW (v_a.Name, v_b.Name);
            }
            else
                order = NativeMethods.StrCmpLogicalW (v_a.Name, v_b.Name) * m_direction;
            return order;
        }
    }

    /// <summary>
    /// Image format model for formats drop-down list widgets.
    /// </summary>
    public class ImageFormatModel
    {
        public ImageFormat Source { get; private set; }
        public string Tag {
            get { return null != Source ? Source.Tag : guiStrings.TextAsIs; }
        }

        public ImageFormatModel (ImageFormat impl = null)
        {
            Source = impl;
        }
    }

    /// <summary>
    /// Stores current position within directory view model.
    /// </summary>
    public class DirectoryPosition
    {
        public string           Path { get; set; }
        public string    ArchivePath { get; set; }
        public EntryViewModel   Item { get; set; }

        public DirectoryPosition (DirectoryViewModel vm, EntryViewModel item)
        {
            Path = vm.Path;
            Item = item;
            if (vm.IsArchive)
                ArchivePath = (vm as ArchiveViewModel).SubDir;
            else
                ArchivePath = "";
        }

        public DirectoryPosition (string filename)
        {
            Path = System.IO.Path.GetDirectoryName (filename);
            ArchivePath = "";
            var entry = FormatCatalog.Instance.CreateEntry (filename);
            Item = new EntryViewModel (entry, 0);
        }
    }
}
