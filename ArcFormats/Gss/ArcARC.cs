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
using System.IO;

namespace GameRes.Formats.Gss
{
    [Export(typeof(ArchiveFormat))]
    public class LsdOpener : ArchiveFormat
    {
        public override string         Tag { get { return "ARC/LSD"; } }
        public override string Description { get { return "GSS engine resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.HasExtension (".arc"))
                return null;
            var bin_name = Path.ChangeExtension (file.Name, "BIN");
            if (!VFS.FileExists (bin_name))
                return null;
            using (var bin = VFS.OpenView (bin_name))
            {
                if (!bin.View.AsciiEqual (0, "LSDARC V.100"))
                    return null;
                int count = bin.View.ReadInt32 (0xC);
                if (!IsSaneCount (count))
                    return null;
                using (var index = bin.CreateStream())
                {
                    index.Position = 0x10;
                    var dir = new List<Entry> (count);
                    for (int i = 0; i < count; ++i)
                    {
                        var entry = new PackedEntry();
                        entry.IsPacked     = index.ReadInt32() != 0;
                        entry.Offset       = index.ReadUInt32();
                        entry.UnpackedSize = index.ReadUInt32();
                        entry.Size         = index.ReadUInt32();
                        entry.Name         = index.ReadCString();
                        if (!entry.CheckPlacement (file.MaxOffset))
                            return null;
                        dir.Add (entry);
                    }
                    return new ArcFile (file, this, dir);
                }
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var pent = entry as PackedEntry;
            if (null == pent || !pent.IsPacked || !arc.File.View.AsciiEqual (entry.Offset, "LSD\x1A"))
                return base.OpenEntry (arc, entry);
            byte enc_method = arc.File.View.ReadByte (entry.Offset+4);
            byte pack_method = arc.File.View.ReadByte (entry.Offset+5);
            uint unpacked_size = arc.File.View.ReadUInt32 (entry.Offset+6);
            using (var input = arc.File.CreateStream (entry.Offset+12, entry.Size-12))
            {
                var data = new byte[unpacked_size];
                switch ((char)pack_method)
                {
                case 'D':   UnpackD (input, data); break; //sub_81043C02(v9, v3, v6);
                case 'R':   UnpackR (input, data); break; //sub_81043AA6(v9, v3)
                case 'H':
                        var buf_packed = new byte[entry.Size - 12];
                        input.Read(buf_packed, 0, (int)entry.Size - 12);
                        UnpackH (buf_packed, data, unpacked_size); 
                        break; //sub_81043752(v9, v3, v6);
                case 'W':   UnpackW (input, data); break; //sub_81043414£¬ sub_81043340
                default:    input.Read (data, 0, data.Length); break;
                }
                switch ((char)enc_method)
                {
                case 'B':
                case 'W':
                case 'S':
                    break;
                }
                return new BinMemoryStream (data, entry.Name);
            }
        }

        public override IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            throw new NotImplementedException();
        }

        void UnpackD (IBinaryStream input, byte[] output)
        {
            throw new NotImplementedException();
            input.Read(output, 0, output.Length);
            return;
        }

        void UnpackR (IBinaryStream input, byte[] output) //sub_81043AA6
        {
            //throw new NotImplementedException();
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
                case 0xF0: return;
                case 0x40:
                    input.Read (output, dst, count);
                    dst += count;
                    break;

                case 0xD0:
                    count = count << 8 | input.ReadUInt8();
                    input.Read (output, dst, count);
                    dst += count;
                    break;

                case 0x80:
                    {
                        byte v = input.ReadUInt8();
                        while (count --> 0)
                            output[dst++] = v;
                        break;
                    }

                case 0xE0:
                    {
                        count = count << 8 | input.ReadUInt8();
                        byte v = input.ReadUInt8();
                        while (count --> 0)
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

        unsafe void UnpackH (byte[] buf_packed, byte[] output, uint unpacked_size)
        {

            // buf = (_BYTE *)&unk_811C6780;
            //cur_output = output;
            //memset(0x811C6780, 0, 0x2000);                // buf1
            //memset(0x811C8784, 0, 0x800);                 // buf2
            //memset(0x811C8F84, 0, 0x800);                 // buf3
            var buf = new Byte[0x10000];
            Array.Clear(buf, 0, buf.Length);
            const uint buf2_off = 0x2004;
            const uint buf3_off = 0x2804;
            const uint buf4_off = 0x3004; //0x811C9784

            uint cur_addr = 2, next = 2, cur_output_addr=0;
            int first_char = buf_packed[0];
            int v8 = 0;
            int i2, v28, v29;
            byte outchar, i1 = 0;
            do
            {
                var v10 = next;
                int cur_char1 = buf_packed[cur_addr];
                var v32 =cur_addr + 1;
                ++next;
                if (cur_char1 != 0)
                {
                    if (cur_char1 >= 8)
                    {
                        byte v13 = buf_packed[cur_addr + 1];
                        byte v14 = buf_packed[cur_addr + 2];
                        if (cur_char1 >= 0xD)
                        {
                            ushort v16 = (ushort)(v13 + (v14 << 8));
                            if (cur_char1 < 0x10)
                            {
                                //*(_BYTE*)(8 * i1 + 0x811C8F88) = cur_char_1;// buf3 + 4
                                buf[buf3_off + 4 + 8 * i1] = (byte)cur_char1;
                                //*(_DWORD*)(8 * i1 + 0x811C8F84) = v16;// buf3
                                var tmp = BitConverter.GetBytes((uint)v16);
                                tmp.CopyTo(buf, buf3_off + 8 * i1);
                                //*(_BYTE*)(8 * i1 + 0x811C8F88) = v8;// bu3 + 4
                                buf[buf3_off + 4 + 8 * i1] = (byte)v8;
                                v32 = cur_addr + 3;
                                next = v10 + 3;
                                i1++;
                            }
                            else
                            {
                                byte v17 = buf_packed[cur_addr + 3];
                                //*(_BYTE*)(8 * i1 + 0x811C8F88) = cur_char_1;// buf3+4
                                buf[buf3_off + 4 + 8 * i1] = (byte)cur_char1;
                                v32 = cur_addr + 4;
                                //*(_DWORD*)(8 * i1 + 0x811C8F84) = v16 + (v17 << 16);// buf3
                                var tmp = BitConverter.GetBytes((uint)(v16 + (v17 << 16)));
                                tmp.CopyTo(buf, buf3_off + 8 * i1);
                                //*(_BYTE*)(8 * i1 + 0x811C8F89) = v8;// buf3+5
                                buf[buf3_off + 5 + 8 * i1] = (byte)cur_char1;
                                next = v10 + 4;
                                i1++;
                            }
                            
                        }
                        else
                        {
                            uint v15 = (uint)(v13 & 0xFFFF00FF | ((byte)v14 << 8));
                            buf[2 * (ushort)v15] = (byte)v8;
                            buf[2 * (ushort)v15 + 1] = (byte)cur_char1;
                            v32 = cur_addr + 3;
                            next = v10 + 3;
                        }
                    }
                    else
                    {
                        v32 = cur_addr + 2;
                        next = v10 + 2;
                        int v12 = buf_packed[cur_addr + 1];
                        buf[2 * v12] = (byte)v8;
                        buf[2 * v12 + 1] = (byte)cur_char1;
                    }
                }
                cur_addr = v32;
                ++v8;
            } while (v8 != 0x100);

            byte v18 = 0;
            ushort v19 = 0xD;
            do
            {
                i2 = 0;
                // *(_BYTE*)(v19 + 0x811C9784) = v18;         // buf4, buf2+0x1000
                buf[buf4_off + v19] = (byte)v18;
                if (i1 != 0)
                {
                    int buf3_addr = 0x2804; //buf3_addr = (_BYTE *)&dword_811C8F84;
                    do
                    {
                        if (buf[buf3_addr + 4] == v19)
                        {
                            //v22 = *((_DWORD*)buf3 + 1);
                            //*(_DWORD*)(8 * v18 + 0x811C8788) = v22;// buf2+4
                            Array.Copy(buf, buf3_addr + 1, buf, buf2_off + 4 + 8 * v18, 4);
                            //*(_DWORD*)(8 * v18 + 0x811C8784) = *(_DWORD*)buf3;// buf2
                            Array.Copy(buf, buf3_addr, buf, buf2_off + 8 * v18, 4);
                            v18++;
                        }
                        ++i2;
                        buf3_addr += 8;
                    }
                    while (i2 != i1);
                }
                v19++;
            } while (v19 != 0x18);

            buf[0x301c] = (byte)v18; //unk_811C979C = v18;
            int v35 = 0;
            Int64 v33 = 0;
            Array.Clear(buf, 0x3028, 8);//qword_811C97A8 = 0i64;
            int v23 = (int)unpacked_size;
            while (true)
            {
                //v24 = next + ((unsigned int)(v33 & 0x1F) >> 3) +((v33 >> 3) & 0xFFFFFFFC);
                int v24 = (int)(next + ((uint)(v33 & 0x1F)>>3) +((v33 >> 3) & 0xFFFFFFFC));
                v23 = (int)((v23 & 0xffffff00)  + buf_packed[v24 + 3]); // LOBYTE(v23) = buf_packed[v24 + 3];
                Int64 v25 = (Int64)((((buf_packed[v24] | (buf_packed[v24 + 1] << 8)) & 0xFF00FFFF) | (buf_packed[v24 + 2] << 16)) & 0xFFFFFF | (v23 << 24));
                ushort cur_char2 = (ushort)first_char;
                uint v27 = sub_8105E56C((uint)v25, (uint)(v25 >> 32), (uint)((v33 & 0x1F) - (v33 & 0x18)));
                if (first_char != 0xD)
                {
                    while (true)
                    {
                        //v28 = v27 & *(_DWORD*)(8 * cur_char2 + 0x8107F828);// const1
                        v28 = (int)(v27 & dword_8107F828[2 * cur_char2]);
                        //dword_811C8780 = 2 * v28 + 0x811C6780;  // buf2 - 4
                        var tmp2 = BitConverter.GetBytes(2 * v28 + 0x811C6780);
                        tmp2.CopyTo(buf, buf2_off - 4);
                        //if ( cur_char2 == *(_BYTE *)(2 * v28 + 0x811C6781) )// buf1+1
                        if (cur_char2 == buf[2*v28+1])// buf1+1
                            break;
                        cur_char2++;
                        if ((ushort)cur_char2 == 0xD)
                            goto LABEL_22;
                    }
                    //v30 = *(_BYTE*)(2 * v28 + 0x811C6780);   // buf1
                    outchar = buf[2 * v28];
                    goto LABEL_29;
                }
            LABEL_22:
                if (cur_char2 == 0x18)
                    break;
                while (true)
                {
                    //v29 = *(_BYTE*)(cur_char2 + 0x811C9784);  // buf4
                    v29 = buf[buf4_off + cur_char2];
                    //if (v29 != *(_BYTE*)(cur_char2 + 0x811C9785))// buf4+1
                    if (v29 !=  buf[buf4_off + 1 + cur_char2])
                        break;
                    LABEL_26: //goto can not jump here
                    if (++cur_char2 == 0x18)
                    {
                        outchar = buf[buf2_off + 5 + 8 * v29]; //this seems a hack...
                        goto LABEL_29;
                    }
                }
                //while (*(_DWORD*)(8 * v29 + 0x811C8784) != (v27 & *(_DWORD*)(8 * cur_char2 + 0x8107F828)))// buf2, const1
                while (BitConverter.ToUInt32(buf, (int)buf2_off + 8 * v29) !=
                       dword_8107F828[2*cur_char2])
                {
                    v29 = (ushort)(v29 + 1) & 0xffff;
                    //if ( (unsigned __int16)v29 == *(_BYTE *)(cur_char2 + 0x811C9785) )// buf4+1
                    if (v29 == buf[buf4_off + 1 + cur_char2])
                    {
                        if (++cur_char2 == 0x18)
                        {
                            outchar = buf[buf2_off + 5 + 8 * v29]; //this seems a hack...
                            goto LABEL_29;
                        }
                        while (true)
                        {
                            //v29 = *(_BYTE*)(cur_char2 + 0x811C9784);  // buf4
                            v29 = buf[buf4_off + cur_char2];
                            //if (v29 != *(_BYTE*)(cur_char2 + 0x811C9785))// buf4+1
                            if (v29 != buf[buf4_off + 1 + cur_char2])
                                break;
                            if (++cur_char2 == 0x18)
                            {
                                outchar = buf[buf2_off + 5 + 8 * v29]; //this seems a hack...
                                goto LABEL_29;
                            }
                        }
                    }
                }
                //outchar = *(_BYTE*)(8 * v29 + 0x811C8789);     // buf2+5
                outchar = buf[buf2_off + 5 + 8 * v29];
            LABEL_29:
                output[cur_output_addr] = (byte)outchar;
                //qword_811C97A8 = v33 + (unsigned __int16)cur_char2;
                var tmp = BitConverter.GetBytes((Int64)v33 + (ushort)cur_char2);
                tmp.CopyTo(buf, 0x3028);//0x3028
                v23 =v35 + 1;
                ++cur_output_addr;
                v33 += (cur_char2 & 0xffff);
                v35 = v23;
                if (v23 >= unpacked_size)
                    return;
            }
        }
        uint  sub_8105E56C(uint result, uint a2, uint a3)
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

        void UnpackW (IBinaryStream input, byte[] output)
        {
            throw new NotImplementedException();
            output = input.ReadBytes(output.Length);
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
                int v10;
                if (bit < 0x1C)
                {
                    v10 = input.ReadInt32(); //MemInt32(&src[v9]);
                }
                else
                {
                    v10 = input.ReadInt32(); //MemInt32(&src[v9 + 1]);
                    bit -= 8;
                }
                v7 += 5;
                int v13 = (v10 >> bit) & 0x1F;
                bit = v7 & 0x1F;
                int v14 = (v7 >> 3) & 0x1FFFFFFC;
                if (bit < 8)
                {
                    v10 = MemInt32(&src[v14]);
                }
                else if (bit < 0x10)
                {
                    v10 = MemInt32(&src[v14 + 1]);
                    bit -= 8;
                }
                else if (bit < 0x18)
                {
                    v10 = MemInt32(&src[v14 + 2]);
                    bit -= 16;
                }
                else
                {
                    v10 = MemInt32(&src[v14 + 3]);
                    bit -= 24;
                }
                v17 = v13 & 0xF;
                int sample = dword_455580[v17] + (((v10 >> bit) & (dword_4554E8[v17] >> shift)) << shift);
                if ((v13 & 0x10) != 0)
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
        static readonly uint[] dword_8107F828 = { //8107F828
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
    }
}
