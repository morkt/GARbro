//! \file       ArcPACK.cs
//! \date       2018 Feb 03
//! \brief      Lune Adv Game resource archive.
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

using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using GameRes.Utility;

// [001110][Lune] Wasurekaketa Kokoro no Kakera

namespace GameRes.Formats.Lune
{
#if DEBUG
    [Export(typeof(ArchiveFormat))]
#endif
    public class PackOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PACK/LUNE"; } }
        public override string Description { get { return "Lune Adv Game engine resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public PackOpener ()
        {
            Extensions = new string[] { "dat", "wda" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            uint first_offset = file.View.ReadUInt32 (0);
            if (first_offset <= 8 || first_offset >= file.MaxOffset || 0 != (first_offset & 7))
                return null;
            int count = (int)(first_offset / 8);
            if (!IsSaneCount (count))
                return null;
            var base_name = Path.GetFileNameWithoutExtension (file.Name);
            string type = file.Name.HasExtension (".wda") ? "audio"
                        : file.Name.HasExtension (".scr") ? "script"
                        : "image";
            uint index_offset = 0;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var entry = new Entry {
                    Name = string.Format ("{0}#{1:D5}", base_name, i),
                    Type = type,
                    Offset = file.View.ReadUInt32 (index_offset),
                    Size   = file.View.ReadUInt32 (index_offset+4),
                };
                if (entry.Offset < first_offset || !entry.CheckPlacement (file.MaxOffset))
                    return null;
                if (entry.Size > 0)
                    dir.Add (entry);
                index_offset += 8;
            }
            if (dir.Count == 0 || dir[dir.Count-1].Offset + dir[dir.Count-1].Size != file.MaxOffset)
                return null;
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            if (!arc.File.Name.HasExtension (".wda"))
                return base.OpenEntry (arc, entry);
            var format = new WaveFormat {
                FormatTag = 1,
                Channels = 1,
                SamplesPerSecond = 22050,
                AverageBytesPerSecond = 44100,
                BlockAlign = 2,
                BitsPerSample = 16,
            };
            byte[] wav_header;
            using (var output = new MemoryStream (0x2C))
            {
                WaveAudio.WriteRiffHeader (output, format, entry.Size);
                wav_header = output.ToArray();
            }
            var pcm_data = arc.File.CreateStream (entry.Offset, entry.Size);
            return new PrefixStream (wav_header, pcm_data);
        }

        public override IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            if (arc.File.Name.HasExtension (".msk"))
                return new PackMaskDecoder (input);
            else
                return new PackImageDecoder (input);
        }
    }

    internal class PackImageDecoder : IImageDecoder
    {
        protected IBinaryStream   m_input;
        protected byte[]          m_output;
        private   ImageData       m_image;

        public Stream            Source { get { m_input.Position = 0; return m_input.AsStream; } }
        public ImageFormat SourceFormat { get { return null; } }
        public PixelFormat       Format { get; private set; }
        public ImageMetaData       Info { get; private set; }

        public ImageData Image {
            get {
                if (null == m_image)
                {
                    Unpack();
                    m_image = ImageData.Create (Info, Format, null, m_output);
                }
                return m_image;
            }
        }   

        public PackImageDecoder (IBinaryStream input) : this (input, PixelFormats.Bgr24) { }

        protected PackImageDecoder (IBinaryStream input, PixelFormat format)
        {
            m_input = input;
            uint width = input.ReadUInt16();
            uint height = input.ReadUInt16();
            Format = format;
            Info = new ImageMetaData { Width = width, Height = height, BPP = format.BitsPerPixel };
            m_output = new byte[(int)width * (int)height * Info.BPP / 8];
        }

        protected virtual void Unpack ()
        {
            m_input.Position = 4;
            var data = ReadDataBytes();
            using (var bits = new MsbBitStream (m_input.AsStream, true))
            {
                int src = 0;
                m_output[0] = data[src++];
                for (int dst = 1; dst < m_output.Length; ++dst)
                {
                    int ctl = bits.GetBits (2);
                    byte v;
                    if (0 == ctl)
                    {
                        v = data[src++];
                    }
                    else
                    {
                        v = m_output[dst-3];
                        if (ctl == 2)
                        {
                            if (bits.GetNextBit() != 0)
                                v -= 1;
                            else
                                v += 1;
                        }
                        else if (ctl == 3)
                        {
                            ctl = bits.GetBits (2);
                            if (ctl == 2)
                            {
                                if (bits.GetNextBit() != 0)
                                    v -= 3;
                                else
                                    v += 3;
                            }
                            else if (ctl == 3)
                            {
                                ctl = bits.GetBits (2);
                                if (ctl == 2)
                                {
                                    if (bits.GetNextBit() != 0)
                                        v -= 5;
                                    else
                                        v += 5;
                                }
                                else if (ctl == 3)
                                {
                                    switch (bits.GetBits (2))
                                    {
                                    case 3:  v -= 7; break;
                                    case 2:  v += 7; break;
                                    case 1:  v -= 6; break;
                                    default: v += 6; break;
                                    }
                                }
                                else if (ctl == 1)
                                    v -= 4;
                                else
                                    v += 4;
                            }
                            else if (ctl == 1)
                                v -= 2;
                            else
                                v += 2;
                        }
                    }
                    m_output[dst] = v;
                }
            }
        }

        protected byte[] ReadDataBytes ()
        {
            uint data_pos = m_input.ReadUInt32();
            long ctl_pos = m_input.Position;
            m_input.Seek (data_pos, SeekOrigin.Current);
            var data = m_input.ReadBytes ((int)(m_input.Length - data_pos));
            if (0 == data.Length)
                throw new InvalidFormatException();
            m_input.Position = ctl_pos;
            return data;
        }

        bool m_disposed = false;
        public void Dispose ()
        {
            if (!m_disposed)
            {
                m_input.Dispose();
                m_disposed = true;
            }
        }
    }

    internal sealed class PackMaskDecoder : PackImageDecoder
    {
        public PackMaskDecoder (IBinaryStream input) : base (input, PixelFormats.Gray8) { }

        protected override void Unpack ()
        {
            m_input.Position = 4;
            var data = ReadDataBytes();
            using (var bits = new MsbBitStream (m_input.AsStream, true))
            {
                int stride = (int)Info.Width;
                int src = 0;
                int dst = 0;
                byte init_value = data[src++];
                m_output[dst++] = init_value;
                int bit_count = 0;
                int y = 0;
                int x = 1;
                while (dst < m_output.Length)
                {
                    int ctl = bits.GetBits (2);
                    if (0 == ctl)
                    {
                        int count;
                        if (bit_count > 0)
                            count = bits.GetBits (14 - bit_count);
                        else
                            count = bits.GetBits (6);
                        while (count --> 0 && dst < m_output.Length)
                        {
                            if (y == 0 || x + 1 == stride)
                                m_output[dst] = init_value;
                            else
                                m_output[dst] = m_output[dst - stride + 1];
                            ++dst;
                            if (++x == stride)
                            {
                                x = 0;
                                ++y;
                            }
                        }
                        bit_count = 0;
                        continue;
                    }
                    else if (1 == ctl)
                    {
                        bit_count += 2;
                        if (0 == x)
                            m_output[dst] = init_value;
                        else
                            m_output[dst] = m_output[dst - stride - 1];
                    }
                    else if (2 == ctl)
                    {
                        bit_count += 2;
                        m_output[dst] = data[src++];
                    }
                    else
                    {
                        bit_count += 3;
                        if (bits.GetNextBit() != 0)
                            m_output[dst] = m_output[dst - stride];
                        else
                            m_output[dst] = m_output[dst-1];
                    }
                    ++dst;
                    if (++x == stride)
                    {
                        x = 0;
                        ++y;
                    }
                    if (bit_count >= 8)
                        bit_count -= 8;
                }
            }
            const byte max = 0x20; // System.Linq.Enumerable.Max (m_output);
            if (max != 0 && max != 0xFF)
            {
                for (int i = 0; i < m_output.Length; ++i)
                    m_output[i] = (byte)(m_output[i] * 0xFF / max);
            }
        }
    }
}
