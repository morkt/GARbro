//! \file       FileSystem.cs
//! \date       Fri Jun 05 15:32:27 2015
//! \brief      Gameres file system abstraction.
//
// Copyright (C) 2015-2016 by morkt
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
using System.Runtime.ExceptionServices;
using System.Text.RegularExpressions;
using GameRes.Strings;

namespace GameRes
{
    public interface IFileSystem : IDisposable
    {
        /// <summary>
        /// Returns entry corresponding to the given file or directory within filesystem.
        /// </summary>
        /// <exception cref="FileNotFoundException">File is not found.</exception>
        Entry FindFile (string filename);

        /// <summary>
        /// System.IO.File.Exists() analog.
        /// </summary>
        bool FileExists (string filename);

        /// <summary>
        /// Open file for reading as stream.
        /// </summary>
        Stream OpenStream (Entry entry);

        /// <summary>
        /// Open file for reading as seekable binary stream.
        /// </summary>
        IBinaryStream OpenBinaryStream (Entry entry);

        /// <summary>
        /// Open file for reading as memory-mapped view.
        /// </summary>
        ArcView OpenView (Entry entry);

        /// <summary>
        /// Enumerates subdirectories and files in current directory.
        /// </summary>
        IEnumerable<Entry> GetFiles ();

        /// <summary>
        /// Returns enumeration of files within current directory that match specified pattern.
        /// </summary>
        IEnumerable<Entry> GetFiles (string pattern);

        /// <summary>
        /// System.IO.Path.Combine() analog.
        /// </summary>
        string CombinePath (string path1, string path2);

        /// <summary>
        /// System.IO.Path.GetDirectoryName() analog.
        /// </summary>
        string GetDirectoryName (string path);

        /// <summary>
        /// Recursively enumerates files in the current directory and its subdirectories.
        /// Subdirectory entries are omitted from resulting set.
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

    public sealed class PhysicalFileSystem : IFileSystem
    {
        public string CurrentDirectory
        {
            get { return Directory.GetCurrentDirectory(); }
            set { Directory.SetCurrentDirectory (value); }
        }

        public string CombinePath (string path1, string path2)
        {
            return Path.Combine (path1, path2);
        }

        public string GetDirectoryName (string path)
        {
            return Path.GetDirectoryName (path);
        }

        public Entry FindFile (string filename)
        {
            var attr = File.GetAttributes (filename);
            if ((attr & FileAttributes.Directory) == FileAttributes.Directory)
                return new SubDirEntry (filename);
            else
                return EntryFromFileInfo (new FileInfo (filename));
        }

        public bool FileExists (string filename)
        {
            return File.Exists (filename);
        }

        public IEnumerable<Entry> GetFiles ()
        {
            var info = new DirectoryInfo (CurrentDirectory);
            foreach (var subdir in info.EnumerateDirectories())
            {
                if (0 != (subdir.Attributes & FileAttributes.System))
                    continue;
                yield return new SubDirEntry (subdir.FullName);
            }
            foreach (var file in info.EnumerateFiles())
            {
                if (0 != (file.Attributes & FileAttributes.System))
                    continue;
                yield return EntryFromFileInfo (file);
            }
        }

        public IEnumerable<Entry> GetFiles (string pattern)
        {
            string path = GetDirectoryName (pattern);
            pattern = Path.GetFileName (pattern);
            path = CombinePath (CurrentDirectory, path);
            var info = new DirectoryInfo (path);
            foreach (var file in info.EnumerateFiles (pattern))
            {
                if (0 != (file.Attributes & FileAttributes.System))
                    continue;
                yield return EntryFromFileInfo (file);
            }
        }

        public IEnumerable<Entry> GetFilesRecursive ()
        {
            var info = new DirectoryInfo (CurrentDirectory);
            foreach (var file in info.EnumerateFiles ("*", SearchOption.AllDirectories))
            {
                if (0 != (file.Attributes & FileAttributes.System))
                    continue;
                yield return EntryFromFileInfo (file);
            }
        }

        private Entry EntryFromFileInfo (FileInfo file)
        {
            var entry = FormatCatalog.Instance.Create<Entry> (file.FullName);
            entry.Size = (uint)Math.Min (file.Length, uint.MaxValue);
            return entry;
        }

        public Stream OpenStream (Entry entry)
        {
            return File.OpenRead (entry.Name);
        }

        public IBinaryStream OpenBinaryStream (Entry entry)
        {
            var input = OpenStream (entry);
            return new BinaryStream (input, entry.Name);
        }

        public ArcView OpenView (Entry entry)
        {
            return new ArcView (entry.Name);
        }

        public void Dispose ()
        {
            GC.SuppressFinalize (this);
        }

        /// <summary>
        /// Create file named <paramref name="filename"/> in current directory and open it
        /// for writing. Overwrites existing file, if any.
        /// </summary>
        static public Stream CreateFile (string filename)
        {
            filename = CreatePath (filename);
            return File.Create (filename);
        }

        /// <summary>
        /// Create all directories that lead to <paramref name="filename"/>, if any.
        /// </summary>
        static public string CreatePath (string filename)
        {
            string dir = Path.GetDirectoryName (filename);
            if (!string.IsNullOrEmpty (dir)) // check for malformed filenames
            {
                string root = Path.GetPathRoot (dir);
                if (!string.IsNullOrEmpty (root))
                {
                    dir = dir.Substring (root.Length); // strip root
                }
                string cwd = Directory.GetCurrentDirectory() + Path.DirectorySeparatorChar;
                dir = Path.GetFullPath (dir);
                filename = Path.GetFileName (filename);
                // check whether filename would reside within current directory
                if (dir.StartsWith (cwd, StringComparison.OrdinalIgnoreCase))
                {
                    // path looks legit, create it
                    Directory.CreateDirectory (dir);
                    filename = Path.Combine (dir, filename);
                }
            }
            return filename;
        }
    }

    public abstract class ArchiveFileSystem : IFileSystem
    {
        protected readonly ArcFile                      m_arc;
        protected readonly Dictionary<string, Entry>    m_dir;

        public ArcFile Source { get { return m_arc; } }

        public abstract string CurrentDirectory { get; set; }

        public ArchiveFileSystem (ArcFile arc)
        {
            m_arc = arc;
            m_dir = new Dictionary<string, Entry> (arc.Dir.Count, StringComparer.OrdinalIgnoreCase);
            foreach (var entry in arc.Dir)
            {
                if (!m_dir.ContainsKey (entry.Name))
                    m_dir.Add (entry.Name, entry);
            }
        }

        public bool FileExists (string filename)
        {
            return m_dir.ContainsKey (filename)
                || !string.IsNullOrEmpty (CurrentDirectory)
                   && m_dir.ContainsKey (CombinePath (CurrentDirectory, filename));
        }

        public Stream OpenStream (Entry entry)
        {
            return m_arc.OpenEntry (entry);
        }

        public IBinaryStream OpenBinaryStream (Entry entry)
        {
            return m_arc.OpenBinaryEntry (entry);
        }

        public ArcView OpenView (Entry entry)
        {
            return m_arc.OpenView (entry);
        }

        public abstract Entry FindFile (string filename);

        public abstract IEnumerable<Entry> GetFiles ();

        public abstract IEnumerable<Entry> GetFilesRecursive ();

        public abstract string CombinePath (string path1, string path2);

        public abstract string GetDirectoryName (string path);

        public abstract IEnumerable<Entry> GetFiles (string pattern);

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

    public class FlatArchiveFileSystem : ArchiveFileSystem
    {
        public override string CurrentDirectory
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

        public FlatArchiveFileSystem (ArcFile arc) : base (arc)
        {
        }

        public override Entry FindFile (string filename)
        {
            Entry entry = null;
            if (!m_dir.TryGetValue (filename, out entry))
                throw new FileNotFoundException ("Unable to find the specified file.", filename);
            return entry;
        }

        public override IEnumerable<Entry> GetFiles ()
        {
            return m_arc.Dir;
        }

        public override IEnumerable<Entry> GetFilesRecursive ()
        {
            return m_arc.Dir;
        }

        public override IEnumerable<Entry> GetFiles (string pattern)
        {
            var glob = new FileNameGlob (pattern);
            return m_arc.Dir.Where (f => glob.IsMatch (f.Name));
        }

        public override string CombinePath (string path1, string path2)
        {
            return Path.Combine (path1, path2);
        }

        public override string GetDirectoryName (string path)
        {
            return "";
        }
    }

    public class TreeArchiveFileSystem : ArchiveFileSystem
    {
        private string  m_cwd;

        private string PathDelimiter { get; set; }

        private static readonly char[] m_path_delimiters = { '/', '\\' };

        public TreeArchiveFileSystem (ArcFile arc) : base (arc)
        {
            m_cwd = "";
            PathDelimiter = "/";
        }

        public override string CurrentDirectory
        {
            get { return m_cwd; }
            set { ChDir (value); }
        }

        public override string CombinePath (string path1, string path2)
        {
            if (0 == path1.Length)
                return path2;
            if (0 == path2.Length)
                return path1;
            if (path1.EndsWith (PathDelimiter))
                return path1+path2;
            return string.Join (PathDelimiter, path1, path2);
        }

        public override Entry FindFile (string filename)
        {
            Entry entry = null;
            if (m_dir.TryGetValue (filename, out entry))
                return entry;
            if (m_dir.TryGetValue (CombinePath (CurrentDirectory, filename), out entry))
                return entry;
            var dir_name = filename + PathDelimiter;
            if (m_dir.Keys.Any (n => n.StartsWith (dir_name)))
                return new SubDirEntry (filename);
            throw new FileNotFoundException ("Unable to find the specified file.", filename);
        }

        static readonly Regex path_re = new Regex (@"\G[/\\]?([^/\\]+)([/\\])");

        public override IEnumerable<Entry> GetFiles ()
        {
            IEnumerable<Entry> dir = GetFilesRecursive();
            var root_dir = m_cwd;
            if (!string.IsNullOrEmpty (root_dir))
                root_dir += PathDelimiter;

            var subdirs = new HashSet<string> (StringComparer.OrdinalIgnoreCase);
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
            var path = m_cwd + PathDelimiter;
            return from file in m_arc.Dir
                   where file.Name.StartsWith (path, StringComparison.OrdinalIgnoreCase)
                   select file;
        }

        public override IEnumerable<Entry> GetFiles (string pattern)
        {
            string path = GetDirectoryName (pattern);
            if (string.IsNullOrEmpty (path))
                path = CurrentDirectory;
            pattern = Path.GetFileName (pattern);
            var glob = new FileNameGlob (pattern);
            if (string.IsNullOrEmpty (path))
            {
                return m_arc.Dir.Where (f => glob.IsMatch (Path.GetFileName (f.Name)));
            }
            else
            {
                path += PathDelimiter;
                return m_arc.Dir.Where (f => f.Name.StartsWith (path, StringComparison.OrdinalIgnoreCase)
                                             && glob.IsMatch (Path.GetFileName (f.Name)));
            }
        }

        public IEnumerable<Entry> GetFilesRecursive (IEnumerable<Entry> list)
        {
            var result = new List<Entry>();
            foreach (var entry in list)
            {
                if (!(entry is SubDirEntry)) // add ordinary file
                    result.Add (entry);
                else if (".." == entry.Name) // skip reference to parent directory
                    continue;
                else // add all files contained within directory, recursive
                {
                    var dir_name = entry.Name+PathDelimiter;
                    result.AddRange (from file in m_arc.Dir
                                     where file.Name.StartsWith (dir_name, StringComparison.OrdinalIgnoreCase)
                                     select file);
                }
            }
            return result;
        }

        private void ChDir (string path)
        {
            if (string.IsNullOrEmpty (path))
            {
                m_cwd = "";
                return;
            }
            var cur_dir = new List<string>();
            if (-1 != Array.IndexOf (m_path_delimiters, path[0]))
            {
                path = path.TrimStart (m_path_delimiters);
            }
            else if (".." == path && !string.IsNullOrEmpty (m_cwd))
            {
                cur_dir.AddRange (m_cwd.Split (m_path_delimiters));
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
                var dir_name = new_path + PathDelimiter;
                var entry = m_arc.Dir.FirstOrDefault (e => e.Name.StartsWith (dir_name, StringComparison.OrdinalIgnoreCase));
                if (null == entry)
                    throw new DirectoryNotFoundException();
            }
            m_cwd = new_path;
        }

        public override string GetDirectoryName (string path)
        {
            int sep = path.LastIndexOfAny (m_path_delimiters);
            if (-1 == sep)
                return "";
            return path.Substring (0, sep);
        }
    }

    public sealed class FileSystemStack : IDisposable
    {
        Stack<IFileSystem> m_fs_stack = new Stack<IFileSystem>();
        Stack<string> m_arc_name_stack = new Stack<string>();

        public IEnumerable<IFileSystem> All { get { return m_fs_stack; } }

        public IFileSystem Top { get { return m_fs_stack.Peek(); } }
        public int       Count { get { return m_fs_stack.Count; } }
        public IEnumerable<string> ArcStack { get { return m_arc_name_stack; } }

        public ArcFile      CurrentArchive { get; private set; }
        private IFileSystem LastVisitedArc { get; set; }
        private string     LastVisitedPath { get; set; }

        public FileSystemStack ()
        {
            m_fs_stack.Push (new PhysicalFileSystem());
        }

        public void ChDir (Entry entry)
        {
            if (entry is SubDirEntry)
            {
                if (Count > 1 && ".." == entry.Name && string.IsNullOrEmpty (Top.CurrentDirectory))
                {
                    Pop();
                    if (!string.IsNullOrEmpty (LastVisitedPath))
                    {
                        Top.CurrentDirectory = Top.GetDirectoryName (LastVisitedPath);
                    }
                }
                else
                {
                    Top.CurrentDirectory = entry.Name;
                }
                return;
            }
            if (entry.Name == LastVisitedPath && null != LastVisitedArc)
            {
                Push (LastVisitedPath, LastVisitedArc);
                var fs = LastVisitedArc as ArchiveFileSystem;
                if (null != fs)
                    CurrentArchive = fs.Source;
                return;
            }
            Flush();
            var arc = ArcFile.TryOpen (entry);
            if (null == arc)
            {
                if (FormatCatalog.Instance.LastError is OperationCanceledException)
                    ExceptionDispatchInfo.Capture (FormatCatalog.Instance.LastError).Throw();
                else
                    throw new UnknownFormatException (FormatCatalog.Instance.LastError);
            }
            try
            {
                Push (entry.Name, arc.CreateFileSystem());
                CurrentArchive = arc;
            }
            catch
            {
                arc.Dispose();
                throw;
            }
        }

        private void Push (string path, IFileSystem fs)
        {
            m_fs_stack.Push (fs);
            m_arc_name_stack.Push (path);
        }

        internal void Pop ()
        {
            if (m_fs_stack.Count > 1)
            {
                Flush();
                LastVisitedArc = m_fs_stack.Pop();
                LastVisitedPath = m_arc_name_stack.Pop();
                if (m_fs_stack.Count > 1 && m_fs_stack.Peek() is ArchiveFileSystem)
                    CurrentArchive = (m_fs_stack.Peek() as ArchiveFileSystem).Source;
                else
                    CurrentArchive = null;
            }
        }

        public void Flush ()
        {
            if (LastVisitedArc != null && (0 == Count || LastVisitedArc != Top))
            {
                LastVisitedArc.Dispose();
                LastVisitedArc = null;
                LastVisitedPath = null;
            }
        }

        private bool _disposed = false;
        public void Dispose ()
        {
            if (!_disposed)
            {
                Flush();
                foreach (var fs in m_fs_stack.Reverse())
                    fs.Dispose();
                _disposed = true;
            }
            GC.SuppressFinalize (this);
        }
    }

    public static class VFS
    {
        private static FileSystemStack m_vfs = new FileSystemStack();

        /// <summary>
        /// Top, or "current" filesystem in VFS hierarchy.
        /// </summary>
        public static IFileSystem Top { get { return m_vfs.Top; } }

        /// <summary>
        /// Whether top filesystem is virtual (i.e. represents an archive).
        /// </summary>
        public static bool  IsVirtual { get { return m_vfs.Count > 1; } }

        /// <summary>
        /// Number of filesystems in hierarchy. ==1 when only physical file system is represented.
        /// Always >= 1
        /// </summary>
        public static  int      Count { get { return m_vfs.Count; } }

        /// <summary>
        /// Archive corresponding to the top filesystem, or null if file system doesn't have underlying
        /// archive file.
        /// </summary>
        public static ArcFile CurrentArchive { get { return m_vfs.CurrentArchive; } }

        private static string[] m_top_path = new string[1];

        public static IEnumerable<string> FullPath
        {
            get
            {
                m_top_path[0] = Top.CurrentDirectory;
                if (1 == Count)
                    return m_top_path;
                else
                    return m_vfs.ArcStack.Reverse().Concat (m_top_path);
            }
            set
            {
                if (!value.Any())
                    return;
                var desired = value.ToArray();
                int desired_vfs_count = desired.Length;
                int i = 0;
                using (var arc_iterator = m_vfs.ArcStack.Reverse().GetEnumerator())
                {
                    while (i < desired_vfs_count - 1 && arc_iterator.MoveNext())
                    {
                        if (arc_iterator.Current != desired[i])
                            break;
                        ++i;
                    }
                }
                while (Count > i+1)
                    m_vfs.Pop();
                while (Count < desired_vfs_count)
                {
                    var entry = m_vfs.Top.FindFile (desired[Count-1]);
                    if (entry is SubDirEntry)
                        throw new FileNotFoundException ("Unable to find the specified file.", desired[Count-1]);
                    m_vfs.ChDir (entry);
                }
                m_vfs.Top.CurrentDirectory = desired.Last();
            }
        }

        public static string CombinePath (string path1, string path2)
        {
            return m_vfs.Top.CombinePath (path1, path2);
        }

        public static string GetDirectoryName (string path)
        {
            return m_vfs.Top.GetDirectoryName (path);
        }

        public static Entry FindFile (string filename)
        {
            if (".." == filename)
                return new SubDirEntry ("..");
            return m_vfs.Top.FindFile (filename);
        }

        public static bool FileExists (string filename)
        {
            return m_vfs.Top.FileExists (filename);
        }

        public static Stream OpenStream (Entry entry)
        {
            return m_vfs.Top.OpenStream (entry);
        }

        public static IBinaryStream OpenBinaryStream (Entry entry)
        {
            return m_vfs.Top.OpenBinaryStream (entry);
        }

        public static ArcView OpenView (Entry entry)
        {
            return m_vfs.Top.OpenView (entry);
        }

        public static IImageDecoder OpenImage (Entry entry)
        {
            var fs = m_vfs.Top;
            var arc_fs = fs as ArchiveFileSystem;
            if (arc_fs != null)
                return arc_fs.Source.OpenImage (entry);

            var input = fs.OpenBinaryStream (entry);
            return ImageFormatDecoder.Create (input);
        }

        public static Stream OpenStream (string filename)
        {
            return m_vfs.Top.OpenStream (m_vfs.Top.FindFile (filename));
        }

        public static IBinaryStream OpenBinaryStream (string filename)
        {
            return m_vfs.Top.OpenBinaryStream (m_vfs.Top.FindFile (filename));
        }

        public static ArcView OpenView (string filename)
        {
            return m_vfs.Top.OpenView (m_vfs.Top.FindFile (filename));
        }

        public static void ChDir (Entry entry)
        {
            m_vfs.ChDir (entry);
        }

        public static void ChDir (string path)
        {
            m_vfs.ChDir (FindFile (path));
        }

        public static void Flush ()
        {
            m_vfs.Flush();
        }

        public static IEnumerable<Entry> GetFiles ()
        {
            return m_vfs.Top.GetFiles();
        }

        /// <summary>
        /// Returns enumeration of files within current directory that match specified pattern.
        /// </summary>
        public static IEnumerable<Entry> GetFiles (string pattern)
        {
            return m_vfs.Top.GetFiles (pattern);
        }

        public static readonly ISet<char> InvalidFileNameChars = new HashSet<char> (Path.GetInvalidFileNameChars());

        public static readonly char[] PathSeparatorChars = { '\\', '/', ':' };

        /// <summary>
        /// Returns true if given <paramref name="path"/> points to a specified <paramref name="filename"/>.
        /// </summary>
        public static bool IsPathEqualsToFileName (string path, string filename)
        {
            // first, filter out completely different paths
            if (!path.EndsWith (filename, StringComparison.OrdinalIgnoreCase))
                return false;
            // now, compare length of filename portion of the path
            int filename_index = path.LastIndexOfAny (PathSeparatorChars);
            filename_index++;
            int filename_portion_length = path.Length - filename_index;
            return filename.Length == filename_portion_length;
        }

        /// <summary>
        /// Change filename portion of the <paramref name="path"/> to <paramref name="target"/>.
        /// </summary>
        public static string ChangeFileName (string path, string target)
        {
            var dir_name = GetDirectoryName (path);
            return CombinePath (dir_name, target);
        }
    }

    public class FileNameGlob
    {
        Regex   m_glob;

        public FileNameGlob (string pattern)
        {
            pattern = Regex.Escape (pattern);
            if (pattern.EndsWith (@"\.\*")) // "*" and "*.*" are equivalent
                pattern = pattern.Remove (pattern.Length-4) + @"(?:\..*)?";
            pattern = pattern.Replace (@"\*", ".*").Replace (@"\?", ".");
            m_glob = new Regex ("^"+pattern+"$", RegexOptions.IgnoreCase);
        }

        public bool IsMatch (string str)
        {
            return m_glob.IsMatch (str);
        }
    }

    public class UnknownFormatException : FileFormatException
    {
        public UnknownFormatException () : base (garStrings.MsgUnknownFormat) { }
        public UnknownFormatException (Exception inner) : base (garStrings.MsgUnknownFormat, inner) { }
    }
}
