//! \file       ArcARC.cs
//! \date       2018 May 05
//! \brief      GSS engine resource archive.
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
using System.Diagnostics;
using System.IO;

namespace GameRes.Formats.Gss
{
    [Export(typeof(ArchiveFormat))]
    public class LsdOpener : ArchiveFormat
    {
        public override string Tag { get { return "ARC/LSD"; } }
        public override string Description { get { return "GSS engine resource archive"; } }
        public override uint Signature { get { return 0; } }
        public override bool IsHierarchic { get { return false; } }
        public override bool CanWrite { get { return false; } }

        public override ArcFile TryOpen(ArcView file)
        {
            if (!file.Name.HasExtension(".arc"))
                return null;
            var bin_name = Path.ChangeExtension(file.Name, "BIN");
            if (!VFS.FileExists(bin_name))
                return null;
            using (var bin = VFS.OpenView(bin_name))
            {
                if (!bin.View.AsciiEqual(0, "LSDARC V.100"))
                    return null;
                int count = bin.View.ReadInt32(0xC);
                if (!IsSaneCount(count))
                    return null;
                using (var index = bin.CreateStream())
                {
                    index.Position = 0x10;
                    var dir = new List<Entry>(count);
                    for (int i = 0; i < count; ++i)
                    {
                        var entry = new PackedEntry();
                        entry.IsPacked = index.ReadInt32() != 0;
                        entry.Offset = index.ReadUInt32();
                        entry.UnpackedSize = index.ReadUInt32();
                        entry.Size = index.ReadUInt32();
                        entry.Name = index.ReadCString();
                        if (!entry.CheckPlacement(file.MaxOffset))
                            return null;
                        dir.Add(entry);
                    }
                    return new ArcFile(file, this, dir);
                }
            }
        }

        public override Stream OpenEntry(ArcFile arc, Entry entry)
        {
            var pent = entry as PackedEntry;
            if (null == pent || !pent.IsPacked || !arc.File.View.AsciiEqual(entry.Offset, "LSD\x1A"))
                return base.OpenEntry(arc, entry);
            byte enc_method = arc.File.View.ReadByte(entry.Offset + 4);
            byte pack_method = arc.File.View.ReadByte(entry.Offset + 5);
            uint unpacked_size = arc.File.View.ReadUInt32(entry.Offset + 6);
            int len;
            using (var input = arc.File.CreateStream(entry.Offset + 12, entry.Size - 12))
            {
                var buf_packed = new byte[unpacked_size > entry.Size ? unpacked_size : entry.Size];
                input.Read(buf_packed, 0, (int)entry.Size - 12);
                var output = new byte[unpacked_size];
                switch ((char)pack_method)
                {
                    case 'D': len = UnpackD(buf_packed, output, unpacked_size); break; //sub_81043C02, sub_81048646
                    case 'R': len = UnpackR(input, output); break; //sub_81043AA6
                    case 'H': len = UnpackH(buf_packed, output, unpacked_size);break; //sub_81043752
                    case 'W': var v11 = UnpackW(buf_packed, output, unpacked_size);  //sub_81043414
                              var v12 = buf_packed[0];
                              len = decrypt(output, v11 - v12, (char)enc_method, v12);
                              break;
                    default: len = input.Read(output, 0, output.Length); break;
                }
                decrypt(output, len, (char)enc_method); //sub_81043340
                return new BinMemoryStream(output, entry.Name);
            }
        }

        public override IImageDecoder OpenImage(ArcFile arc, Entry entry)
        {
            throw new NotImplementedException();
        }

        int UnpackD(byte[] buf_packed, byte[] output, uint unpacked_size)
        {
            int result = 0, i=0, v8, v12;
            int v3 = 0, qword_811C97A8 = 0;
            var size1 = unpacked_size >> 1;
            byte v6, v16;
            if ((unpacked_size >> 1) != 0)
            {
                int cur_output_addr = 0;
                int cur_pos = 0;
                do
                {
                    v6 = (byte)(v3 & 0x1F);
                    int v7 = (int)((v3 >> 3) & 0xFFFFFFFC);
                    if ((int)(v3 & 0x1F) < 28)
                    {
                        v8 = BitConverter.ToInt32(buf_packed, v7);     
                    }
                    else
                    {
                        v8 = BitConverter.ToInt32(buf_packed, v7 + 1);
                        v6 -= (byte)8;
                    }
                    var v23 = v8 >> v6;
                    qword_811C97A8  +=  5;
                    byte v10 = (byte)(qword_811C97A8 & 0x1F);
                    int idx = v23 & 0xF ;
                    int v11 = (int)(((qword_811C97A8) >> 3) & 0xFFFFFFFC);
                    if (v10 >= 24)
                    {
                        v12 = BitConverter.ToInt32(buf_packed, v11 + 3);
                        v16 = (byte)(v10 - 24);
                    }
                    else if (v10 >= 16)
                    {
                            v12 = BitConverter.ToInt32(buf_packed, v11 + 2);
                            v16 = (byte)(v10 - 16);
                    }
                    else
                    {
                        if (v10 < 8)
                        {
                            v12 = BitConverter.ToInt32(buf_packed, v11);
                        }
                        else
                        {
                            v12 = BitConverter.ToInt32(buf_packed, v11 + 1);
                            v10 -= 8;
                        }
                        v16 = v10;
                    }
                    var v19 = (dword_8107F750[idx] & (v12 >> v16)) + dword_8107F7A4[0x10 + idx];
                    if ((v23 & 0x10) != 0)
                        v19 = v19 & 0xffff0000 |  (~v19 & 0xffff);
                    output[cur_output_addr++] = (byte)(v19); //word
                    output[cur_output_addr++] = (byte)(v19 >> 8);
                    qword_811C97A8 += (int)dword_8107F7A4[idx];
                    ++i;
                    v3 = qword_811C97A8;
                    cur_pos += 2;
                }
                while (i != size1);
                result = cur_pos + 2;
            }
            return result;
        }

        int UnpackR(IBinaryStream input, byte[] output) //sub_81043AA6
        {
            throw new NotImplementedException();
            int dst = 0;
            while (dst < output.Length)
            {
                int ctl = input.ReadByte();
                if (-1 == ctl)
                    break;
                int count;
                if ((ctl & 0xC0) == 0xC0)
                {
                    count = ctl & 0xF;
                    ctl &= 0xF0;
                }
                else
                {
                    count = ctl & 0x3F;
                    ctl &= 0xC0;
                }
                switch (ctl)
                {
                    case 0xF0: return 0;
                    case 0x40:
                        input.Read(output, dst, count);
                        dst += count;
                        break;

                    case 0xD0:
                        count = count << 8 | input.ReadUInt8();
                        input.Read(output, dst, count);
                        dst += count;
                        break;

                    case 0x80:
                        {
                            byte v = input.ReadUInt8();
                            while (count-- > 0)
                                output[dst++] = v;
                            break;
                        }

                    case 0xE0:
                        {
                            count = count << 8 | input.ReadUInt8();
                            byte v = input.ReadUInt8();
                            while (count-- > 0)
                                output[dst++] = v;
                            break;
                        }

                    case 0x00:
                        dst += count;
                        break;

                    case 0xC0:
                        count = count << 8 | input.ReadUInt8();
                        dst += count;
                        break;
                }
            }
        }

        int UnpackH(byte[] buf_packed, byte[] output, uint unpacked_size)
        {
            var buf = new Byte[0x10000];
            Array.Clear(buf, 0, buf.Length);
            const uint off2 = 0x2004;
            const uint off3 = 0x2804;
            const uint off4 = 0x3004; //0x811C9784

            uint cur_addr = 2, pre_pos, next_pos = 2, cur_output_addr = 0;
            byte outchar, i1 = 0, i2, v29, first_char = buf_packed[0];
            int idx1 = 0, v28=0;
            do
            {
                pre_pos = next_pos;
                var cur_char1 = buf_packed[cur_addr];
                var next_addr = cur_addr + 1;
                next_pos++;
                if (cur_char1 != 0)
                {
                    byte l1 = buf_packed[cur_addr + 1];
                    byte h1 = buf_packed[cur_addr + 2];
                    uint t1 = (ushort)(l1 + (h1 << 8));
                    if (cur_char1 >= 8)
                    {
                        if (cur_char1 >= 0xD)
                        {
                            uint d;
                            if (cur_char1 < 0x10) // 2 byte
                            {
                                d = t1;
                                next_addr = cur_addr + 3;
                                next_pos = pre_pos + 3;
                            }
                            else //3 byte
                            {
                                d = (uint)(t1 + (buf_packed[cur_addr + 3] << 16));
                                next_addr = cur_addr + 4;
                                next_pos = pre_pos + 4;
                            }
                            buf[off3 + 4 + 8 * i1] = cur_char1;
                            BitConverter.GetBytes((uint)(d)).CopyTo(buf, off3 + 8 * i1);
                            buf[off3 + 5 + 8 * i1] = (byte)idx1;
                            i1++;
                        }
                        else //2byte
                        {
                            buf[2 * t1] = (byte)idx1;
                            buf[2 * t1 + 1] = cur_char1;
                            next_addr = cur_addr + 3;
                            next_pos = pre_pos + 3;
                        }
                    }
                    else // 1byte
                    {
                        buf[2 * l1] = (byte)idx1;
                        buf[2 * l1 + 1] = cur_char1;
                        next_addr = cur_addr + 2;
                        next_pos = pre_pos + 2;
                    }
                }
                cur_addr = next_addr;
                ++idx1;
            } while (idx1 != 0x100);

            byte idx2 = 0;
            byte v19 = 0xD;
            do
            {
                i2 = 0;
                buf[off4 + v19] = (byte)idx2;
                if (i1 != 0)
                {
                    int buf3_addr = 0x2804; //buf3_addr = (_BYTE *)&dword_811C8F84;
                    do
                    {
                        if (buf[buf3_addr + 4] == v19)
                        {
                            Array.Copy(buf, buf3_addr, buf, off2 + 8 * idx2, 4);
                            Array.Copy(buf, buf3_addr+4, buf, off2 + 4 + 8 * idx2, 4); 
                            idx2++;
                        }
                        ++i2;
                        buf3_addr += 8;
                    }
                    while (i2 != i1);
                }
                v19++;
            } while (v19 != 0x18);

            buf[0x301c] = (byte)idx2; //unk_811C979C = idx2;
            Array.Clear(buf, 0x3028, 8);//qword_811C97A8 = 0i64;
            int v23 = (int)unpacked_size, v33=0, size_done = 0;
            while (true)
            {
                int v24 = (int)(next_pos + ((uint)(v33 & 0x1F) >> 3) + ((v33 >> 3) & 0xFFFFFFFC)); //this place, out of range
                ulong v25 = BitConverter.ToUInt32(buf_packed, v24);
                byte cur_char2 = first_char;
                uint v27 = sub_8105E56C((uint)v25, (uint)(v25 >> 32), (uint)((v33 & 0x1F) - (v33 & 0x18)));
                if (first_char != 0xD)
                {
                    while (true)
                    { 
                        v28 = (int)(v27 & dword_8107F828[2 * cur_char2]);
                        BitConverter.GetBytes((int)(2 * v28 + 0x811C6780)).CopyTo(buf, off2 - 4); //must pay attention to the type convert and length
                        if (cur_char2 == buf[2 * v28 + 1])// buf1+1
                            break;
                        cur_char2++;
                        if (cur_char2 == 0xD)
                            goto LABEL_22;
                    }
                    outchar = buf[2 * v28];
                    goto LABEL_29;
                }
            LABEL_22:
                if (cur_char2 == 0x18)
                    break;
            LABEL_26_2:
                while (true)
                {
                    v29 = buf[off4 + cur_char2];
                    if (v29 != buf[off4 + 1 + cur_char2])
                        break;
                    LABEL_26: //goto can not jump here
                    if (++cur_char2 == 0x18)
                    {
                        return 0;
                        //outchar = buf[off2 + 5 + 8 * v29]; //this seems a hack...
                        //goto LABEL_29;                   
                    }
                }
                while (BitConverter.ToUInt32(buf, (int)off2 + 8 * v29) !=
                       (v27 & dword_8107F828[2 * cur_char2]))
                {
                    v29++;
                    if (v29 == buf[off4 + 1 + cur_char2 ])
                    {
                        if (++cur_char2 == 0x18)
                        {
                            return 0;
                        }
                        goto LABEL_26_2;
                    }
                }
                outchar = buf[off2 + 5 + 8 * v29];
            LABEL_29:
                output[cur_output_addr] = (byte)outchar;
                BitConverter.GetBytes((Int64)v33 + (ushort)cur_char2).CopyTo(buf, 0x3028);
                v23 = size_done + 1;
                ++cur_output_addr;
                v33 += cur_char2 ;
                size_done = v23;
                if (v23 >= unpacked_size)
                    return size_done;
            }
            return 0;
        }
        uint sub_8105E56C(uint result, uint a2, uint a3)
        {
            if (a3 > 63)
            {
                result = 0;
            }
            else if (a3 != 0)
            {
                if (a3 > 31)
                    result = a2 >> (int)(a3 - 32);
                else
                    result = (a2 << (int)(32 - a3)) | (result >> (int)a3);
                //insert the low a3 bit of the result into a2, then asign to result
            }
            return result;
        }

        int UnpackW(byte[] buf_packed, byte[] output, uint unpacked_size)
        {
            throw new NotImplementedException();
            /*
            int header_length = input.ReadUInt8();
            int shift = input.ReadUInt8();
            input.Read (output, 0, header_length);
            int dst = header_length & ~1;
            int v7 = 0;
            while (dst < output.Length)
            {
                int bit = v7 & 0x1F;
                int v9 = (v7 >> 3) & 0x1FFFFFFC;
                int pre_addr;
                if (bit < 0x1C)
                {
                    pre_addr = input.ReadInt32(); //MemInt32(&src[v9]);
                }
                else
                {
                    pre_addr = input.ReadInt32(); //MemInt32(&src[v9 + 1]);
                    bit -= 8;
                }
                v7 += 5;
                int l1 = (pre_addr >> bit) & 0x1F;
                bit = v7 & 0x1F;
                int v14 = (v7 >> 3) & 0x1FFFFFFC;
                if (bit < 8)
                {
                    pre_addr = MemInt32(&src[v14]);
                }
                else if (bit < 0x10)
                {
                    pre_addr = MemInt32(&src[v14 + 1]);
                    bit -= 8;
                }
                else if (bit < 0x18)
                {
                    pre_addr = MemInt32(&src[v14 + 2]);
                    bit -= 16;
                }
                else
                {
                    pre_addr = MemInt32(&src[v14 + 3]);
                    bit -= 24;
                }
                v17 = l1 & 0xF;
                int sample = dword_455580[v17] + (((pre_addr >> bit) & (dword_4554E8[v17] >> shift)) << shift);
                if ((l1 & 0x10) != 0)
                    sample = -sample;
                LittleEndian.Pack ((short)sample, output, dst);
                dst += 2;
                v20 = dword_455540[v17];
                if (v20 > shift)
                {
                    v7 += v20 - shift;
                }
            }
            */
        }

        int decrypt(byte[] output, int len, char enc_method, int start_pos=0) 
        {
            return 0;
        }

        static readonly int[] dword_455540 = {
            0x0, 0x0, 0x0, 0x0, 0x3, 0x4, 0x5, 0x6,
            0x7, 0x8, 0x9, 0xA, 0xB, 0xC, 0xD, 0xE
        };
        static readonly int[] dword_455580 = {
            0x00, 0x01, 0x02, 0x04, 0x08, 0x10, 0x20, 0x40, 0x80,
            0x0100, 0x0200, 0x0400, 0x0800, 0x1000, 0x2000, 0x4000
        };
        static readonly int[] dword_4554E8 = { //8107F748
            0x00, 0x00, 0x00, 0x00, 0x07, 0x0F, 0x1F, 0x3F,
            0x7F, 0xFF, 0x01FF, 0x03FF, 0x07FF, 0x0FFF, 0x1FFF, 0x3FFF
        };
        static readonly uint[] dword_8107F828 = { 
            0x00000000, 0x00000000, 0x00000001, 0x00000000, 0x00000003, 0x00000000, 0x00000007, 0x00000000,
            0x0000000F, 0x00000000, 0x0000001F, 0x00000000, 0x0000003F, 0x00000000, 0x0000007F, 0x00000000,
            0x000000FF, 0x00000000, 0x000001FF, 0x00000000, 0x000003FF, 0x00000000, 0x000007FF, 0x00000000,
            0x00000FFF, 0x00000000, 0x00001FFF, 0x00000000, 0x00003FFF, 0x00000000, 0x00007FFF, 0x00000000,
            0x0000FFFF, 0x00000000, 0x0001FFFF, 0x00000000, 0x0003FFFF, 0x00000000, 0x0007FFFF, 0x00000000,
            0x000FFFFF, 0x00000000, 0x001FFFFF, 0x00000000, 0x003FFFFF, 0x00000000, 0x007FFFFF, 0x00000000,
            0x00FFFFFF, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000007, 0x0000000F,
            0x0000001F, 0x0000003F, 0x0000007F, 0x000000FF, 0x000001FF, 0x000003FF, 0x000007FF, 0x00000FFF,
            0x00001FFF, 0x00003FFF, 0x00007FFF, 0x0000FFFF, 0x0001FFFF, 0x0003FFFF, 0x000FFFFF, 0x00000000,
            0x06050403, 0x0A090807, 0x0E0D0C0B, 0x00000000, 0x00000001, 0x00000002, 0x00000004, 0x00000008,
            0x00000010, 0x00000020, 0x00000040, 0x00000080, 0x00000100, 0x00000200, 0x00000400, 0x00000800,
            0x00001000, 0x00002000, 0x00004000, 0x00000000, 0xFFFF0001, 0x00000000, 0x00000000, 0x00000001,
            0xFFFFFFFF, 0xFFFFFFFF, 0xFFFFFFFF, 0x00000001, 0x00000001,
        };
        static readonly uint[] dword_8107F750 ={
            0x00000000, 0x00000000, 0x00000001, 0x00000003, 0x00000007, 0x0000000F, 0x0000001F, 0x0000003F,
            0x0000007F, 0x000000FF, 0x000001FF, 0x000003FF, 0x000007FF, 0x00000FFF, 0x00001FFF, 0x00003FFF,
            0x00007FFF, 0x0000FFFF, 0x0001FFFF, 0x0003FFFF, 0x000FFFFF, 0x00000000, 0x00000000, 0x00000001,
        };
        static readonly uint[] dword_8107F7A4 = {
            0x00000000, 0x00000000, 0x00000001, 0x00000002, 0x00000003, 0x00000004, 0x00000005, 0x00000006,
            0x00000007, 0x00000008, 0x00000009, 0x0000000A, 0x0000000B, 0x0000000C, 0x0000000D, 0x0000000E,
            0x00000000, 0x00000001, 0x00000002, 0x00000004, 0x00000008, 0x00000010, 0x00000020, 0x00000040,
            0x00000080, 0x00000100, 0x00000200, 0x00000400, 0x00000800, 0x00001000, 0x00002000, 0x00004000,
            0x00000000, 0x00000000, 0x00000000, 0x00000001, 0x00000000, 0x00000003, 0x00000000, 0x00000007,
            0x00000000, 0x0000000F, 0x00000000, 0x0000001F, 0x00000000, 0x0000003F, 0x00000000, 0x0000007F,
            0x00000000, 0x000000FF, 0x00000000, 0x000001FF, 0x00000000, 0x000003FF, 0x00000000, 0x000007FF,
            0x00000000, 0x00000FFF, 0x00000000, 0x00001FFF, 0x00000000, 0x00003FFF, 0x00000000, 0x00007FFF,
            0x00000000, 0x0000FFFF, 0x00000000, 0x0001FFFF, 0x00000000, 0x0003FFFF, 0x00000000, 0x0007FFFF,
            0x00000000, 0x000FFFFF, 0x00000000, 0x001FFFFF, 0x00000000, 0x003FFFFF, 0x00000000, 0x007FFFFF,
            0x00000000, 0x00FFFFFF, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000007,
            0x0000000F, 0x0000001F, 0x0000003F, 0x0000007F, 0x000000FF, 0x000001FF, 0x000003FF, 0x000007FF,
            0x00000FFF, 0x00001FFF, 0x00003FFF, 0x00007FFF, 0x0000FFFF, 0x0001FFFF, 0x0003FFFF, 0x000FFFFF,
        };
    }
}
