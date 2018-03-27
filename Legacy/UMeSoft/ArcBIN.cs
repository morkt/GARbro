//! \file       ArcBIN.cs
//! \date       2018 Mar 05
//! \brief      UmeSoft resource archive.
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
using GameRes.Utility;

namespace GameRes.Formats.UMeSoft
{
    [Export(typeof(ArchiveFormat))]
    public class BinOpener : ArchiveFormat
    {
        public override string         Tag { get { return "BIN/UME"; } }
        public override string Description { get { return "U-Me Soft resources archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (0);
            if ((count & 0xFFFF) != 0)
                return null;
            count = (count >> 16) - 1;
            if (!IsSaneCount (count))
                return null;

            uint index_offset = 0xC;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var name = file.View.ReadUInt32 (index_offset).ToString ("D5");
                var entry = new PackedEntry {
                    Name   = name,
                    Offset = file.View.ReadUInt32 (index_offset+4) << 11,
                    Size   = file.View.ReadUInt32 (index_offset+8),
                };
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 12;
            }
            foreach (PackedEntry entry in dir)
            {
                uint signature;
                if (entry.Size > 13 && file.View.AsciiEqual (entry.Offset+2, "ike"))
                {
                    int unpacked_size = IkeReader.DecodeSize (file.View.ReadByte (entry.Offset+10),
                                                              file.View.ReadByte (entry.Offset+11),
                                                              file.View.ReadByte (entry.Offset+12));
                    entry.IsPacked = true;
                    entry.UnpackedSize = (uint)unpacked_size;
                    signature = file.View.ReadUInt32 (entry.Offset+0xF);
                    entry.Offset += 13;
                    entry.Size   -= 13;
                }
                else
                    signature = file.View.ReadUInt32 (entry.Offset);
                entry.ChangeType (AutoEntry.DetectFileType (signature));
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var pent = entry as PackedEntry;
            if (null == pent || !pent.IsPacked)
                return base.OpenEntry (arc, entry);
            using (var input = arc.File.CreateStream (entry.Offset, entry.Size))
            {
                var reader = new IkeReader (input, (int)pent.UnpackedSize);
                var data = reader.Unpack();
                return new BinMemoryStream (data, entry.Name);
            }
        }
    }

    internal class IkeReader
    {
        IBinaryStream   m_input;
        byte[]          m_output;

        public IkeReader (IBinaryStream input, int unpacked_size)
        {
            m_input = input;
            m_output = new byte[unpacked_size];
        }

        public static int DecodeSize (byte a, byte b, byte c)
        {
            return b + ((c + (a >> 2 << 8)) << 8);
        }

        public static IBinaryStream CreateStream (IBinaryStream input, int unpacked_size)
        {
            input.Position = 0xD;
            var ike = new IkeReader (input, unpacked_size);
            var data = ike.Unpack();
            return new BinMemoryStream (data);
        }

        public byte[] Unpack ()
        {
            m_bits = 2;
            GetBit();
            int dst = 0;
            while (dst < m_output.Length)
            {
                int offset, shift, count;
                if (GetBit() != 0)
                {
                    m_output[dst++] = m_input.ReadUInt8();
                    continue;
                }
                if (GetBit() != 0)
                {
                    offset = m_input.ReadUInt8() | -0x100;
                    shift = 0;
                    if (GetBit() == 0)
                        shift += 0x100;
                    if (GetBit() == 0)
                    {
                        offset -= 0x200;
                        if (GetBit() == 0)
                        {
                            shift <<= 1;
                            if (GetBit() == 0)
                                shift += 0x100;
                            offset -= 0x200;
                            if (GetBit() == 0)
                            {
                                shift <<= 1;
                                if (GetBit() == 0)
                                    shift += 0x100;
                                offset -= 0x400;
                                if (GetBit() == 0)
                                {
                                    offset -= 0x800;
                                    shift <<= 1;
                                    if (GetBit() == 0)
                                        shift += 0x100;
                                }
                            }
                        }
                    }
                    offset -= shift;
                    if (GetBit() != 0)
                    {
                        count = 3;
                    }
                    else if (GetBit() != 0)
                    {
                        count = 4;
                    }
                    else if (GetBit() != 0)
                    {
                        count = 5;
                    }
                    else if (GetBit() != 0)
                    {
                        count = 6;
                    }
                    else if (GetBit() != 0)
                    {
                        if (GetBit() != 0)
                            count = 8;
                        else
                            count = 7;
                    }
                    else if (GetBit() != 0)
                    {
                        count = m_input.ReadUInt8() + 17;
                    }
                    else
                    {
                        count = 9;
                        if (GetBit() != 0)
                            count = 13;
                        if (GetBit() != 0)
                            count += 2;
                        if (GetBit() != 0)
                            count++;
                    }
                }
                else
                {
                    offset = m_input.ReadUInt8() | -0x100;
                    if (GetBit() != 0)
                    {
                        offset -= 0x100;
                        if (GetBit() == 0)
                            offset -= 0x400;
                        if (GetBit() == 0)
                            offset -= 0x200;
                        if (GetBit() == 0)
                            offset -= 0x100;
                    }
                    else if (offset == -1)
                    {
                        if (GetBit() == 0)
                            break;
                        else
                            continue;
                    }
                    count = 2;
                }
                count = Math.Min (count, m_output.Length - dst);
                Binary.CopyOverlapped (m_output, dst+offset, dst, count);
                dst += count;
            }
            return m_output;
        }

        int m_bits;

        int GetBit ()
        {
            int bit = m_bits & 1;
            m_bits >>= 1;
            if (1 == m_bits)
            {
                m_bits = m_input.ReadUInt16() | 0x10000;
            }
            return bit;
        }
    }
}
