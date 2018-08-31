//! \file       AudioWADY.cs
//! \date       Sat Mar 28 01:36:42 2015
//! \brief      Marble engine audio format.
//
// Copyright (C) 2015 by morkt
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
using System.ComponentModel.Composition;
using System.IO;
using System.Text;

namespace GameRes.Formats.Marble
{
    public class WadyInput : SoundInput
    {
        byte        MulValue;

        public override long Position
        {
            get { return Source.Position; }
            set { Source.Position = value; }
        }

        public override bool CanSeek { get { return Source.CanSeek; } }

        public override int SourceBitrate
        {
            get { return (int)Format.AverageBytesPerSecond * 8; }
        }

        public override string SourceFormat { get { return "raw"; } }

        public override long Seek (long offset, SeekOrigin origin)
        {
            return Source.Seek (offset, origin);
        }

        public override int Read (byte[] buffer, int offset, int count)
        {
            return Source.Read (buffer, offset, count);
        }

        public override int ReadByte ()
        {
            return Source.ReadByte();
        }

        public WadyInput (IBinaryStream input) : base (new MemoryStream())
        {
            input.Seek (5, SeekOrigin.Begin);
            MulValue = input.ReadUInt8();
            input.Seek (6, SeekOrigin.Current);
            int src_size = input.ReadInt32();
            input.Seek (16, SeekOrigin.Current);
            var format = new WaveFormat();
            format.FormatTag                = input.ReadUInt16();
            format.Channels                 = input.ReadUInt16();
            format.SamplesPerSecond         = input.ReadUInt32();
            format.AverageBytesPerSecond    = input.ReadUInt32();
            format.BlockAlign               = input.ReadUInt16();
            format.BitsPerSample            = input.ReadUInt16();
            format.ExtraSize                = 0;
            this.Format = format;
            int remaining = (int)(input.Length-input.Position);
            if (remaining == src_size)
            {
                (Source as MemoryStream).Capacity = src_size * 2;
                Decode (input, src_size, Source);
            }
            else
                Decode2 (input, Source);
            Source.Position = 0;
            this.PcmSize = Source.Length;
            input.Dispose();
        }

        private void Decode (IBinaryStream input, int count, Stream output)
        {
            using (var buffer = new BinaryWriter (output, Encoding.ASCII, true))
            {
                ushort sampleL = 0;
                ushort sampleR = 0;
                for (int i = 0; i < count; ++i)
                {
                    byte v = input.ReadUInt8();
                    if (0 != (v & 0x80))
                        sampleL = (ushort)(v << 9);
                    else
                        sampleL += (ushort)(MulValue * SampleTable[v]);
                    buffer.Write (sampleL);
                    if (1 != Format.Channels)
                    {
                        ++i;
                        v = input.ReadUInt8();
                        if (0 != (v & 0x80))
                            sampleR = (ushort)(v << 9);
                        else
                            sampleR += (ushort)(MulValue * SampleTable[v]);
                        buffer.Write (sampleR);
                    }
                }
            }
        }

        private void Decode2 (IBinaryStream input, Stream output)
        {
            if (1 != Format.Channels)
            {
                int channel_size = input.ReadInt32();
                Decode3 (input, output, 2);
                input.Position = 0x38 + channel_size;
                output.Position = 2;
                Decode3 (input, output, 2);
            }
            else
                Decode3 (input, output, 0);
        }

        private void Decode2Alt (IBinaryStream input, Stream output)
        {
            if (1 != Format.Channels)
            {
                int channel_size = input.ReadInt32();
                Decode3Alt (input, output, 2);
                input.Position = 0x38 + channel_size;
                output.Position = 2;
                Decode3Alt (input, output, 2);
            }
            else
                Decode3Alt (input, output, 0);
        }

        private void Decode3 (IBinaryStream input, Stream output, int step)
        {
            input.ReadInt32(); // unpacked_size
            int count = input.ReadInt32();
            using (var buffer = new BinaryWriter (output, Encoding.ASCII, true))
            {
                short sample = input.ReadInt16();
                buffer.Write (sample);
                for (int i = 0; i < count; ++i)
                {
                    if (count - 300 == i)
                        sample = 0;
                    ushort v = input.ReadUInt8();
                    if (0 != (v & 1))
                    {
                        ushort v14 = (ushort)((v >> 1) & 0x7F);
                        if (0 != (v14 & 0x40))
                            sample = (short)(v14 << 10);
                        else
                            sample += (short)SampleTable2[v14];
                        buffer.Write (sample);
                        if (step != 0)
                            buffer.BaseStream.Seek (step, SeekOrigin.Current);
                    }
                    else
                    {
                        v |= (ushort)(input.ReadUInt8() << 8);
                        int repeat = SizeTable[(v >> 1) & 7];
                        short end = (short)(v & 0xFFF0);
                        double inc = (end - sample) / (double)repeat;
                        double v8 = sample;
                        for (int j = 0; j < repeat; ++j)
                        {
                            v8 += inc;
                            buffer.Write ((short)v8);
                            if (step != 0)
                                buffer.BaseStream.Seek (step, SeekOrigin.Current);
                        }
                        sample = end;
                    }
                }
            }
        }

        private void Decode3Alt (IBinaryStream input, Stream output, int step)
        {
            int output_size = input.ReadInt32();
            int count = input.ReadInt32();
            using (var buffer = new BinaryWriter (output, Encoding.ASCII, true))
            {
                short sample = input.ReadInt16();
                buffer.Write (sample);
                int output_count = 1;
                for (int i = 0; i < count; ++i)
                {
                    short v = input.ReadInt16();
                    int repeat = SizeTableAlt[v & 7];
                    output_count += repeat;
                    short end = (short)(v & 0xFFF8);
                    short inc = (short)((end - sample) / repeat);
                    for (int j = 0; j < repeat; ++j)
                    {
                        sample += inc;
                        buffer.Write (sample);
                        if (step != 0)
                            buffer.BaseStream.Seek (step, SeekOrigin.Current);
                    }
                    sample = end;
                }
                if (step != 0)
                    output_size /= step;
                while (output_count++ < output_size)
                {
                    sample = input.ReadInt16();
                    buffer.Write (sample);
                    if (step != 0)
                        buffer.BaseStream.Seek (step, SeekOrigin.Current);
                }
            }
        }

        static readonly int[] SizeTable = new int[] { 3, 4, 5, 6, 8, 0x10, 0x20, 0x100 };
        static readonly int[] SizeTableAlt = new int[] { 2, 3, 4, 5, 6, 8, 0x10, 0x20 };

        static readonly ushort[] SampleTable = new ushort[] {
            0x0000, 0x0002, 0x0004, 0x0006, 0x0008, 0x000A, 0x000C, 0x000F,
            0x0012, 0x0015, 0x0018, 0x001C, 0x0020, 0x0024, 0x0028, 0x002C,
            0x0031, 0x0036, 0x003B, 0x0040, 0x0046, 0x004C, 0x0052, 0x0058,
            0x005F, 0x0066, 0x006D, 0x0074, 0x007C, 0x0084, 0x008C, 0x0094,
            0x00A0, 0x00AA, 0x00B4, 0x00BE, 0x00C8, 0x00D2, 0x00DC, 0x00E6,
            0x00F0, 0x00FF, 0x010E, 0x011D, 0x012C, 0x0140, 0x0154, 0x0168,
            0x017C, 0x0190, 0x01A9, 0x01C2, 0x01DB, 0x01F4, 0x020D, 0x0226,
            0x0244, 0x0262, 0x028A, 0x02BC, 0x02EE, 0x0320, 0x0384, 0x03E8,
            0x0000, 0xFFFE, 0xFFFC, 0xFFFA, 0xFFF8, 0xFFF6, 0xFFF4, 0xFFF1,
            0xFFEE, 0xFFEB, 0xFFE8, 0xFFE4, 0xFFE0, 0xFFDC, 0xFFD8, 0xFFD4,
            0xFFCF, 0xFFCA, 0xFFC5, 0xFFC0, 0xFFBA, 0xFFB4, 0xFFAE, 0xFFA8,
            0xFFA1, 0xFF9A, 0xFF93, 0xFF8C, 0xFF84, 0xFF7C, 0xFF74, 0xFF6C,
            0xFF60, 0xFF56, 0xFF4C, 0xFF42, 0xFF38, 0xFF2E, 0xFF24, 0xFF1A,
            0xFF10, 0xFF01, 0xFEF2, 0xFEE3, 0xFED4, 0xFEC0, 0xFEAC, 0xFE98,
            0xFE84, 0xFE70, 0xFE57, 0xFE3E, 0xFE25, 0xFE0C, 0xFDF3, 0xFDDA,
            0xFDBC, 0xFD9E, 0xFD76, 0xFD44, 0xFD12, 0xFCE0, 0xFC7C, 0xFC18,
        };

        static readonly ushort[] SampleTable2 = new ushort[] {
            0x0000, 0x0004, 0x0008, 0x000C, 0x0013, 0x0018, 0x001E, 0x0026,
            0x002F, 0x003B, 0x004A, 0x005C, 0x0073, 0x0090, 0x00B4, 0x00E1,
            0x0119, 0x0160, 0x01B8, 0x0226, 0x02AF, 0x035B, 0x0431, 0x053E,
            0x068E, 0x0831, 0x0A3D, 0x0CCD, 0x1000, 0x1400, 0x1900, 0x1F40,
            0x0000, 0xFFFC, 0xFFF8, 0xFFF4, 0xFFED, 0xFFE8, 0xFFE2, 0xFFDA,
            0xFFD1, 0xFFC5, 0xFFB6, 0xFFA4, 0xFF8D, 0xFF70, 0xFF4C, 0xFF1F,
            0xFEE7, 0xFEA0, 0xFE48, 0xFDDA, 0xFD51, 0xFCA5, 0xFBCF, 0xFAC2,
            0xF972, 0xF7CF, 0xF5C3, 0xF333, 0xF000, 0xEC00, 0xE700, 0xE0C0,
        };
    }

    [Export(typeof(AudioFormat))]
    public class WadyAudio : AudioFormat
    {
        public override string         Tag { get { return "WAY"; } }
        public override string Description { get { return "Marble engine wave audio format"; } }
        public override uint     Signature { get { return 0x59444157u; } } // 'WADY'

        public override SoundInput TryOpen (IBinaryStream file)
        {
            return new WadyInput (file);
        }
    }
}
