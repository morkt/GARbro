//! \file       ArcDAT.cs
//! \date       2023 Sep 04
//! \brief      OMI Script Engine resource archive.
//
// Copyright (C) 2023 by morkt
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

using GameRes.Utility;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Text;

namespace GameRes.Formats.Omi
{
    [Export(typeof(ArchiveFormat))]
    public class DatOpener : ArchiveFormat
    {
        public override string         Tag => "DAT/OMI";
        public override string Description => "OMI Script Engine resource archive";
        public override uint     Signature => 0;
        public override bool  IsHierarchic => false;
        public override bool      CanWrite => false;

        internal const uint DefaultKey = 7654321u;

        public DatOpener ()
        {
            ContainedFormats = new[] { "BMP", "TGA", "WAV", "TXT" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!VFS.IsPathEqualsToFileName (file.Name, "scrdat"))
                return null;
            using (var input = file.CreateStream())
            using (var index = new DecryptedStream (input, DefaultKey, 0))
            {
                var line = index.ReadLine();
                int count = int.Parse (line);
                if (!IsSaneCount (count))
                    return null;
                var dir = new List<Entry> (count);
                for (int i = 0; i < count; ++i)
                {
                    var name = index.ReadLine();
                    line = index.ReadLine();
                    uint size = uint.Parse (line);
                    var entry = Create<PackedEntry> (name);
                    entry.Size = size;
                    entry.IsPacked = entry.Type == "image";
                    dir.Add (entry);
                }
                long data_pos = index.Position;
                for (int i = 0; i < count; ++i)
                {
                    dir[i].Offset = data_pos;
                    if (!dir[i].CheckPlacement (file.MaxOffset))
                        return null;
                    data_pos += dir[i].Size;
                }
                return new ArcFile (file, this, dir);
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var pent = (PackedEntry)entry;
            Stream input = arc.File.CreateStream (entry.Offset, entry.Size);
            input = new DecryptedStream (input, DefaultKey, (uint)entry.Offset);
            if (!pent.IsPacked)
                return input;
            using (var packed = new BinaryStream (input, pent.Name))
            {
                var unpacked = DecompressRle (packed);
                if (pent.UnpackedSize == 0)
                    pent.UnpackedSize = (uint)unpacked.Length;
                return new BinMemoryStream (unpacked, pent.Name);
            }
        }

        internal static byte[] DecompressRle (IBinaryStream input)
        {
            int size = input.ReadInt32();
            var output = new byte[size * 2];
            ushort rle_marker = input.ReadUInt16();
            int dst = 0;
            while (dst < output.Length)
            {
                input.Read (output, dst, 2);
                if (output.ToUInt16 (dst) == rle_marker)
                {
                    input.Read (output, dst, 2);
                    dst += 2;
                    int count = input.ReadUInt16() - 1;
                    if (count > 0)
                    {
                        count *= 2;
                        Binary.CopyOverlapped (output, dst-2, dst, count);
                        dst += count;
                    }
                }
                else
                {
                    dst += 2;
                }
            }
            return output;
        }
    }

    internal class DecryptedStream : InputProxyStream
    {
        private uint        m_key;

        static readonly Encoding Encoding = Encodings.cp932;

        public override bool CanSeek { get => false; }
        public override long Position
        {
            get => BaseStream.Position;
            set => throw new NotSupportedException ("Stream.Position property is not supported");
        }

        public DecryptedStream (Stream stream, uint key, uint start_offset) : base (stream)
        {
            if (start_offset > 0)
            {
                do
                {
                    key = 5 * key - 3;
                }
                while (--start_offset > 0);
            }
            m_key = key;
        }

        public override int Read (byte[] buffer, int offset, int count)
        {
            int read = BaseStream.Read (buffer, offset, count);
            Decrypt (buffer, offset, read);
            return read;
        }

        byte[] m_byte_buffer = new byte[1];

        public override int ReadByte ()
        {
            int b = BaseStream.ReadByte();
            if (-1 != b)
            {
                m_byte_buffer[0] = (byte)b;
                Decrypt (m_byte_buffer, 0, 1);
                b = m_byte_buffer[0];
            }
            return b;
        }

        internal void Decrypt (byte[] data, int offset, int count)
        {
            for (int i = 0; i < count; ++i)
            {
                data[offset+i] = (byte)(Binary.RotByteR (data[offset+i], 1) - m_key);
                m_key = 5 * m_key - 3;
            }
        }

        byte[] m_buffer;

        public string ReadLine ()
        {
            if (null == m_buffer)
                m_buffer = new byte[32];
            int size = 0;
            for (;;)
            {
                int b = ReadByte();
                if (-1 == b || '\n' == b)
                    break;
                if (m_buffer.Length == size)
                {
                    Array.Resize (ref m_buffer, checked(size/2*3));
                }
                m_buffer[size++] = (byte)b;
            }
            return Encoding.GetString (m_buffer, 0, size);
        }

        public override long Seek (long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }
    }
}
