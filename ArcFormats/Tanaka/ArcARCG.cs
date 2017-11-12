//! \file       ArcARCG.cs
//! \date       Wed Feb 01 06:41:10 2017
//! \brief      Archive format by Tanaka Tatsuhiro.
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
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using GameRes.Cryptography;
using GameRes.Utility;

namespace GameRes.Formats.Will
{
    [Export(typeof(ArchiveFormat))]
    public class ArcGOpener : ArchiveFormat
    {
        public override string         Tag { get { return "ARCG"; } }
        public override string Description { get { return "Tanaka Tatsuhiro's engine resource archive"; } }
        public override uint     Signature { get { return 0x47435241; } } // 'ARCG'
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public ArcGOpener ()
        {
            Extensions = new string[] { "arc", "bmx", "scb", "vpk" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (0x10000 != file.View.ReadUInt32 (4))
                return null;
            int index_offset = file.View.ReadInt32 (8);
            int index_size   = file.View.ReadInt32 (0xC);
            int dir_count = file.View.ReadUInt16 (0x10);
            int count = file.View.ReadInt32 (0x12);
            int base_offset = index_offset;
            byte[] index = null;
            if (0 == index_offset)
            {
                if (VFS.IsVirtual || !file.Name.HasExtension ("bmx"))
                    return null;
                var bmi_name = Path.ChangeExtension (file.Name, "bmi");
                index = ReadIndex (bmi_name);
                if (null == index || !index.AsciiEqual ("ARCG") || index.ToUInt32 (4) != 0x10000)
                    return null;
                index_offset = index.ToInt32 (8);
                index_size   = index.ToInt32 (0xC);
                dir_count = index.ToUInt16 (0x10);
                count = index.ToInt32 (0x12);
                base_offset = 0;
            }
            else
            {
                if (index_offset >= file.MaxOffset)
                    return null;
                index = file.View.ReadBytes (index_offset, (uint)index_size);
            }
            if (!IsSaneCount (count) || index_size > index.Length)
                return null;
            int index_pos = index_offset - base_offset;
            var dir = new List<Entry> (count);
            for (int j = 0; j < dir_count; ++j)
            {
                int name_length = index[index_pos];
                var dir_name = Binary.GetCString (index, index_pos+1, name_length-1);
                index_pos += name_length; 
                int dir_offset = index.ToInt32 (index_pos) - base_offset;
                int file_count = index.ToInt32 (index_pos+4);
                if (dir_offset < 0 || dir_offset >= index.Length || file_count < 0 || file_count > count)
                    return null;
                index_pos += 8;
                for (int i = 0; i < file_count; ++i)
                {
                    name_length = index[dir_offset];
                    if (0 == name_length)
                        return null;
                    var file_name = Binary.GetCString (index, dir_offset+1, name_length-1);
                    file_name = file_name.Replace ('?', '？');
                    dir_offset += name_length;
                    file_name = Path.Combine (dir_name, file_name);
                    var entry = FormatCatalog.Instance.Create<Entry> (file_name);
                    entry.Offset = index.ToUInt32 (dir_offset);
                    entry.Size   = index.ToUInt32 (dir_offset+4);
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir_offset += 8;
                    dir.Add (entry);
                }
            }
            foreach (var entry in dir.Where (e => string.IsNullOrEmpty (e.Type)))
            {
                uint signature = file.View.ReadUInt32 (entry.Offset);
                IResource res;
                if ((signature & 0xFFFF) == 0x4342) // 'BC'
                    res = BcFormat.Value;
                else
                    res = AutoEntry.DetectFileType (signature);
                if (res != null)
                    entry.Type = res.Type;
            }
            return new ArcFile (file, this, dir);
        }

        byte[] ReadIndex (string bmi_name)
        {
            var index = File.ReadAllBytes (bmi_name);
            uint signature = index.ToUInt32 (0);
            string passkey;
            if (!KnownKeys.TryGetValue (signature, out passkey))
            {
                var root = Path.GetPathRoot (bmi_name);
                if (string.IsNullOrEmpty (root))
                    return null;
                uint serial;
                if (!GetVolumeInformation (root, IntPtr.Zero, 0, out serial,
                                           IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0))
                    return null;
                passkey = string.Format ("{0:x4}{1}", serial, (int)serial);
            }
            uint seed = GetSeedFromString (passkey);
            var twister = new MersenneTwister (seed);
            unsafe
            {
                fixed (byte* idx8 = index)
                {
                    uint* dst = (uint*)idx8;
                    for (int n = index.Length / 4; n > 0; --n)
                    {
                        *dst++ ^= twister.Rand();
                    }
                }
            }
            return index;
        }

        uint GetSeedFromString (string passphrase)
        {
            if (string.IsNullOrEmpty (passphrase))
                return 0;
            var buf = Encodings.cp932.GetBytes (passphrase);
            int seed = (sbyte)buf[0];
            for (int i = 1; i < buf.Length; ++i)
                seed *= (sbyte)buf[i];
            return (uint)seed;
        }
        
        [DllImport("Kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public extern static bool GetVolumeInformation(
            string rootPathName, IntPtr volumeNameBuffer, int volumeNameSize,
            out uint volumeSerialNumber, IntPtr maximumComponentLength, IntPtr fileSystemFlags,
            IntPtr fileSystemNameBuffer, int nFileSystemNameSize);

        BmiScheme KnownSchemes = new BmiScheme();

        public override ResourceScheme Scheme
        {
            get { return KnownSchemes; }
            set { KnownSchemes = (BmiScheme)value; }
        }

        internal IDictionary<uint, string> KnownKeys {
            get { return KnownSchemes.KnownKeys ?? new Dictionary<uint, string>(); }
        }

        internal static Lazy<ImageFormat> BcFormat = new Lazy<ImageFormat> (() => ImageFormat.FindByTag ("BC"));
    }

    [Serializable]
    public class BmiScheme : ResourceScheme
    {
        public IDictionary<uint, string>  KnownKeys;
    }
}
