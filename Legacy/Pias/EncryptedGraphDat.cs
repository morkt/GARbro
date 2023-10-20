//! \file       EncryptedGraphDat.cs
//! \date       2023 Oct 20
//! \brief      Pias encrypted resource archive.
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

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Windows.Media;

// [000526][Pias] Ningyou no Hako

namespace GameRes.Formats.Pias
{
    internal class PiasEncryptedArchive : ArcFile
    {
        public PiasEncryptedArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir)
            : base (arc, impl, dir)
        {
        }
    }

    internal class EncryptedIndexReader : IndexReader
    {
        public EncryptedIndexReader (ArcView arc, ResourceType res) : base (arc, res)
        {
        }

        new public List<Entry> GetIndex ()
        {
            if (m_res > 0)
            {
                var text_name = VFS.ChangeFileName (m_arc.Name, "text.dat");
                if (!VFS.FileExists (text_name))
                    return null;
                IBinaryStream input = VFS.OpenBinaryStream (text_name);
                try
                {
                    if (!DatOpener.EncryptedSignatures.Contains (input.Signature))
                        return null;

                    input.Position = 4;
                    var rnd = new KeyGenerator (1);
                    rnd.Seed (input.Signature);
                    var crypto = new InputCryptoStream (input.AsStream, new PiasTransform (rnd));
                    input = new BinaryStream (crypto, text_name);

                    var reader = new TextReader (input);
                    m_dir = reader.GetResourceList ((int)m_res);
                }
                finally
                {
                    input.Dispose();
                }
            }
            IsEncrypted = ResourceType.Graphics == m_res;
            if (null == m_dir)
            {
                m_dir = new List<Entry>();
            }
            if (!IsEncrypted)
            {
                if (!FillEntries())
                    return null;
                return m_dir;
            }
            var buffer = new byte[4];
            var key = new KeyGenerator (0);
            for (int i = m_dir.Count - 1; i >= 0; --i)
            {
                var entry = m_dir[i];
                uint seed = m_arc.View.ReadUInt32 (entry.Offset);
                m_arc.View.Read (entry.Offset+4, buffer, 0, 4);
                key.Seed (seed);
                Decrypt (buffer, 0, 4, key);
                entry.Size = (buffer.ToUInt32 (0) & 0xFFFFFu) + 8u;
                entry.Name = NamesAsHexOffset ? entry.Offset.ToString ("X8") : i.ToString("D4");
                entry.Type = "image";
            }
            var known_offsets = new HashSet<long> (m_dir.Select (e => e.Offset));
            long offset = 0;
            while (offset < m_arc.MaxOffset)
            {
                uint seed = m_arc.View.ReadUInt32 (offset);
                m_arc.View.Read (offset+4, buffer, 0, 4);
                key.Seed (seed);
                Decrypt (buffer, 0, 4, key);
                uint entry_size = (buffer.ToUInt32 (0) & 0xFFFFFu) + 8u;
                if (!known_offsets.Contains (offset))
                {
                    var entry = new Entry {
                        Name = (NamesAsHexOffset ? offset.ToString ("X8") : m_dir.Count.ToString("D4")) + "_",
                        Type = "image",
                        Offset = offset,
                        Size = entry_size,
                    };
                    if (!entry.CheckPlacement (m_arc.MaxOffset))
                        return null;
                    m_dir.Add (entry);
                }
                offset += entry_size + 4;
            }
            return m_dir;
        }

        internal static void Decrypt (byte[] data, int pos, int length, KeyGenerator key)
        {
            for (int i = 0; i < length; ++i)
            {
                data[pos+i] ^= (byte)key.Next();
            }
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class EncryptedDatOpener : DatOpener
    {
        public override string         Tag => "DAT/PIAS/ENC";
        public override string Description => "Pias encrypted resource archive";
        public override uint     Signature => 0;
        public override bool      CanWrite => false;

        public EncryptedDatOpener ()
        {
            Signatures = new[] { 0x02F3A62Bu, 0u };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            var arc_name = Path.GetFileName (file.Name).ToLowerInvariant();

            ResourceType resource_type = ResourceType.Undefined;
            if ("sound.dat" == arc_name)
                resource_type = ResourceType.Sound;
            else if ("graph.dat" == arc_name)
                resource_type = ResourceType.Graphics;
            else
                return null;

            var index = new EncryptedIndexReader (file, resource_type);
            var dir = index.GetIndex();
            if (null == dir)
                return null;
            if (index.IsEncrypted)
                return new PiasEncryptedArchive (file, this, dir);
            else
                return new ArcFile (file, this, dir);
        }

        public override IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            var input = arc.OpenBinaryEntry (entry);
            return new EncryptedGraphDecoder (input);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            if (entry.Type != "audio")
                return OpenEncrypted (arc, entry);
            var format = new WaveFormat
            {
                FormatTag = 1,
                Channels = 2,
                SamplesPerSecond = 22050,
                AverageBytesPerSecond = 88200,
                BitsPerSample = 16,
                BlockAlign = 4,
            };
            return OpenAudioEntry (arc, entry, format);
        }

        public Stream OpenEncrypted (ArcFile arc, Entry entry)
        {
            uint seed = arc.File.View.ReadUInt32 (entry.Offset);
            var stream = arc.File.CreateStream (entry.Offset+4, entry.Size);
            var key = new KeyGenerator (0);
            key.Seed (seed);
            return new InputCryptoStream (stream, new PiasTransform (key));
        }
    }

    internal class KeyGenerator
    {
        int     m_type;
        uint    m_seed;

        // 0 -> graph.dat
        // 1 -> text.dat
        // 2 -> save.dat

        public KeyGenerator (int type)
        {
            m_type = type;
            m_seed = 0;
        }

        public void Seed (uint seed)
        {
            m_seed = seed;
        }

        public uint Next ()
        {
            uint y, x;
            if (0 == m_type)
            {
                x = 0xD22;
                y = 0x849;
            }
            else if (1 == m_type)
            {
                x = 0xF43;
                y = 0x356B;
            }
            else if (2 == m_type)
            {
                x = 0x292;
                y = 0x57A7;
            }
            else
            {
                x = 0;
                y = 0;
            }
            uint a = x + m_seed * y;
            uint b = 0;
            if ((a & 0x400000) != 0)
                b = 1;
            if ((a & 0x400) != 0)
                b ^= 1;
            if ((a & 1) != 0)
                b ^= 1;
            m_seed = (a >> 1) | (b != 0 ? 0x80000000u : 0u);
            return m_seed;
        }
    }

    internal sealed class PiasTransform : ByteTransform
    {
        KeyGenerator     m_key;

        public PiasTransform (KeyGenerator key)
        {
            m_key = key;
        }

        public override int TransformBlock (byte[] inputBuffer, int inputOffset, int inputCount,
                                   byte[] outputBuffer, int outputOffset)
        {
            for (int i = 0; i < inputCount; ++i)
            {
                outputBuffer[outputOffset++] = (byte)(m_key.Next() ^ inputBuffer[inputOffset+i]);
            }
            return inputCount;
        }
    }

    internal class EncryptedGraphDecoder : BinaryImageDecoder
    {
        public EncryptedGraphDecoder (IBinaryStream input) : base (input, new ImageMetaData { BPP = 16 })
        {
            m_input.ReadInt32(); // skip size
            Info.Width  = m_input.ReadUInt16() & 0x3FFu;
            Info.Height = m_input.ReadUInt16() & 0x3FFu;
        }

        protected override ImageData GetImageData ()
        {
            m_input.Position = 8;
            int width = Info.iWidth;
            int output_size = width * Info.iHeight;
            var pixels = new ushort[output_size];
            var prev = new int[8];
            int dst = 0;
            while (dst < output_size)
            {
                int count;
                ushort w = m_input.ReadUInt16();
                if ((w & 0x2000) != 0)
                {
                    count = (((w >> 1) & 0x6000 | w & 0x1000) >> 12) + 1;
                    int idx = prev[count - 1]++ % 19;
                    int off = w & 0xFFF;
                    bool step_back = (off & StepBackMask[idx]) != 0;
                    bool step_vertical = (off & StepVerticalMask[idx]) != 0;
                    int m = (off & OffsetMask0[idx]) | (off >> 1) & (OffsetMask1[idx] >> 1) | (off >> 2) & (OffsetMask2[idx] >> 2);
                    int n = 16 - width * ((m + 16) / 32);
                    int p = m + 16;
                    int hidword = p >> 31;
                    p = (p & ~0xFF) | ((hidword & 0xFF) ^ (m + 16));
                    int src = dst + n - (hidword ^ ((p - hidword) & 0x1F) - hidword);
                    count = Math.Min (count, output_size - dst);
                    if (step_vertical)
                    {
                        if (step_back)
                        {
                            for (int i = 0; i < count; ++i)
                            {
                                pixels[dst+i] = pixels[src];
                                src -= width;
                            }
                        }
                        else
                        {
                            int step = width;
                            for (int i = 0; i < count; ++i)
                            {
                                pixels[dst+i] = pixels[src];
                                src += width;
                            }
                        }
                    }
                    else if (step_back)
                    {
                        for (int i = 0; i < count; ++i)
                        {
                            pixels[dst+i] = pixels[src--];
                        }
                    }
                    else
                    {
                        for (int i = 0; i < count; ++i)
                        {
                            pixels[dst+i] = pixels[src++];
                        }
                    }
                }
                else
                {
                    pixels[dst] = (ushort)((w >> 1) & 0x6000 | w & 0x1FFF);
                    count = 1;
                }
                dst += count;
            }
            int stride = width * 2;
            return ImageData.Create (Info, PixelFormats.Bgr555, null, pixels, stride);
        }

        static readonly ushort[] OffsetMask2 = {
            0, 0x800, 0x0C00, 0x0E00, 0x800, 0x0FC0, 0, 0x0F00, 0x0FF0, 0x0FF0, 0x0C00, 0x0F00, 0x800, 0x0E00, 0x0F00, 0x0C00, 0x0C00, 0x0F80, 0,
        };
        static readonly ushort[] OffsetMask1 = {
            0, 0, 0, 0x0F0, 0x200, 0x18, 0x7C0, 0x7E, 0, 0, 0x1FE, 0x7E, 0x3E0, 0x0C0, 0x78, 0x1F0, 0, 0x30, 0x7E0,
        };
        static readonly ushort[] OffsetMask0 = {
            0x3FF, 0x1FF, 0x0FF, 7, 0x0FF, 3, 0x1F, 0, 3, 3, 0, 0, 0x0F, 0x1F, 3, 7, 0x0FF, 7, 0x0F,
        };
        static readonly ushort[] StepBackMask = {
            0x800, 0x400, 0x100, 8, 0x400, 0x20, 0x20, 0x80, 4, 8, 0x200, 1, 0x10, 0x100, 4, 0x200, 0x100, 0x40, 0x800,
        };
        static readonly ushort[] StepVerticalMask = {
            0x400, 0x200, 0x200, 0x100, 0x100, 4, 0x800, 1, 8, 4, 1, 0x80, 0x400, 0x20, 0x80, 8, 0x200, 8, 0x10,
        };
    }
}
