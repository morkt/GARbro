//! \file       ArcDAT.cs
//! \date       2018 Nov 17
//! \brief      KID resource archive.
//
// Copyright (C) 2018 by morkt
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
using System.Security.Cryptography;
using GameRes.Utility;

namespace GameRes.Formats.Kid
{
    [Export(typeof(ArchiveFormat))]
    public class LnkOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DAT/LNK"; } }
        public override string Description { get { return "KID resource archive"; } }
        public override uint     Signature { get { return 0x4B4E4C; } } // 'LNK'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        LnkOpener ()
        {
            ContainedFormats = new[] { "PRT", "WAF" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (4);
            if (!IsSaneCount (count))
                return null;
            uint index_offset = 0x10;
            long data_offset = index_offset + count * 32;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                uint offset = file.View.ReadUInt32 (index_offset);
                uint size   = file.View.ReadUInt32 (index_offset+4);
                var name = file.View.ReadString (index_offset+8, 0x18);
                if (string.IsNullOrWhiteSpace (name))
                    return null;
                var entry = Create<PackedEntry> (name);
                entry.Offset = data_offset + offset;
                entry.Size = size >> 1;
                entry.IsPacked = (size & 1) != 0;
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 0x20;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            IBinaryStream input = arc.File.CreateStream (entry.Offset, entry.Size, entry.Name);
            var pent = entry as PackedEntry;
            if (pent != null && pent.IsPacked && input.Signature == 0x646E6C) // 'lnd'
            {
                using (var lnd = input)
                {
                    lnd.Position = 8;
                    pent.UnpackedSize = lnd.ReadUInt32();
                    lnd.ReadInt32();
                    var data = new byte[pent.UnpackedSize];
                    UnpackLnd (lnd, data);
                    input = new BinMemoryStream (data, entry.Name);
                }
            }
            if (input.Signature == 0x535043) // 'CPS'
            {
                using (input)
                    return UnpackCps (input);
            }
            return input.AsStream;
        }

        internal static void UnpackLnd (IBinaryStream input, byte[] output)
        {
            int unpacked_size = output.Length;
            int dst = 0;
            while (dst < unpacked_size)
            {
                int ctl = input.ReadByte();
                if (-1 == ctl)
                    break;
                if ((ctl & 0x80) != 0)
                {
                    if ((ctl & 0x40) != 0)
                    {
                        int count = (ctl & 0x1F) + 2;
                        if ((ctl & 0x20) != 0)
                            count += input.ReadUInt8() << 5;
                        count = Math.Min (count, unpacked_size - dst);
                        byte v = input.ReadUInt8();
                        for (int i = 0; i < count; ++i)
                            output[dst++] = v;
                    }
                    else
                    {
                        int count = ((ctl >> 2) & 0xF) + 2;
                        int offset = ((ctl & 3) << 8) + input.ReadUInt8() + 1;
                        count = Math.Min (count, unpacked_size - dst);
                        Binary.CopyOverlapped (output, dst - offset, dst, count);
                        dst += count;
                    }
                }
                else if ((ctl & 0x40) != 0)
                {
                    int length = Math.Min ((ctl & 0x3F) + 2, unpacked_size - dst);
                    int count = input.ReadUInt8();
                    input.Read (output, dst, length);
                    dst += length;
                    count = Math.Min (count * length, unpacked_size - dst);
                    if (count > 0)
                    {
                        Binary.CopyOverlapped (output, dst - length, dst, count);
                        dst += count;
                    }
                }
                else
                {
                    int count = (ctl & 0x1F) + 1;
                    if ((ctl & 0x20) != 0)
                        count += input.ReadUInt8() << 5;
                    count = Math.Min (count, unpacked_size - dst);
                    input.Read (output, dst, count);
                    dst += count;
                }
            }
        }

        void UnpackLnd16 (IBinaryStream input, byte[] output)
        {
            throw new NotImplementedException ("KID Lnd16 compression not implemented.");
        }

        Stream UnpackCps (IBinaryStream input)
        {
            input.Seek (-4, SeekOrigin.End);
            uint key_offset = input.ReadUInt32() - 0x7534682;
            input.Position = key_offset;
            uint key = input.ReadUInt32() + key_offset + 0x3786425;

            var header = input.ReadHeader (0x10);
            int packed_size = header.ToInt32 (4);
            int compression = header.ToUInt16 (0xA);
            int unpacked_size = header.ToInt32 (0xC);

            var decryptor = new CpsTransform (packed_size, (int)key_offset, key);
            using (var decoded = new InputCryptoStream (input.AsStream, decryptor))
            using (var cps = new BinaryStream (decoded, input.Name))
            {
                var output = new byte[unpacked_size];
                if ((compression & 1) != 0)
                {
                    cps.ReadInt32();
                    UnpackLnd (cps, output);
                }
                else if ((compression & 2) != 0)
                {
                    UnpackLnd16 (cps, output);
                }
                else
                {
                    cps.ReadInt32();
                    cps.Read (output, 0, unpacked_size);
                }
                return new BinMemoryStream (output);
            }
        }
    }

    internal sealed class CpsTransform : ICryptoTransform
    {
        const int BlockSize = 4;

        public bool          CanReuseTransform { get { return false; } }
        public bool CanTransformMultipleBlocks { get { return true; } }
        public int              InputBlockSize { get { return BlockSize; } }
        public int             OutputBlockSize { get { return BlockSize; } }

        int     m_data_length;
        int     m_key_offset;
        uint    m_key;
        int     m_position;

        public CpsTransform (int data_length, int key_offset, uint key)
        {
            m_data_length = data_length;
            m_key_offset = key_offset;
            m_key = key;
            m_position = 0x10;
        }

        public int TransformBlock (byte[] inputBuffer, int src, int inputCount,
                                   byte[] outputBuffer, int dst)
        {
            for (int i = 0; i < inputCount; i += BlockSize)
            {
                if (m_position == m_data_length - 4)
                {
                    LittleEndian.Pack (0, outputBuffer, dst);
                    break;
                }
                uint data = LittleEndian.ToUInt32 (inputBuffer, src);
                if (m_position != m_key_offset && m_key_offset != 0)
                    data -= m_key + (uint)m_data_length;
                LittleEndian.Pack (data, outputBuffer, dst);
                src += 4;
                dst += 4;
                m_position += 4;
                m_key = 1103515245 * m_key + 39686;
            }
            return inputCount;
        }

        public byte[] TransformFinalBlock (byte[] inputBuffer, int inputOffset, int inputCount)
        {
            byte[] outputBuffer = new byte[inputCount];
            TransformBlock (inputBuffer, inputOffset, inputCount, outputBuffer, 0);
            return outputBuffer;
        }

        public void Dispose ()
        {
        }
    }
}
