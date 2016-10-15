//! \file       ViewModel.cs
//! \date       Wed Jul 02 07:29:11 2014
//! \brief      GARbro directory list.
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
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Text.RegularExpressions;
using System.Globalization;
using System.Windows.Data;
using GameRes;
using GARbro.GUI.Strings;

namespace GARbro.GUI
{
    public class DirectoryViewModel : ObservableCollection<EntryViewModel>
    {
        public IReadOnlyList<string> Path { get; private set; }
        public IEnumerable<Entry>  Source { get; private set; }
        public bool             IsArchive { get; private set; }

        public DirectoryViewModel (IEnumerable<string> path, IEnumerable<Entry> filelist, bool is_archive)
        {
            Path = path.ToList();
            Source = filelist;
            IsArchive = is_archive;
            ImportFromSource();
        }

        protected void ImportFromSource ()
        {
            var last_dir = Path.Last();
            if (IsArchive || !string.IsNullOrEmpty (last_dir) && null != Directory.GetParent (last_dir))
            {
                Add (new EntryViewModel (new SubDirEntry (".."), -2));
            }
            foreach (var entry in Source)
            {
                int prio = entry is SubDirEntry ? -1 : 0;
                Add (new EntryViewModel (entry, prio));
            }
        }

        public EntryViewModel Find (string name)
        {
            return this.FirstOrDefault (e => e.Name.Equals (name, StringComparison.InvariantCultureIgnoreCase));
        }
    }

    public class EntryViewModel : INotifyPropertyChanged
    {
        public EntryViewModel (Entry entry, int priority)
        {
            Source = entry;
            Name = SafeGetFileName (entry.Name);
            Priority = priority;
        }

        static char[] SeparatorCharacters = { '\\', '/', ':' };

        /// <summary>
        /// Same as Path.GetFileName, but ignores invalid charactes
        /// </summary>
        string SafeGetFileName (string filename)
        {
            var name_start = filename.LastIndexOfAny (SeparatorCharacters);
            if (-1 == name_start)
                return filename;
            return filename.Substring (name_start+1);
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public Entry Source { get; private set; }

        public string Name { get; private set; }
        public string Type
        {
            get { return Source.Type; }
            set
            {
                if (Source.Type != value)
                {
                    Source.Type = value;
                    OnPropertyChanged ("Type");
                }
            }
        }
        public uint?  Size { get { return IsDirectory ? null : (uint?)Source.Size; } }
        public int    Priority { get; private set; }
        public bool   IsDirectory { get { return Priority < 0; } }

        private void OnPropertyChanged (string property = "")
        {
            if (PropertyChanged != null)
            {
                PropertyChanged (this, new PropertyChangedEventArgs (property));
            }
        }
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
                    order = CompareNames (v_a.Name, v_b.Name);
            }
            else
                order = CompareNames (v_a.Name, v_b.Name) * m_direction;
            return order;
        }

        static int CompareNames (string a, string b)
        {
//            return NativeMethods.StrCmpLogicalW (a, b);
            return string.Compare (a, b, StringComparison.CurrentCultureIgnoreCase);
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
        public IEnumerable<string> Path { get; set; }
        public string              Item { get; set; }

        public DirectoryPosition (DirectoryViewModel vm, EntryViewModel item)
        {
            Path = vm.Path;
            Item = null != item ? item.Name : null;
        }

        public DirectoryPosition (string filename)
        {
            Path = new string[] { System.IO.Path.GetDirectoryName (filename) };
            Item = System.IO.Path.GetFileName (filename);
        }
    }

    public class EntryTypeConverter : IValueConverter
    {
        public object Convert (object value, Type targetType, object parameter, CultureInfo culture)
        {
            var type = value as string;
            if (!string.IsNullOrEmpty (type))
            {
                var translation = guiStrings.ResourceManager.GetString ("Type_"+type, guiStrings.Culture);
                if (!string.IsNullOrEmpty (translation))
                    return translation;
            }
            return value;
        }

        public object ConvertBack (object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
