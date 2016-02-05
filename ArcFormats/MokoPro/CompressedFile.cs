//! \file       CompressedFile.cs
//! \date       Fri Feb 05 01:33:25 2016
//! \brief      Mokopro compressed resources.
//
// Copyright (C) 2016 by morkt
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
using GameRes.Compression;
using GameRes.Utility;

namespace GameRes.Formats.Mokopro
{
    internal class NNNNMetaData : ImageMetaData
    {
        public ImageMetaData    BmpInfo;
        public MokoCrypt        Input;
    }

    internal class MokoCrypt
    {
        static readonly byte[] DefaultKey = new byte[] { 1, 0x23 };

        byte[]  m_input;
        int     m_unpacked_size;

        public MokoCrypt (Stream stream)
        {
            var header = new byte[8];
            if (8 != stream.Read (header, 0, 8) || !Binary.AsciiEqual (header, 0, "NNNN"))
                throw new InvalidFormatException();
            m_unpacked_size = LittleEndian.ToInt32 (header, 4);
            if (m_unpacked_size <= 0)
                throw new InvalidFormatException();
            m_input = new byte[stream.Length-8];
            stream.Read (m_input, 0, m_input.Length);
            Decrypt (m_input, DefaultKey);
        }

        public Stream UnpackStream ()
        {
            var lzss = new LzssStream (new MemoryStream (m_input));
            lzss.Config.FrameFill = 0x20;
            return lzss;
        }

        public byte[] UnpackBytes ()
        {
            using (var mem = new MemoryStream (m_input))
            using (var lzss = new LzssReader (mem, m_input.Length, m_unpacked_size))
            {
                lzss.FrameFill = 0x20;
                lzss.Unpack();
                return lzss.Data;
            }
        }

        public static void Decrypt (byte[] input, byte[] key)
        {
            for (int i = input.Length-2; i >= 0; --i)
            {
                input[i]   ^= (byte)(key[1] ^ input[i+1]);
                input[i+1] ^= (byte)(key[0] ^ input[i]);
            }
        }
    }

    [Export(typeof(ImageFormat))]
    public class NNNNBmpFormat : BmpFormat
    {
        public override string         Tag { get { return "BMP/NNNN"; } }
        public override string Description { get { return "Mokopro compressed bitmap"; } }
        public override uint     Signature { get { return 0x4E4E4E4E; } } // 'NNNN'

        public NNNNBmpFormat ()
        {
            Extensions = new string[0];
        }

        public override ImageMetaData ReadMetaData (Stream stream)
        {
            var moko = new MokoCrypt (stream);
            using (var lzss = moko.UnpackStream())
            {
                var info = base.ReadMetaData (lzss);
                if (null == info)
                    return null;
                return new NNNNMetaData
                {
                    Width   = info.Width,
                    Height  = info.Height,
                    BPP     = info.BPP,
                    BmpInfo = info,
                    Input   = moko,
                };
            }
        }

        public override ImageData Read (Stream stream, ImageMetaData info)
        {
            var meta = (NNNNMetaData)info;
            using (var lzss = meta.Input.UnpackStream())
                return base.Read (lzss, meta.BmpInfo);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("NNNNFormat.Write not implemented");
        }
    }

    [Export(typeof(AudioFormat))]
    public class NNNNOggAudio : AudioFormat
    {
        public override string         Tag { get { return "OGG/NNNN"; } }
        public override string Description { get { return "Mokopro compressed audio"; } }
        public override uint     Signature { get { return 0x4E4E4E4E; } } // 'NNNN'

        public NNNNOggAudio ()
        {
            Extensions = new string[0];
        }

        public override SoundInput TryOpen (Stream stream)
        {
            var moko = new MokoCrypt (stream);
            var ogg = moko.UnpackBytes();
            var output = new MemoryStream (ogg);
            try
            {
                return new OggInput (output);
            }
            catch
            {
                output.Dispose();
                throw;
            }
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class NNNNOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DAT/NNNN"; } }
        public override string Description { get { return "Mokopro compressed file"; } }
        public override uint     Signature { get { return 0x4E4E4E4E; } } // 'NNNN'
        public override bool  IsHierarchic { get { return false; } }
        public override bool     CanCreate { get { return false; } }

        public NNNNOpener ()
        {
            Extensions = new string[] { "dat" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            var name = Path.GetFileName (file.Name);
            var dir = new List<Entry> (1);
            var entry = FormatCatalog.Instance.Create<PackedEntry> (name);
            entry.Offset = 0;
            entry.Size = (uint)file.MaxOffset;
            entry.UnpackedSize = file.View.ReadUInt32 (4);
            dir.Add (entry);
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            var moko = new MokoCrypt (input);
            return moko.UnpackStream();
        }
    }
}
