//! \file       Shell.cs
//! \date       Tue Aug 02 13:48:55 2011
//! \brief      Win32 shell functions.
//
// Copyright (C) 2011 by poddav
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
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace GARbro.Shell
{
    static class File
    {
        [DllImport("kernel32.dll", EntryPoint="MoveFileExW", SetLastError=true, CharSet=CharSet.Unicode)]
        public static extern bool MoveFileEx (string lpExistingFileName, string lpNewFileName, MoveFileFlags dwFlags);

        [Flags]
        public enum MoveFileFlags : uint
        {
            ReplaceExisting         = 0x00000001,
            CopyAllowed             = 0x00000002,
            DelayUntilReboot        = 0x00000004,
            WriteThrough            = 0x00000008,
            CreateHardlink          = 0x00000010,
            FailIfNotTrackable      = 0x00000020
        }

        /// <summary>
        /// Wrapper around MoveFileEx WINAPI call.
        /// </summary>
        public static bool Rename (string szFrom, string szTo)
        {
            return MoveFileEx (szFrom, szTo, MoveFileFlags.ReplaceExisting);
        }

        public static int GetLastError ()
        {
            return Marshal.GetLastWin32Error();
        }

        /// <summary>
        /// Possible flags for the SHFileOperation method.
        /// </summary>
        [Flags]
        public enum FileOperationFlags : ushort
        {
            /// <summary>
            /// Do not show a dialog during the process
            /// </summary>
            FOF_SILENT = 0x0004,
            /// <summary>
            /// Do not ask the user to confirm selection
            /// </summary>
            FOF_NOCONFIRMATION = 0x0010,
            /// <summary>
            /// Delete the file to the recycle bin.  (Required flag to send a file to the bin
            /// </summary>
            FOF_ALLOWUNDO = 0x0040,
            /// <summary>
            /// Do not show the names of the files or folders that are being recycled.
            /// </summary>
            FOF_SIMPLEPROGRESS = 0x0100,
            /// <summary>
            /// Surpress errors, if any occur during the process.
            /// </summary>
            FOF_NOERRORUI = 0x0400,
            /// <summary>
            /// Warn if files are too big to fit in the recycle bin and will need
            /// to be deleted completely.
            /// </summary>
            FOF_WANTNUKEWARNING = 0x4000,
        }

        /// <summary>
        /// File Operation Function Type for SHFileOperation
        /// </summary>
        public enum FileOperationType : uint
        {
            /// <summary>
            /// Move the objects
            /// </summary>
            FO_MOVE = 0x0001,
            /// <summary>
            /// Copy the objects
            /// </summary>
            FO_COPY = 0x0002,
            /// <summary>
            /// Delete (or recycle) the objects
            /// </summary>
            FO_DELETE = 0x0003,
            /// <summary>
            /// Rename the object(s)
            /// </summary>
            FO_RENAME = 0x0004,
        }

        /// <summary>
        /// SHFILEOPSTRUCT for SHFileOperation from COM
        /// </summary>
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto, Pack = 1)]
        private struct SHFILEOPSTRUCT
        {

            public IntPtr hwnd;
            [MarshalAs(UnmanagedType.U4)]
            public FileOperationType wFunc;
            public string pFrom;
            public string pTo;
            public FileOperationFlags fFlags;
            [MarshalAs(UnmanagedType.Bool)]
            public bool fAnyOperationsAborted;
            public IntPtr hNameMappings;
            public string lpszProgressTitle;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern int SHFileOperation (ref SHFILEOPSTRUCT FileOp);

        /// <summary>
        /// Send file to recycle bin
        /// </summary>
        /// <param name="path">Location of directory or file to recycle</param>
        /// <param name="flags">FileOperationFlags to add in addition to FOF_ALLOWUNDO</param>
        public static bool Delete (string path, FileOperationFlags flags)
        {
            var fs = new SHFILEOPSTRUCT
            {
                wFunc = FileOperationType.FO_DELETE,
                pFrom = path + '\0' + '\0',
                fFlags = FileOperationFlags.FOF_ALLOWUNDO | flags
            };
            return 0 == SHFileOperation (ref fs);
        }

        public static bool Delete (IEnumerable<string> file_list, FileOperationFlags flags)
        {
            var files = new StringBuilder();
            foreach (var file in file_list)
            {
                files.Append (file);
                files.Append ('\0');
            }
            if (0 == files.Length)
                return false;
            files.Append ('\0');
            var fs = new SHFILEOPSTRUCT
            {
                wFunc = FileOperationType.FO_DELETE,
                pFrom = files.ToString(),
                fFlags = FileOperationFlags.FOF_ALLOWUNDO | flags
            };
            return 0 == SHFileOperation (ref fs);
        }

        public static bool Delete (IEnumerable<string> file_list)
        {
            return Delete (file_list, FileOperationFlags.FOF_WANTNUKEWARNING);
        }

        /// <summary>
        /// Send file to recycle bin.  Display dialog, display warning if files are too big to fit (FOF_WANTNUKEWARNING)
        /// </summary>
        /// <param name="path">Location of directory or file to recycle</param>
        public static bool Delete (string path)
        {
            return Delete (path, FileOperationFlags.FOF_NOCONFIRMATION | FileOperationFlags.FOF_WANTNUKEWARNING);
        }

        /// <summary>
        /// Send file silently to recycle bin.  Surpress dialog, surpress errors, delete if too large.
        /// </summary>
        /// <param name="path">Location of directory or file to recycle</param>
        public static bool MoveToRecycleBin (string path)
        {
            return Delete (path, FileOperationFlags.FOF_NOCONFIRMATION | FileOperationFlags.FOF_NOERRORUI | FileOperationFlags.FOF_SILENT);

        }

        [DllImport("shlwapi.dll", EntryPoint="PathCompactPathExW", CharSet = CharSet.Unicode)]
        static extern bool PathCompactPathEx ([Out] StringBuilder pszOut, string szPath, int cchMax, int dwFlags);

        const int MAX_PATH = 0x104;

        /// <summary>
        /// Truncates a path to fit within a certain number of characters by replacing path components with ellipses.
        /// </summary>
        public static string CompactPath (string name, int length)
        {
            var sb = new StringBuilder (MAX_PATH);
            PathCompactPathEx (sb, name, Math.Min (length+1, MAX_PATH), 0);
            return sb.ToString();
        }
    }

    public class TemporaryFile : IDisposable
    {
        private string m_name;
        public string Name { get { return m_name; } }

        public TemporaryFile ()
        {
            m_name = Path.GetRandomFileName();
        }

        public TemporaryFile (string filename)
        {
            m_name = filename;
        }

        public TemporaryFile (string path, string filename)
        {
            m_name = Path.Combine (path, filename);
        }

        #region IDisposable Members
        bool disposed = false;

        public void Dispose ()
        {
            Dispose (true);
            GC.SuppressFinalize (this);
        }

        protected virtual void Dispose (bool disposing)
        {
            if (!disposed)
            {
                if (disposing)
                {
                    System.IO.File.Delete (m_name);
                }
                disposed = true;
            }
        }
        #endregion
    };

    /// <summary>
    /// Wrapper around SHGetFileInfo WINAPI call.
    /// </summary>
    class FileInfo
    {
        [DllImport("shell32.dll", CharSet=CharSet.Auto)]
        public static extern IntPtr SHGetFileInfo(
                string pszPath, Int32 dwFileAttributes,
                ref SHFILEINFO psfi, int cbFileInfo, int uFlags);

        [DllImport("User32.dll")]
        public static extern int DestroyIcon(IntPtr hIcon);

        [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Auto)]
        public struct SHFILEINFO
        {
             public IntPtr hIcon;
             public int iIcon;
             public uint dwAttributes;

             [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
             public string szDisplayName;

             [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
             public string szTypeName;

             public SHFILEINFO(bool b)
             {
                 hIcon = IntPtr.Zero;
                 iIcon = 0;
                 dwAttributes = 0;
                 szDisplayName = "";
                 szTypeName = "";
             }
        };

        [Flags]
        public enum SHGFI : uint
        {
            /// <summary>get icon</summary>
            Icon            = 0x000000100,  
            /// <summary>get display name</summary>
            DisplayName     = 0x000000200,
            /// <summary>get type name</summary>
            TypeName        = 0x000000400,
            /// <summary>get attributes</summary>
            Attributes      = 0x000000800,
            /// <summary>get icon location</summary>
            IconLocation    = 0x000001000,  
            /// <summary>return exe type</summary>
            ExeType         = 0x000002000,  
            /// <summary>get system icon index</summary>
            SysIconIndex    = 0x000004000,  
            /// <summary>put a link overlay on icon</summary>
            LinkOverlay     = 0x000008000,  
            /// <summary>show icon in selected state</summary>
            Selected        = 0x000010000,  
            /// <summary>get only specified attributes</summary>
            Attr_Specified  = 0x000020000,  
            /// <summary>get large icon</summary>
            LargeIcon       = 0x000000000,  
            /// <summary>get small icon</summary>
            SmallIcon       = 0x000000001,  
            /// <summary>get open icon</summary>
            OpenIcon        = 0x000000002,  
            /// <summary>get shell size icon</summary>
            ShellIconSize   = 0x000000004,  
            /// <summary>pszPath is a pidl</summary>
            PIDL            = 0x000000008,  
            /// <summary>use passed dwFileAttribute</summary>
            UseFileAttributes= 0x000000010,  
            /// <summary>apply the appropriate overlays</summary>
            AddOverlays     = 0x000000020,  
            /// <summary>Get the index of the overlay in the upper 8 bits of the iIcon</summary>
            OverlayIndex    = 0x000000040,  
        }

        public static string GetTypeName (string filename)
        {
            SHFILEINFO info = new SHFILEINFO(true);
            int szInfo = Marshal.SizeOf (info);
            int result = (int)SHGetFileInfo (filename, 0, ref info, szInfo, (int)SHGFI.TypeName);

            // If uFlags does not contain SHGFI_EXETYPE or SHGFI_SYSICONINDEX,
            // the return value is nonzero if successful, or zero otherwise.
            if (result != 0)
                return info.szTypeName;
            else
                return string.Empty;
        }

        public static SHFILEINFO? GetInfo (string filename, SHGFI flags)
        {
            SHFILEINFO info = new SHFILEINFO(true);
            int szInfo = Marshal.SizeOf (info);
            int result = (int)SHGetFileInfo (filename, 0, ref info, szInfo, (int)flags);

            return result != 0? new Nullable<SHFILEINFO> (info): null;
        }
    }
}
