//! \file       ArcMGS.cs
//! \date       2017 Nov 21
//! \brief      MEGU audio archive implementation.
//
// Copyright (C) 2015-2017 by morkt
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
using GameRes.Formats.Abogado;
using GameRes.Utility;

namespace GameRes.Formats.Megu
{
    internal class MgsEntry : Entry
    {
        public ushort  Channels;
        public uint    SamplesPerSecond;
        public ushort  BitsPerSample;
        public byte    Format;
    }

    [Export(typeof(ArchiveFormat))]
    public class MgsOpener : ArchiveFormat
    {
        public override string         Tag { get { return "MGS"; } }
        public override string Description { get { return "Masys audio resources archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (0, "MGS"))
                return null;
            int count = file.View.ReadInt16 (0x20);
            if (!IsSaneCount (count))
                return null;
            int flag = file.View.ReadUInt16 (3);
            var dir = new List<Entry> (count);
            int index_offset = 0x22;
            byte[] name_buf = new byte[16];
            for (int i = 0; i < count; ++i)
            {
                byte format = file.View.ReadByte (index_offset);
                int name_size = file.View.ReadByte (index_offset+9);
                if (0 == name_size)
                    return null;
                if (name_size > name_buf.Length)
                    Array.Resize (ref name_buf, name_size);
                file.View.Read (index_offset+10, name_buf, 0, (uint)name_size);
                if (100 == flag)
                    MgdOpener.Decrypt (name_buf, 0, name_size);
                var name = Encodings.cp932.GetString (name_buf, 0, name_size);
                name = Path.ChangeExtension (name, GetExtFromFormatId (format));

                var entry = FormatCatalog.Instance.Create<MgsEntry> (name);
                entry.Format = format;
                if (0 == format)
                {
                    entry.Channels = file.View.ReadUInt16 (index_offset+1);
                    entry.SamplesPerSecond = file.View.ReadUInt32 (index_offset+3);
                    entry.BitsPerSample = file.View.ReadUInt16 (index_offset+7);
                }
                index_offset += 10 + name_size;
                entry.Size = file.View.ReadUInt32 (index_offset);
                entry.Offset = file.View.ReadUInt32 (index_offset + 4);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 8;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var went = entry as MgsEntry;
            if (null == went || went.Format != 0)
                return arc.File.CreateStream (entry.Offset, entry.Size);
            var format = new WaveFormat {
                FormatTag = 1,
                Channels = (ushort)(went.Channels & 0x7FFF),
                SamplesPerSecond = went.SamplesPerSecond,
            };
            Stream pcm;
            uint pcm_size;
            if (0 != (went.Channels & 0x8000))
            {
                format.BitsPerSample = 0x10;
                var decoder = new PcmDecoder (went);
                using (var input = arc.File.CreateStream (entry.Offset, entry.Size))
                {
                    var data = decoder.Decode (input);
                    pcm = new MemoryStream (data);
                    pcm_size = (uint)data.Length;
                }
            }
            else
            {
                format.BitsPerSample = went.BitsPerSample;
                pcm = arc.File.CreateStream (entry.Offset, entry.Size);
                pcm_size = entry.Size;
            }
            using (var riff = new MemoryStream (0x2C))
            {
                ushort align = (ushort)(format.Channels * format.BitsPerSample / 8);
                format.AverageBytesPerSecond = went.SamplesPerSecond * align;
                format.BlockAlign = align;
                WaveAudio.WriteRiffHeader (riff, format, pcm_size);
                return new PrefixStream (riff.ToArray(), pcm);
            }
        }

        internal static string GetExtFromFormatId (int id)
        {
            switch (id)
            {
            case 0: return "wav";
            case 1: return "mid";
            default: return null;
            }
        }

    }

    internal sealed class PcmDecoder
    {
        int     Channels;
        int     BytesPerChunk;
        byte[]  m_output;
        int     m_dst;

        public PcmDecoder (MgsEntry went)
        {
            Channels = went.Channels & 0x7FFF;
            BytesPerChunk = went.BitsPerSample;
            int output_size;
            if (1 == Channels)
                output_size = (int)went.Size / BytesPerChunk * ((BytesPerChunk - 4) * 4 + 2);
            else
                output_size = (int)went.Size / BytesPerChunk * ((BytesPerChunk - 8) * 4 + 4);
            m_output = new byte[output_size];
        }

        void PutSample (short sample)
        {
            LittleEndian.Pack (sample, m_output, m_dst);
            m_dst += 2;
        }

        public byte[] Decode (IBinaryStream input)
        {
            m_dst = 0;
            if (1 == Channels)
            {
                var adp = new AdpDecoder();
                while (input.PeekByte() != -1)
                {
                    short sample = input.ReadInt16();
                    PutSample (sample);
                    int quant_idx = input.ReadUInt16() & 0xFF;
                    adp.Reset (sample, quant_idx);

                    for (int j = 0; j < BytesPerChunk - 4; ++j)
                    {
                        byte octet = input.ReadUInt8();
                        PutSample (adp.DecodeSample (octet));
                        PutSample (adp.DecodeSample (octet >> 4));
                    }
                }

            }
            else
            {
                var first = new AdpDecoder();
                var second = new AdpDecoder();
                int samples_per_chunk = (BytesPerChunk - 8) / 8;
                while (input.PeekByte() != -1)
                {
                    short sample = input.ReadInt16();
                    PutSample (sample);
                    int quant_idx = input.ReadUInt16() & 0xFF;
                    first.Reset (sample, quant_idx);

                    sample = input.ReadInt16();
                    PutSample (sample);
                    quant_idx = input.ReadUInt16() & 0xFF;
                    second.Reset (sample, quant_idx);

                    for (int j = 0; j < samples_per_chunk; ++j)
                    {
                        uint first_code = input.ReadUInt32();
                        uint second_code = input.ReadUInt32();
                        for (int i = 0; i < 8; ++i)
                        {
                            PutSample (first.DecodeSample ((byte)first_code));
                            PutSample (second.DecodeSample ((byte)second_code));
                            first_code >>= 4;
                            second_code >>= 4;
                        }
                    }
                }
            }
            return m_output;
        }
    }
}
