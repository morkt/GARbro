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
using System.Linq;
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
        uint                        m_image_base = 0;
        List<ImageSection>          m_section_list;
        bool?                       m_is_NE;

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

        public bool IsWin16 => m_is_NE ?? (m_is_NE = IsNe()).Value;

        private bool IsNe ()
        {
            uint ne_offset = View.ReadUInt32 (0x3C);
            return ne_offset < m_file.MaxOffset-2 && View.AsciiEqual (ne_offset, "NE");
        }

        /// <summary>
        /// Dictionary of executable file sections.
        /// </summary>
        ///
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

        public uint ImageBase
        {
            get
            {
                if (0 == m_image_base)
                    InitImageBase();
                return m_image_base;
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

        public class ImageSection // IMAGE_SECTION_HEADER
        {
            public string   Name;
            public uint     VirtualSize;
            public uint     VirtualAddress;
            public uint     SizeOfRawData;
            public uint     PointerToRawData;
            public uint     Characteristics;
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

        public Section SectionByOffset (long offset)
        {
            foreach (var section in Sections.Values)
            {
                if (offset >= section.Offset && offset < section.Offset + section.Size)
                    return section;
            }
            return new Section { Offset = Whole.Size, Size = 0 };
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

        /// <summary>
        /// Convert virtual address into raw file offset.
        /// </summary>
        public long GetAddressOffset (uint address)
        {
            var section = GetAddressSection (address);
            if (null == section)
                return m_file.MaxOffset;
            uint rva = address - ImageBase;
            return section.PointerToRawData + (rva - section.VirtualAddress);
        }

        public string GetCString (uint address)
        {
            return GetCString (address, Encodings.cp932);
        }

        static readonly byte[] ZeroByte = new byte[1] { 0 };

        /// <summary>
        /// Returns null-terminated string from specified virtual address.
        /// </summary>
        public string GetCString (uint address, Encoding enc)
        {
            var section = GetAddressSection (address);
            if (null == section)
                return null;
            uint rva = address - ImageBase;
            uint offset = section.PointerToRawData + (rva - section.VirtualAddress);
            uint size   = section.PointerToRawData + section.SizeOfRawData - offset;
            long eos = FindString (new Section { Offset = offset, Size = size }, ZeroByte);
            if (eos < 0)
                return null;
            return View.ReadString (offset, (uint)(eos - offset), enc);
        }

        private ImageSection GetAddressSection (uint address)
        {
            var img_base = ImageBase;
            if (address < img_base)
                throw new ArgumentException ("Invalid virtual address.");
            if (null == m_section_list)
                InitSectionTable();
            uint rva = address - img_base;
            foreach (var section in m_section_list)
            {
                if (rva >= section.VirtualAddress && rva < section.VirtualAddress + section.SizeOfRawData)
                    return section;
            }
            return null;
        }

        private void InitImageBase ()
        {
            long hdr_offset = GetHeaderOffset() + 0x18;
            if (View.ReadUInt16 (hdr_offset) != 0x010B)
                throw new InvalidFormatException ("File is not a valid Windows 32-bit executable.");
            m_image_base = View.ReadUInt32 (hdr_offset+0x1C); // ImageBase
        }

        private long GetHeaderOffset ()
        {
            long pe_offset = View.ReadUInt32 (0x3C);
            if (pe_offset >= m_file.MaxOffset-0x58 || !View.AsciiEqual (pe_offset, "PE\0\0"))
                throw new InvalidFormatException ("File is not a valid Windows 32-bit executable.");
            return pe_offset;
        }

        private void InitSectionTable ()
        {
            if (IsWin16)
            {
                InitNe();
                return;
            }
            long pe_offset = GetHeaderOffset();
            int opt_header = View.ReadUInt16 (pe_offset+0x14); // SizeOfOptionalHeader
            long section_table = pe_offset+opt_header+0x18;
            long offset = View.ReadUInt32 (pe_offset+0x54); // SizeOfHeaders
            int count = View.ReadUInt16 (pe_offset+6); // NumberOfSections
            var table = new Dictionary<string, Section> (count);
            var list = new List<ImageSection> (count);
            if (section_table + 0x28*count < m_file.MaxOffset)
            {
                for (int i = 0; i < count; ++i)
                {
                    var name = View.ReadString (section_table, 8);
                    var img_section = new ImageSection {
                        Name = name,
                        VirtualSize      = View.ReadUInt32 (section_table+0x08),
                        VirtualAddress   = View.ReadUInt32 (section_table+0x0C),
                        SizeOfRawData    = View.ReadUInt32 (section_table+0x10), 
                        PointerToRawData = View.ReadUInt32 (section_table+0x14),
                        Characteristics  = View.ReadUInt32 (section_table+0x24),
                    };
                    var section = new Section {
                        Offset = img_section.PointerToRawData,
                        Size  = img_section.SizeOfRawData
                    };
                    list.Add (img_section);
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
            m_section_list = list;
        }

        void InitNe ()
        {
            uint ne_offset = m_file.View.ReadUInt32 (0x3C);
            int segment_count = m_file.View.ReadUInt16 (ne_offset + 0x1C);
            uint seg_table = m_file.View.ReadUInt16 (ne_offset + 0x22) + ne_offset;
            int shift = m_file.View.ReadUInt16 (ne_offset + 0x32);
            uint last_seg_end = 0;
            for (int i = 0; i < segment_count; ++i)
            {
                uint offset = (uint)m_file.View.ReadUInt16 (seg_table) << shift;
                uint size   = m_file.View.ReadUInt16 (seg_table+2);
                if (offset + size > last_seg_end)
                    last_seg_end = offset + size;
            }
            m_overlay.Offset = last_seg_end;
            m_overlay.Size = (uint)(m_file.MaxOffset - last_seg_end);
            m_section_table = new Dictionary<string, Section>();    // these are empty for 16-bit executables
            m_section_list = new List<ImageSection>();              //
        }

        /// <summary>
        /// Helper class for executable file resources access.
        /// </summary>
        public sealed class ResourceAccessor : IDisposable
        {
            IntPtr      m_exe;

            public ResourceAccessor (string filename)
            {
                const uint LOAD_LIBRARY_AS_DATAFILE       = 0x02;
                const uint LOAD_LIBRARY_AS_IMAGE_RESOURCE = 0x20;

                m_exe = NativeMethods.LoadLibraryEx (filename, IntPtr.Zero, LOAD_LIBRARY_AS_DATAFILE|LOAD_LIBRARY_AS_IMAGE_RESOURCE);
                if (IntPtr.Zero == m_exe)
                    throw new Win32Exception (Marshal.GetLastWin32Error());
            }

            public byte[] GetResource (string name, string type)
            {
                var res = FindResource (name, type);
                if (IntPtr.Zero == res)
                    return null;
                var src = LockResource (res);
                if (IntPtr.Zero == src)
                    return null;
                uint size = NativeMethods.SizeofResource (m_exe, res);
                var dst = new byte[size];
                Marshal.Copy (src, dst, 0, dst.Length);
                return dst;
            }

            public int ReadResource (string name, string type, byte[] dest, int pos)
            {
                var res = FindResource (name, type);
                if (IntPtr.Zero == res)
                    return 0;
                var src = LockResource (res);
                if (IntPtr.Zero == src)
                    return 0;
                int length = (int)NativeMethods.SizeofResource (m_exe, res);
                length = Math.Min (dest.Length - pos, length);
                Marshal.Copy (src, dest, pos, length);
                return length;
            }

            public uint GetResourceSize (string name, string type)
            {
                var res = FindResource (name, type);
                if (IntPtr.Zero == res)
                    return 0;
                return NativeMethods.SizeofResource (m_exe, res);
            }

            private IntPtr FindResource (string name, string type)
            {
                if (m_disposed)
                    throw new ObjectDisposedException ("Access to disposed ResourceAccessor object failed.");
                return NativeMethods.FindResource (m_exe, name, type);
            }

            private IntPtr LockResource (IntPtr res)
            {
                var glob = NativeMethods.LoadResource (m_exe, res);
                if (IntPtr.Zero == glob)
                    return IntPtr.Zero;
                return NativeMethods.LockResource (glob);
            }

            public IEnumerable<string> EnumTypes ()
            {
                var types = new List<string>();
                if (!NativeMethods.EnumResourceTypes (m_exe, (m, t, p) => AddResourceName (types, t), IntPtr.Zero))
                    return Enumerable.Empty<string>();
                return types;
            }

            public IEnumerable<string> EnumNames (string type)
            {
                var names = new List<string>();
                if (!NativeMethods.EnumResourceNames (m_exe, type, (m, t, n, p) => AddResourceName (names, n), IntPtr.Zero))
                    return Enumerable.Empty<string>();
                return names;
            }

            private static bool AddResourceName (List<string> list, IntPtr name)
            {
                list.Add (ResourceNameToString (name));
                return true; 
            }

            private static string ResourceNameToString (IntPtr resName)
            {
                if ((resName.ToInt64() >> 16) == 0)
                {
                    return "#" + resName.ToString();
                }
                else
                {
                    return Marshal.PtrToStringUni (resName);
                }
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

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static internal extern IntPtr FindResource (IntPtr hModule, string lpName, string lpType);

        [DllImport("Kernel32.dll", SetLastError = true)]
        static internal extern IntPtr LoadResource (IntPtr hModule, IntPtr hResource);

        [DllImport("Kernel32.dll", SetLastError = true)]
        static internal extern uint SizeofResource (IntPtr hModule, IntPtr hResource);

        [DllImport("kernel32.dll")]
        static internal extern IntPtr LockResource (IntPtr hResData);

        internal delegate bool EnumResTypeProc (IntPtr hModule, IntPtr lpszType, IntPtr lParam);
        internal delegate bool EnumResNameProc (IntPtr hModule, IntPtr lpszType, IntPtr lpszName, IntPtr lParam);
        internal delegate bool EnumResLangProc (IntPtr hModule, IntPtr lpszType, IntPtr lpszName, ushort wIDLanguage, IntPtr lParam);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static internal extern bool EnumResourceTypes (IntPtr hModule, [MarshalAs(UnmanagedType.FunctionPtr)] EnumResTypeProc lpEnumFunc, IntPtr lParam);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static internal extern bool EnumResourceNames (IntPtr hModule, IntPtr lpszType, EnumResNameProc lpEnumFunc, IntPtr lParam);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static internal extern bool EnumResourceNames (IntPtr hModule, string lpszType, EnumResNameProc lpEnumFunc, IntPtr lParam);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static internal extern bool EnumResourceLanguages (IntPtr hModule, IntPtr lpszType, string lpName, EnumResLangProc lpEnumFunc, IntPtr lParam);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static internal extern bool EnumResourceLanguages (IntPtr hModule, string lpszType, string lpName, EnumResLangProc lpEnumFunc, IntPtr lParam);
    }
}
