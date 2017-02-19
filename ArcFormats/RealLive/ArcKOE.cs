//! \file       ArcKOE.cs
//! \date       Sun Feb 19 14:58:05 2017
//! \brief      RealLive audio archive.
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
using System.Text;

namespace GameRes.Formats.RealLive
{
    internal class KoeEntry : PackedEntry
    {
        public ushort SampleCount;
    }

    internal class KoeArchive : ArcFile
    {
        public readonly WaveFormat Format;

        public KoeArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, WaveFormat format)
            : base (arc, impl, dir)
        {
            Format = format;
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class KoeOpener : ArchiveFormat
    {
        public override string         Tag { get { return "KOE"; } }
        public override string Description { get { return "RealLive engine audio archive"; } }
        public override uint     Signature { get { return 0x50454F4B; } } // 'KOEPAC'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (4, "AC\0"))
                return null;
            int count = file.View.ReadInt32 (0x10);
            if (!IsSaneCount (count))
                return null;
            uint data_offset = file.View.ReadUInt32 (0x14);
            uint sample_rate = file.View.ReadUInt32 (0x18);
            if (0 == sample_rate)
                sample_rate = 22050;

            var base_name = Path.GetFileNameWithoutExtension (file.Name);
            uint index_offset = 0x20;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                int id = file.View.ReadUInt16 (index_offset);
                var entry = new KoeEntry
                {
                    Name = string.Format ("{0}#{1:D4}.wav", base_name, id),
                    Type = "audio",
                    SampleCount = file.View.ReadUInt16 (index_offset+2),
                    Offset = file.View.ReadUInt32 (index_offset+4),
                    IsPacked = true,
                };
                entry.Size = entry.SampleCount * 2u;
                index_offset += 8;
                dir.Add (entry);
            }
            var format = new WaveFormat
            {
                FormatTag = 1,
                Channels = 2,
                SamplesPerSecond = sample_rate,
                AverageBytesPerSecond = 4 * sample_rate,
                BlockAlign = 4,
                BitsPerSample = 16,
            };
            return new KoeArchive (file, this, dir, format);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var kent = (KoeEntry)entry;
            var karc = (KoeArchive)arc;
            var table = new ushort[kent.SampleCount];
            uint packed_size = 0;
            var offset = kent.Offset;
            for (int i = 0; i < table.Length; ++i)
            {
                table[i] = arc.File.View.ReadUInt16 (offset);
                offset += 2;
                packed_size += table[i];
            }
            int total_size = kent.SampleCount * 0x1000;
            var wav = new MemoryStream (total_size);
            WaveAudio.WriteRiffHeader (wav, karc.Format, (uint)total_size);
            using (var output = new BinaryWriter (wav, Encoding.ASCII, true))
            using (var input = arc.File.CreateStream (offset, packed_size))
            {
                foreach (ushort chunk_length in table)
                {
                    if (0 == chunk_length)
                    {
                        output.Seek (0x1000, SeekOrigin.Current);
                    }
                    else if (0x400 == chunk_length)
                    {
                        for (int i = 0; i < 0x400; ++i)
                        {
                            ushort sample = SampleTable[input.ReadUInt8()];
                            output.Write (sample);
                            output.Write (sample);
                        }
                    }
                    else
                    {
                        byte src = 0;
                        for (int i = 0; i < 0x400; i += 2)
                        {
                            byte bits = input.ReadUInt8();
                            if (0 != ((bits + 1) & 0xF))
                            {
                                src -= AdjustTable[bits & 0xF];
                            }
                            else
                            {
                                int idx = bits >> 4;
                                bits = input.ReadUInt8();
                                idx |= (bits << 4) & 0xF0;
                                src -= AdjustTable[idx];
                            }
                            ushort sample = SampleTable[src];
                            output.Write (sample);
                            output.Write (sample);
                            bits >>= 4;
                            if (0 != ((bits + 1) & 0xF))
                                src -= AdjustTable[bits & 0xF];
                            else
                                src -= AdjustTable[input.ReadUInt8()];

                            sample = SampleTable[src];
                            output.Write (sample);
                            output.Write (sample);
                        }
                    }
                }
            }
            kent.UnpackedSize = (uint)wav.Length;
            wav.Position = 0;
            return wav;
        }

        static readonly ushort[] SampleTable = new ushort[] {
            0x8000, 0x81FF, 0x83F9, 0x85EF, 0x87E1, 0x89CF, 0x8BB9, 0x8D9F,
            0x8F81, 0x915F, 0x9339, 0x950F, 0x96E1, 0x98AF, 0x9A79, 0x9C3F,
            0x9E01, 0x9FBF, 0xA179, 0xA32F, 0xA4E1, 0xA68F, 0xA839, 0xA9DF,
            0xAB81, 0xAD1F, 0xAEB9, 0xB04F, 0xB1E1, 0xB36F, 0xB4F9, 0xB67F,
            0xB801, 0xB97F, 0xBAF9, 0xBC6F, 0xBDE1, 0xBF4F, 0xC0B9, 0xC21F,
            0xC381, 0xC4DF, 0xC639, 0xC78F, 0xC8E1, 0xCA2F, 0xCB79, 0xCCBF,
            0xCE01, 0xCF3F, 0xD079, 0xD1AF, 0xD2E1, 0xD40F, 0xD539, 0xD65F,
            0xD781, 0xD89F, 0xD9B9, 0xDACF, 0xDBE1, 0xDCEF, 0xDDF9, 0xDEFF,
            0xE001, 0xE0FF, 0xE1F9, 0xE2EF, 0xE3E1, 0xE4CF, 0xE5B9, 0xE69F,
            0xE781, 0xE85F, 0xE939, 0xEA0F, 0xEAE1, 0xEBAF, 0xEC79, 0xED3F,
            0xEE01, 0xEEBF, 0xEF79, 0xF02F, 0xF0E1, 0xF18F, 0xF239, 0xF2DF,
            0xF381, 0xF41F, 0xF4B9, 0xF54F, 0xF5E1, 0xF66F, 0xF6F9, 0xF77F,
            0xF801, 0xF87F, 0xF8F9, 0xF96F, 0xF9E1, 0xFA4F, 0xFAB9, 0xFB1F,
            0xFB81, 0xFBDF, 0xFC39, 0xFC8F, 0xFCE1, 0xFD2F, 0xFD79, 0xFDBF,
            0xFE01, 0xFE3F, 0xFE79, 0xFEAF, 0xFEE1, 0xFF0F, 0xFF39, 0xFF5F,
            0xFF81, 0xFF9F, 0xFFB9, 0xFFCF, 0xFFE1, 0xFFEF, 0xFFF9, 0xFFFF,
            0x0000, 0x0001, 0x0007, 0x0011, 0x001F, 0x0031, 0x0047, 0x0061,
            0x007F, 0x00A1, 0x00C7, 0x00F1, 0x011F, 0x0151, 0x0187, 0x01C1,
            0x01FF, 0x0241, 0x0287, 0x02D1, 0x031F, 0x0371, 0x03C7, 0x0421,
            0x047F, 0x04E1, 0x0547, 0x05B1, 0x061F, 0x0691, 0x0707, 0x0781,
            0x07FF, 0x0881, 0x0907, 0x0991, 0x0A1F, 0x0AB1, 0x0B47, 0x0BE1,
            0x0C7F, 0x0D21, 0x0DC7, 0x0E71, 0x0F1F, 0x0FD1, 0x1087, 0x1141,
            0x11FF, 0x12C1, 0x1387, 0x1451, 0x151F, 0x15F1, 0x16C7, 0x17A1,
            0x187F, 0x1961, 0x1A47, 0x1B31, 0x1C1F, 0x1D11, 0x1E07, 0x1F01,
            0x1FFF, 0x2101, 0x2207, 0x2311, 0x241F, 0x2531, 0x2647, 0x2761,
            0x287F, 0x29A1, 0x2AC7, 0x2BF1, 0x2D1F, 0x2E51, 0x2F87, 0x30C1,
            0x31FF, 0x3341, 0x3487, 0x35D1, 0x371F, 0x3871, 0x39C7, 0x3B21,
            0x3C7F, 0x3DE1, 0x3F47, 0x40B1, 0x421F, 0x4391, 0x4507, 0x4681,
            0x47FF, 0x4981, 0x4B07, 0x4C91, 0x4E1F, 0x4FB1, 0x5147, 0x52E1,
            0x547F, 0x5621, 0x57C7, 0x5971, 0x5B1F, 0x5CD1, 0x5E87, 0x6041,
            0x61FF, 0x63C1, 0x6587, 0x6751, 0x691F, 0x6AF1, 0x6CC7, 0x6EA1,
            0x707F, 0x7261, 0x7447, 0x7631, 0x781F, 0x7A11, 0x7C07, 0x7FFF
        };

        static readonly byte[] AdjustTable = new byte[] {
            0x00, 0xFF, 0x01, 0xFE, 0x02, 0xFD, 0x03, 0xFC, 0x04, 0xFB, 0x05, 0xFA, 0x06, 0xF9, 0x07, 0xF8,
            0x08, 0xF7, 0x09, 0xF6, 0x0A, 0xF5, 0x0B, 0xF4, 0x0C, 0xF3, 0x0D, 0xF2, 0x0E, 0xF1, 0x0F, 0xF0,
            0x10, 0xEF, 0x11, 0xEE, 0x12, 0xED, 0x13, 0xEC, 0x14, 0xEB, 0x15, 0xEA, 0x16, 0xE9, 0x17, 0xE8,
            0x18, 0xE7, 0x19, 0xE6, 0x1A, 0xE5, 0x1B, 0xE4, 0x1C, 0xE3, 0x1D, 0xE2, 0x1E, 0xE1, 0x1F, 0xE0,
            0x20, 0xDF, 0x21, 0xDE, 0x22, 0xDD, 0x23, 0xDC, 0x24, 0xDB, 0x25, 0xDA, 0x26, 0xD9, 0x27, 0xD8,
            0x28, 0xD7, 0x29, 0xD6, 0x2A, 0xD5, 0x2B, 0xD4, 0x2C, 0xD3, 0x2D, 0xD2, 0x2E, 0xD1, 0x2F, 0xD0,
            0x30, 0xCF, 0x31, 0xCE, 0x32, 0xCD, 0x33, 0xCC, 0x34, 0xCB, 0x35, 0xCA, 0x36, 0xC9, 0x37, 0xC8,
            0x38, 0xC7, 0x39, 0xC6, 0x3A, 0xC5, 0x3B, 0xC4, 0x3C, 0xC3, 0x3D, 0xC2, 0x3E, 0xC1, 0x3F, 0xC0,
            0x40, 0xBF, 0x41, 0xBE, 0x42, 0xBD, 0x43, 0xBC, 0x44, 0xBB, 0x45, 0xBA, 0x46, 0xB9, 0x47, 0xB8,
            0x48, 0xB7, 0x49, 0xB6, 0x4A, 0xB5, 0x4B, 0xB4, 0x4C, 0xB3, 0x4D, 0xB2, 0x4E, 0xB1, 0x4F, 0xB0,
            0x50, 0xAF, 0x51, 0xAE, 0x52, 0xAD, 0x53, 0xAC, 0x54, 0xAB, 0x55, 0xAA, 0x56, 0xA9, 0x57, 0xA8,
            0x58, 0xA7, 0x59, 0xA6, 0x5A, 0xA5, 0x5B, 0xA4, 0x5C, 0xA3, 0x5D, 0xA2, 0x5E, 0xA1, 0x5F, 0xA0,
            0x60, 0x9F, 0x61, 0x9E, 0x62, 0x9D, 0x63, 0x9C, 0x64, 0x9B, 0x65, 0x9A, 0x66, 0x99, 0x67, 0x98,
            0x68, 0x97, 0x69, 0x96, 0x6A, 0x95, 0x6B, 0x94, 0x6C, 0x93, 0x6D, 0x92, 0x6E, 0x91, 0x6F, 0x90,
            0x70, 0x8F, 0x71, 0x8E, 0x72, 0x8D, 0x73, 0x8C, 0x74, 0x8B, 0x75, 0x8A, 0x76, 0x89, 0x77, 0x88,
            0x78, 0x87, 0x79, 0x86, 0x7A, 0x85, 0x7B, 0x84, 0x7C, 0x83, 0x7D, 0x82, 0x7E, 0x81, 0x7F, 0x80
        };
    }
}
