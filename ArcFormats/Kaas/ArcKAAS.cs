//! \file       ArcKAAS.cs
//! \date       Fri Apr 10 15:21:30 2015
//! \brief      KAAS engine archive format implementation.
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
using System.Windows.Media;
using GameRes.Utility;

namespace GameRes.Formats.KAAS
{
    internal interface IIndexDecryptor
    {
        void Decrypt (byte[] data, byte key);
    }

    internal class PdImageEntry : Entry
    {
        public int  Number;
    }

    [Export(typeof(ArchiveFormat))]
    public class PdOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PD/KAAS"; } }
        public override string Description { get { return "KAAS engine PD resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public PdOpener ()
        {
            Extensions = new string[] { "pd" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int index_offset = file.View.ReadByte (0);
            if (index_offset <= 2 || index_offset >= file.MaxOffset)
                return null;
            byte key = file.View.ReadByte (1);
            int count = 0xfff & file.View.ReadUInt16 (index_offset);
            if (0 == count)
                return null;
            index_offset += 16;
            var index = new byte[count*8];
            var base_name = Path.GetFileNameWithoutExtension (file.Name);
            var dir = new List<Entry> (count);
            int data_offset = index_offset + index.Length;

            foreach (var decryptor in KnownDecryptors)
            {
                if (index.Length != file.View.Read (index_offset, index, 0, (uint)(index.Length)))
                    return null;
                decryptor.Decrypt (index, key);
                try
                {
                    if (ReadIndex (index, dir, base_name, data_offset, file.MaxOffset)
                        && dir.Count > 0)
                        return new ArcFile (file, this, dir);
                }
                catch { /* ignore errors caused by wrong decrpytor */ }
                dir.Clear();
            }
            return null;
        }

        bool ReadIndex (byte[] index, List<Entry> dir, string base_name, long data_offset, long max_offset)
        {
            int index_offset = 0;
            int count = index.Length / 8;
            for (int i = 0; i < count; ++i)
            {
                uint offset = LittleEndian.ToUInt32 (index, index_offset);
                uint size   = LittleEndian.ToUInt32 (index, index_offset+4);
                if (offset < data_offset || offset >= max_offset)
                    return false;
                if (size > 0)
                {
                    var entry = new PdImageEntry {
                        Name = string.Format ("{0}#{1:D4}", base_name, i),
                        Type = "image",
                        Offset = offset,
                        Size = size,
                        Number = dir.Count
                    };
                    if (!entry.CheckPlacement (max_offset))
                        return false;
                    dir.Add (entry);
                }
                index_offset += 8;
            }
            return true;
        }

        static readonly IEnumerable<IIndexDecryptor> KnownDecryptors = new IIndexDecryptor[] {
            new DiscoveryDecryptor(),
            new OldDecryptor(),
        };

        public override IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            byte pic_method = arc.File.View.ReadByte (entry.Offset);
            if (pic_method != 1 && pic_method != 2)
                return base.OpenImage (arc, entry);
            var pent = (PdImageEntry)entry;
            byte[] baseline = null;
            var dir = arc.Dir as List<Entry>;
            // actual baseline image index is hard-coded in game script
            // we just look back at the first non-incremental image
            for (int i = pent.Number-1; i >= 0; --i)
            {
                var base_entry = dir[i];
                byte base_method = arc.File.View.ReadByte (base_entry.Offset);
                if (base_method != 1 && base_method != 2)
                {
                    PicMetaData base_info;
                    using (var base_input = arc.File.CreateStream (base_entry.Offset, base_entry.Size))
                    {
                        base_info = s_picFormat.Value.ReadMetaData (base_input) as PicMetaData;
                        if (null == base_info)
                            throw new InvalidFormatException();
                        using (var reader = new PicFormat.Reader (base_input, base_info))
                        {
                            reader.Unpack();
                            baseline = reader.Data;
                        }
                    }
                    var overlay_info = new PdOverlayMetaData {
                        Width = base_info.Width,
                        Height = base_info.Height,
                        BPP = 24,
                        Method = pic_method,
                        ChunkCount = arc.File.View.ReadInt32 (pent.Offset+4),
                    };
                    var input = arc.File.CreateStream (pent.Offset, pent.Size);
                    return new PdOverlayImage (input, overlay_info, baseline);
                }
            }
            return base.OpenImage (arc, entry); // essentialy InvalidFormatException
        }

        static readonly Lazy<ImageFormat> s_picFormat = new Lazy<ImageFormat> (() => ImageFormat.FindByTag ("PIC/KAAS"));
    }

    internal sealed class OldDecryptor : IIndexDecryptor
    {
        public void Decrypt (byte[] data, byte key)
        {
            for (int i = 0; i != data.Length; ++i)
            {
                int k = i + 14;
                int r = 9 - (k & 7) * (k + 5) * key * 0x77;
                data[i] -= (byte)r;
            }
        }
    }

    internal sealed class DiscoveryDecryptor : IIndexDecryptor
    {
        public void Decrypt (byte[] data, byte key)
        {
            for (int i = 0; i != data.Length; ++i)
            {
                int k = i + 14;
                int r = ((k * 0x6b) % (k / 2 + 1)) + key * 0x3b * (k + 11) * (k % (k + 17));
                data[i] -= (byte)r;
            }
        }
    }

    internal class PdOverlayMetaData : ImageMetaData
    {
        public int  Method;
        public int  ChunkCount;
    }

    internal class PdOverlayImage : BinaryImageDecoder
    {
        int     m_method;
        int     m_chunk_count;
        byte[]  m_baseline;

        public PdOverlayImage (IBinaryStream input, PdOverlayMetaData info, byte[] baseline) : base (input, info)
        {
            m_method = info.Method;
            m_chunk_count = info.ChunkCount;
            m_baseline = baseline;
        }

        protected override ImageData GetImageData ()
        {
            m_input.Position = 8;
            if (1 == m_method)
                ReadOverlayV1();
            else
                ReadOverlayV2();
            return ImageData.Create (Info, PixelFormats.Bgr24, null, m_baseline);
        }

        void ReadOverlayV1 ()
        {
            for (int i = 0; i < m_chunk_count; ++i)
            {
                int dst = m_input.ReadInt24() * 3;
                m_input.Read (m_baseline, dst, 3);
            }
        }

        void ReadOverlayV2 ()
        {
            for (int i = 0; i < m_chunk_count; ++i)
            {
                int code = m_input.ReadInt24();
                int dst = (code & 0x7FFFF) * 3;
                int count = ((code >> 19) + 1) * 3;
                m_input.Read (m_baseline, dst, count);
            }
        }
    }
}
