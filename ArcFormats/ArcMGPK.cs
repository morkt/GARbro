//! \file       ArcMGPK.cs
//! \date       Mon Nov 03 20:03:36 2014
//! \brief      MGPK archive format.
//
// Copyright (C) 2014 by morkt
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
using System.Text;

namespace GameRes.Formats.Mg
{
    [Export(typeof(ArchiveFormat))]
    public class MgpkOpener : ArchiveFormat
    {
        public override string         Tag { get { return "MGPK"; } }
        public override string Description { get { return "MG resource archive"; } }
        public override uint     Signature { get { return 0x4b50474d; } } // MGPK
        public override bool  IsHierarchic { get { return false; } }
        public override bool     CanCreate { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            uint count = file.View.ReadUInt32 (8);
            if (count > 0xffffff)
                return null;
            long cur_offset = 0x0c;
            var dir = new List<Entry> ((int)count);
            for (uint i = 0; i < count; ++i)
            {
                uint name_length = file.View.ReadByte (cur_offset);
                string name = file.View.ReadString (cur_offset+1, name_length, Encoding.UTF8);
                var entry = FormatCatalog.Instance.CreateEntry (name);
                entry.Offset = file.View.ReadUInt32 (cur_offset+0x20);
                entry.Size = file.View.ReadUInt32 (cur_offset+0x24);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                cur_offset += 0x30;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            Stream input = arc.File.CreateStream (entry.Offset, entry.Size);
            string ext = Path.GetExtension (entry.Name).ToLowerInvariant();
            if (".png" == ext)
            {
                byte[] data = new byte[entry.Size];
                input.Read (data, 0, data.Length);
                input.Dispose();
                DecryptBlock (data);
                return new MemoryStream (data, false);
            }
            else if (".txt" == ext)
            {
                byte[] data = new byte[entry.Size];
                input.Read (data, 0, data.Length);
                input.Dispose();
                DecryptBlock (data);
                return DecompressStream (data);
            }
            else
                return input;
        }

        public static readonly byte[] Key = new byte[] {
			229, 101, 186, 26, 61, 198, 127, 158, 70, 21, 137
		};

        private byte[] DecryptBlock (byte[] input)
        {
            byte[] key = (byte[])Key.Clone();
            for (int i = 0; i < input.Length; i++)
            {
                input[i] ^= key[i % key.Length];
                key[i % key.Length] += 27;
            }
            return input;
        }

        private Stream DecompressStream (byte[] input)
        {
            int num = input.Length * 2;
            byte[] src = new byte[num];
            int num2;
            for (num2 = lzf_decompress (input, ref src); num2 == 0; num2 = lzf_decompress (input, ref src))
            {
                num *= 2;
                src = new byte[num];
            }
            return new MemoryStream (src, 0, num2, false);
        }

        private static int lzf_decompress (byte[] input, ref byte[] output)
        {
            int num = input.Length;
            int num2 = output.Length;
            uint num3 = 0u;
            uint num4 = 0u;
            do
            {
                uint num5 = (uint)input[(int)((UIntPtr)(num3++))];
                if (num5 < 32u)
                {
                    num5 += 1u;
                    if ((ulong)(num4 + num5) > (ulong)((long)num2))
                    {
                        return 0;
                    }
                    do
                    {
                        output[(int)((UIntPtr)(num4++))] = input[(int)((UIntPtr)(num3++))];
                    }
                    while (--num5 != 0u);
                }
                else
                {
                    uint num6 = num5 >> 5;
                    int num7 = (int)(num4 - ((num5 & 31u) << 8) - 1u);
                    if (num6 == 7u)
                    {
                        num6 += (uint)input[(int)((UIntPtr)(num3++))];
                    }
                    num7 -= (int)input[(int)((UIntPtr)(num3++))];
                    if ((ulong)(num4 + num6 + 2u) > (ulong)((long)num2))
                    {
                        return 0;
                    }
                    if (num7 < 0)
                    {
                        return 0;
                    }
                    output[(int)((UIntPtr)(num4++))] = output[num7++];
                    output[(int)((UIntPtr)(num4++))] = output[num7++];
                    do
                    {
                        output[(int)((UIntPtr)(num4++))] = output[num7++];
                    }
                    while (--num6 != 0u);
                }
            }
            while ((ulong)num3 < (ulong)((long)num));
            return (int)num4;
        }

        private static readonly uint uint_0 = 14u;
        private static readonly uint uint_1 = 16384u;
        private static readonly uint uint_2 = 32u;
        private static readonly uint uint_3 = 8192u;
        private static readonly uint uint_4 = 264u;
        private static readonly long[] long_0 = new long[uint_1];

        private static int lzf_compress (byte[] input, ref byte[] output)
        {
            int num = input.Length;
            int num2 = output.Length;
            Array.Clear (long_0, 0, (int)uint_1);
            uint num3 = 0u;
            uint num4 = 0u;
            uint num5 = (uint)((int)input[(int)((UIntPtr)0)] << 8 | (int)input[(int)((UIntPtr)(0 + 1))]);
            int num6 = 0;
            while (true)
            {
                if ((ulong)num3 < (ulong)((long)(num - 2)))
                {
                    num5 = (num5 << 8 | (uint)input[(int)((UIntPtr)(num3 + 2u))]);
                    long num7 = (long)((ulong)((num5 ^ num5 << 5) >> (int)(24u - uint_0 - num5 * 5u) & uint_1 - 1u));
                    long num8 = long_0[(int)checked((IntPtr)num7)];
                    long_0[(int)checked((IntPtr)num7)] = (long)((ulong)num3);
                    long num9;
                    if ((num9 = (long)((ulong)num3 - (ulong)num8 - 1uL)) < (long)((ulong)uint_3) && (ulong)(num3 + 4u) < (ulong)((long)num) && num8 > 0L && input[(int)checked((IntPtr)num8)] == input[(int)((UIntPtr)num3)] && input[(int)checked((IntPtr)unchecked(num8 + 1L))] == input[(int)((UIntPtr)(num3 + 1u))] && input[(int)checked((IntPtr)unchecked(num8 + 2L))] == input[(int)((UIntPtr)(num3 + 2u))])
                    {
                        uint num10 = 2u;
                        uint num11 = (uint)(num - (int)num3 - 2);
                        num11 = ((num11 <= uint_4) ? num11 : uint_4);
                        if ((ulong)num4 + (ulong)((long)num6) + 1uL + 3uL >= (ulong)((long)num2))
                        {
                            return 0;
                        }
                        do
                        {
                            num10 += 1u;
                            if (num10 >= num11)
                            {
                                break;
                            }
                        }
                        while (input[(int)checked((IntPtr)unchecked(num8 + (long)((ulong)num10)))] == input[(int)((UIntPtr)(num3 + num10))]);
//                        IL_199:
                        if (num6 != 0)
                        {
                            output[(int)((UIntPtr)(num4++))] = (byte)(num6 - 1);
                            num6 = -num6;
                            do
                            {
                                output[(int)((UIntPtr)(num4++))] = input[(int)checked((IntPtr)unchecked((ulong)num3 + (ulong)((long)num6)))];
                            }
                            while (++num6 != 0);
                        }
                        num10 -= 2u;
                        num3 += 1u;
                        if (num10 < 7u)
                        {
                            output[(int)((UIntPtr)(num4++))] = (byte)((num9 >> 8) + (long)((ulong)((ulong)num10 << 5)));
                        }
                        else
                        {
                            output[(int)((UIntPtr)(num4++))] = (byte)((num9 >> 8) + 224L);
                            output[(int)((UIntPtr)(num4++))] = (byte)(num10 - 7u);
                        }
                        output[(int)((UIntPtr)(num4++))] = (byte)num9;
                        num3 += num10 - 1u;
                        num5 = (uint)((int)input[(int)((UIntPtr)num3)] << 8 | (int)input[(int)((UIntPtr)(num3 + 1u))]);
                        num5 = (num5 << 8 | (uint)input[(int)((UIntPtr)(num3 + 2u))]);
                        long_0[(int)((UIntPtr)((num5 ^ num5 << 5) >> (int)(24u - uint_0 - num5 * 5u) & uint_1 - 1u))] = (long)((ulong)num3);
                        num3 += 1u;
                        num5 = (num5 << 8 | (uint)input[(int)((UIntPtr)(num3 + 2u))]);
                        long_0[(int)((UIntPtr)((num5 ^ num5 << 5) >> (int)(24u - uint_0 - num5 * 5u) & uint_1 - 1u))] = (long)((ulong)num3);
                        num3 += 1u;
                        continue;
//                        goto IL_199;
                    }
                }
                else
                {
                    if ((ulong)num3 == (ulong)((long)num))
                    {
                        break;
                    }
                }
                num6++;
                num3 += 1u;
                if ((long)num6 == (long)((ulong)uint_2))
                {
                    if ((ulong)(num4 + 1u + uint_2) >= (ulong)((long)num2))
                    {
                        return 0;
                    }
                    output[(int)((UIntPtr)(num4++))] = (byte)(uint_2 - 1u);
                    num6 = -num6;
                    do
                    {
                        output[(int)((UIntPtr)(num4++))] = input[(int)checked((IntPtr)unchecked((ulong)num3 + (ulong)((long)num6)))];
                    }
                    while (++num6 != 0);
                }
            }
            if (num6 != 0)
            {
                if ((ulong)num4 + (ulong)((long)num6) + 1uL >= (ulong)((long)num2))
                {
                    return 0;
                }
                output[(int)((UIntPtr)(num4++))] = (byte)(num6 - 1);
                num6 = -num6;
                do
                {
                    output[(int)((UIntPtr)(num4++))] = input[(int)checked((IntPtr)unchecked((ulong)num3 + (ulong)((long)num6)))];
                }
                while (++num6 != 0);
            }
            return (int)num4;
        }
    }
}
