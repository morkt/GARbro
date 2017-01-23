//! \file       ExeFile.cs
//! \date       Mon Jan 23 05:12:50 2017
//! \brief      Win32 EXE file parser/accessor.
//
// Copyright (C) 2017 by morkt
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
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using GameRes.Utility;

namespace GameRes.Formats
{
    public class ExeFile
    {
        ArcView                     m_file;
        Dictionary<string, Section> m_section_table;
        Section                     m_overlay;

        public ExeFile (ArcView file)
        {
            if (!file.View.AsciiEqual (0, "MZ"))
                throw new InvalidFormatException ("File is not a valid win32 executable.");
            m_file = file;
            Whole = new Section { Offset = 0, Size = (uint)Math.Min (m_file.MaxOffset, uint.MaxValue) };
        }

        public ArcView.Frame View { get { return m_file.View; } }

        /// <summary>
        /// Section representing the whole file.
        /// </summary>
        public Section Whole { get; private set; }

        /// <summary>
        /// Dictionary of executable file sections.
        /// </summary>
        public IReadOnlyDictionary<string, Section> Sections
        {
            get
            {
                if (null == m_section_table)
                    InitSectionTable();
                return m_section_table;
            }
        }

        /// <summary>
        /// Overlay section of executable file.
        /// </summary>
        public Section Overlay
        {
            get
            {
                if (null == m_section_table)
                    InitSectionTable();
                return m_overlay;
            }
        }

        /// <summary>
        /// Structure representing section of executable file in the form of its offset and size.
        /// </summary>
        public struct Section
        {
            public long Offset;
            public uint Size;
        }

        /// <summary>
        /// Returns true if executable file contains section <paramref name="name"/>.
        /// </summary>
        public bool ContainsSection (string name)
        {
            return Sections.ContainsKey (name);
        }

        /// <summary>
        /// Search for byte sequence within specified section.
        /// </summary>
        /// <returns>Offset of byte sequence, if found, -1 otherwise.</returns>
        public long FindString (Section section, byte[] seq, int step = 1)
        {
            if (step <= 0)
                throw new ArgumentOutOfRangeException ("step", "Search step should be positive integer.");
            long offset = section.Offset;
            if (offset < 0 || offset > m_file.MaxOffset)
                throw new ArgumentOutOfRangeException ("section", "Invalid executable file section specified.");
            uint seq_length = (uint)seq.Length;
            if (0 == seq_length || section.Size < seq_length)
                return -1;
            long end_offset = Math.Min (m_file.MaxOffset, offset + section.Size);
            unsafe
            {
                while (offset < end_offset)
                {
                    uint page_size = (uint)Math.Min (0x10000L, end_offset - offset);
                    if (page_size < seq_length)
                        break;
                    using (var view = m_file.CreateViewAccessor (offset, page_size))
                    using (var ptr = new ViewPointer (view, offset))
                    {
                        byte* page_begin = ptr.Value;
                        byte* page_end   = page_begin + page_size - seq_length;
                        byte* p;
                        for (p = page_begin; p <= page_end; p += step)
                        {
                            int i = 0;
                            while (p[i] == seq[i])
                            {
                                if (++i == seq.Length)
                                    return offset + (p - page_begin);
                            }
                        }
                        offset += p - page_begin;
                    }
                }
            }
            return -1;
        }

        public long FindAsciiString (Section section, string seq, int step = 1)
        {
            return FindString (section, Encoding.ASCII.GetBytes (seq), step);
        }

        public long FindSignature (Section section, uint signature, int step = 4)
        {
            var bytes = new byte[4];
            LittleEndian.Pack (signature, bytes, 0);
            return FindString (section, bytes, step);
        }

        private void InitSectionTable ()
        {
            long pe_offset = m_file.View.ReadUInt32 (0x3C);
            if (pe_offset >= m_file.MaxOffset-0x58 || !m_file.View.AsciiEqual (pe_offset, "PE\0\0"))
                throw new InvalidFormatException ("File is not a valid win32 executable.");

            int opt_header = m_file.View.ReadUInt16 (pe_offset+0x14); // SizeOfOptionalHeader
            long offset = m_file.View.ReadUInt32 (pe_offset+0x54); // SizeOfHeaders
            long section_table = pe_offset+opt_header+0x18;
            int count = m_file.View.ReadUInt16 (pe_offset+6); // NumberOfSections
            var table = new Dictionary<string, Section> (count);
            if (section_table + 0x28*count < m_file.MaxOffset)
            {
                for (int i = 0; i < count; ++i)
                {
                    var name = m_file.View.ReadString (section_table, 0x10);
                    var section = new Section {
                        Size  = m_file.View.ReadUInt32 (section_table+0x10), 
                        Offset = m_file.View.ReadUInt32 (section_table+0x14)
                    };
                    if (!table.ContainsKey (name))
                        table.Add (name, section);
                    if (0 != section.Size)
                        offset = Math.Max (section.Offset + section.Size, offset);
                    section_table += 0x28;
                }
            }
            offset = Math.Min ((offset + 0xF) & ~0xFL, m_file.MaxOffset);
            m_overlay.Offset = offset;
            m_overlay.Size = (uint)(m_file.MaxOffset - offset);
            m_section_table = table;
        }

        /// <summary>
        /// Helper class for executable file resources access.
        /// </summary>
        public sealed class ResourceAccessor : IDisposable
        {
            IntPtr      m_exe;

            public ResourceAccessor (string filename)
            {
                const uint LOAD_LIBRARY_AS_IMAGE_RESOURCE = 0x20;

                m_exe = NativeMethods.LoadLibraryEx (filename, IntPtr.Zero, LOAD_LIBRARY_AS_IMAGE_RESOURCE);
                if (IntPtr.Zero == m_exe)
                    throw new Win32Exception (Marshal.GetLastWin32Error());
            }

            public byte[] GetResource (string name, string type)
            {
                if (m_disposed)
                    throw new ObjectDisposedException ("Access to disposed ResourceAccessor object failed.");
                var res = NativeMethods.FindResource (m_exe, name, type);
                if (IntPtr.Zero == res)
                    return null;
                var glob = NativeMethods.LoadResource (m_exe, res);
                if (IntPtr.Zero == glob)
                    return null;
                uint size = NativeMethods.SizeofResource (m_exe, res);
                var src = NativeMethods.LockResource (glob);
                if (IntPtr.Zero == src)
                    return null;

                var dst = new byte[size];
                Marshal.Copy (src, dst, 0, dst.Length);
                return dst;
            }

            #region IDisposable implementation
            bool m_disposed = false;
            public void Dispose ()
            {
                Dispose (true);
                GC.SuppressFinalize (this);
            }

            ~ResourceAccessor ()
            {
                Dispose (false);
            }

            void Dispose (bool disposing)
            {
                if (!m_disposed)
                {
                    NativeMethods.FreeLibrary (m_exe);
                    m_disposed = true;
                }
            }
            #endregion
        }
    }

    static internal class NativeMethods
    {
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static internal extern IntPtr LoadLibraryEx (string lpFileName, IntPtr hReservedNull, uint dwFlags);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static internal extern bool FreeLibrary (IntPtr hModule);

        [DllImport( "kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static internal extern IntPtr FindResource (IntPtr hModule, string lpName, string lpType);

        [DllImport("Kernel32.dll", SetLastError = true)]
        static internal extern IntPtr LoadResource (IntPtr hModule, IntPtr hResource);

        [DllImport("Kernel32.dll", SetLastError = true)]
        static internal extern uint SizeofResource (IntPtr hModule, IntPtr hResource);

        [DllImport("kernel32.dll")]
        static internal extern IntPtr LockResource (IntPtr hResData);
    }
}
