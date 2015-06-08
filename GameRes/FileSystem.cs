//! \file       FileSystem.cs
//! \date       Fri Jun 05 15:32:27 2015
//! \brief      Gameres file system abstraction.
//
// Copyright (C) 2015 by morkt
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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace GameRes
{
    public interface IFileSystem : IDisposable
    {
        /// <summary>
        /// Open file for reading.
        /// </summary>
        Stream OpenStream (Entry entry);

        ArcView OpenView (Entry entry);

        /// <summary>
        /// Enumerates subdirectories and files in current directory.
        /// </summary>
        IEnumerable<Entry> GetFiles ();

        /// <summary>
        /// Recursively enumerates files in the current directory and its subdirectories.
        /// Subdirectory entries are omitted.
        /// </summary>
        IEnumerable<Entry> GetFilesRecursive ();

        string CurrentDirectory { get; set; }
    }

    public class SubDirEntry : Entry
    {
        public override string Type  { get { return "directory"; } }

        public SubDirEntry (string name)
        {
            Name = name;
            Size = 0;
        }
    }

    public class PhysicalFileSystem : IFileSystem
    {
        public string CurrentDirectory
        {
            get { return Directory.GetCurrentDirectory(); }
            set { Directory.SetCurrentDirectory (value); }
        }

        public IEnumerable<Entry> GetFiles ()
        {
            var info = new DirectoryInfo (CurrentDirectory);
            foreach (var subdir in info.EnumerateDirectories())
            {
                if (0 != (subdir.Attributes & (FileAttributes.Hidden | FileAttributes.System)))
                    continue;
                yield return new SubDirEntry (subdir.FullName);
            }
            foreach (var file in info.EnumerateFiles())
            {
                if (0 != (file.Attributes & (FileAttributes.Hidden | FileAttributes.System)))
                    continue;
                yield return EntryFromFileInfo (file);
            }
        }

        public IEnumerable<Entry> GetFilesRecursive ()
        {
            var info = new DirectoryInfo (CurrentDirectory);
            foreach (var file in info.EnumerateFiles ("*", SearchOption.AllDirectories))
            {
                if (0 != (file.Attributes & (FileAttributes.Hidden | FileAttributes.System)))
                    continue;
                yield return EntryFromFileInfo (file);
            }
        }

        private Entry EntryFromFileInfo (FileInfo file)
        {
            var entry = FormatCatalog.Instance.CreateEntry (file.FullName);
            entry.Size = (uint)Math.Min (file.Length, uint.MaxValue);
            return entry;
        }

        public Stream OpenStream (Entry entry)
        {
            return File.OpenRead (entry.Name);
        }

        public ArcView OpenView (Entry entry)
        {
            return new ArcView (entry.Name);
        }

        public void Dispose ()
        {
            GC.SuppressFinalize (this);
        }
    }

    public class FlatArchiveFileSystem : IFileSystem
    {
        protected ArcFile   m_arc;

        public virtual string CurrentDirectory
        {
            get { return ""; }
            set
            {
                if (string.IsNullOrEmpty (value))
                    return;
                if (".." == value || "." == value)
                    return;
                if ("\\" == value || "/" == value)
                    return;
                throw new DirectoryNotFoundException();
            }
        }

        public FlatArchiveFileSystem (ArcFile arc)
        {
            m_arc = arc;
        }

        public Stream OpenStream (Entry entry)
        {
            return m_arc.OpenEntry (entry);
        }

        public ArcView OpenView (Entry entry)
        {
            return m_arc.OpenView (entry);
        }

        public virtual IEnumerable<Entry> GetFiles ()
        {
            return m_arc.Dir;
        }

        public virtual IEnumerable<Entry> GetFilesRecursive ()
        {
            return m_arc.Dir;
        }

        #region IDisposable Members
        bool _arc_disposed = false;

        public void Dispose ()
        {
            Dispose (true);
            GC.SuppressFinalize (this);
        }

        protected virtual void Dispose (bool disposing)
        {
            if (!_arc_disposed)
            {
                if (disposing)
                {
                    m_arc.Dispose();
                }
                _arc_disposed = true;
            }
        }
        #endregion
    }

    public class ArchiveFileSystem : FlatArchiveFileSystem
    {
        private string  m_cwd;

        private string PathDelimiter { get; set; }

        private static readonly char[] m_path_delimiters = { '/', '\\' };

        public ArchiveFileSystem (ArcFile arc) : base (arc)
        {
            m_cwd = "";
            PathDelimiter = "/";
        }

        public override string CurrentDirectory
        {
            get { return m_cwd; }
            set { ChDir (value); }
        }

        static readonly Regex path_re = new Regex (@"\G[/\\]?([^/\\]+)([/\\])");

        public override IEnumerable<Entry> GetFiles ()
        {
            var root_dir = "";
            IEnumerable<Entry> dir = m_arc.Dir;
            if (!string.IsNullOrEmpty (m_cwd))
            {
                root_dir = m_cwd+PathDelimiter;
                dir = from entry in dir
                      where entry.Name.StartsWith (root_dir)
                      select entry;
                if (!dir.Any())
                {
                    throw new DirectoryNotFoundException();
                }
            }
            var subdirs = new HashSet<string>();
            foreach (var entry in dir)
            {
                var match = path_re.Match (entry.Name, root_dir.Length);
                if (match.Success)
                {
                    string name = match.Groups[1].Value;
                    if (subdirs.Add (name))
                    {
                        PathDelimiter = match.Groups[2].Value;
                        yield return new SubDirEntry (root_dir+name);
                    }
                }
                else
                {
                    yield return entry;
                }
            }
        }

        public override IEnumerable<Entry> GetFilesRecursive ()
        {
            if (0 == m_cwd.Length)
                return m_arc.Dir;
            else
                return from file in m_arc.Dir
                       where file.Name.StartsWith (m_cwd + PathDelimiter)
                       select file;
        }

        private void ChDir (string path)
        {
            if (string.IsNullOrEmpty (path))
                return;
            List<string> cur_dir;
            if (-1 != Array.IndexOf (m_path_delimiters, path[0]))
            {
                path = path.TrimStart (m_path_delimiters);
                cur_dir = new List<string>();
            }
            else
            {
                cur_dir = m_cwd.Split (m_path_delimiters).ToList();
            }
            var path_list = path.Split (m_path_delimiters);
            foreach (var dir in path_list)
            {
                if ("." == dir)
                {
                    continue;
                }
                else if (".." == dir)
                {
                    if (0 == cur_dir.Count)
                        continue;
                    cur_dir.RemoveAt (cur_dir.Count-1);
                }
                else
                {
                    cur_dir.Add (dir);
                }
            }
            string new_path = string.Join (PathDelimiter, cur_dir);
            if (0 != new_path.Length)
            {
                var entry = m_arc.Dir.FirstOrDefault (e => e.Name.StartsWith (new_path + PathDelimiter));
                if (null == entry)
                    throw new DirectoryNotFoundException();
            }
            m_cwd = new_path;
        }
    }
}
