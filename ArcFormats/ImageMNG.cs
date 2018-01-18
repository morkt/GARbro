//! \file       ImageMNG.cs
//! \date       2018 Jan 16
//! \brief      Multiple-image Network Graphics format.
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
using System.Windows.Media.Imaging;
using GameRes.Utility;

namespace GameRes.Formats
{
    internal class MngMetaData : ImageMetaData
    {
        public long PngOffset;
    }

    /// <summary>
    /// MNG may contain multiple frames, only the first one is loaded here.
    /// </summary>
    [Export(typeof(ImageFormat))]
    public class MngFormat : ImageFormat
    {
        public override string         Tag { get { return "MNG"; } }
        public override string Description { get { return "Multiple-image Network Graphics"; } }
        public override uint     Signature { get { return 0x474E4D8A; } }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (8);
            if (!header.AsciiEqual (4, "\x0D\x0A\x1A\x0A"))
                return null;
            uint chunk_size = Binary.BigEndian (file.ReadUInt32());
            var chunk_type = file.ReadBytes (4);
            if (!chunk_type.AsciiEqual ("MHDR"))
                return null;
            long chunk_pos = file.Position + chunk_size + 4;
            var info = new MngMetaData { BPP = 32 };
            info.Width   = Binary.BigEndian (file.ReadUInt32());
            info.Height  = Binary.BigEndian (file.ReadUInt32());

            for (;;)
            {
                file.Position = chunk_pos;
                chunk_size = Binary.BigEndian (file.ReadUInt32());
                file.Read (chunk_type, 0, 4);
                if (Binary.AsciiEqual (chunk_type, "MEND") || Binary.AsciiEqual (chunk_type, "IEND"))
                    break;
                if (Binary.AsciiEqual (chunk_type, "IHDR"))
                {
                    info.PngOffset = chunk_pos;
                    break;
                }
                chunk_pos += chunk_size + 12;
            }
            if (0 == info.PngOffset)
                return null;
            return info;
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (MngMetaData)info;
            var body = new StreamRegion (file.AsStream, meta.PngOffset, true);
            using (var png = new PrefixStream (PngFormat.HeaderBytes, body))
            {
                var decoder = new PngBitmapDecoder (png, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                var frame = decoder.Frames[0];
                frame.Freeze();
                return new ImageData (frame, info);
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("MngFormat.Write not implemented");
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class MngOpener : ArchiveFormat
    {
        public override string         Tag { get { return "MNG"; } }
        public override string Description { get { return "Multiple-image Network Graphics"; } }
        public override uint     Signature { get { return 0x474E4D8A; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            using (var input = file.CreateStream())
            {
                var info = MngFormat.ReadMetaData (input) as MngMetaData;
                if (null == info)
                    return null;
                var base_name = Path.GetFileNameWithoutExtension (file.Name);
                long chunk_pos = info.PngOffset;
                var chunk_type = new byte[4];
                long ihdr_pos = 0;
                var dir = new List<Entry>();
                while (chunk_pos < file.MaxOffset)
                {
                    input.Position = chunk_pos;
                    uint chunk_size = Binary.BigEndian (input.ReadUInt32());
                    input.Read (chunk_type, 0, 4);
                    if (Binary.AsciiEqual (chunk_type, "MEND"))
                        break;
                    if (Binary.AsciiEqual (chunk_type, "IHDR"))
                    {
                        ihdr_pos = chunk_pos;
                    }
                    else if (Binary.AsciiEqual (chunk_type, "IEND"))
                    {
                        if (0 == ihdr_pos) // IEND chunk without corresponding IHDR
                            return null;
                        var entry = new Entry {
                            Name = string.Format ("{0}#{1:D2}.png", base_name, dir.Count),
                            Type = "image",
                            Offset = ihdr_pos,
                            Size = (uint)(chunk_pos + chunk_size + 12 - ihdr_pos),
                        };
                        dir.Add (entry);
                        ihdr_pos = 0;
                    }
                    chunk_pos += chunk_size + 12;
                }
                if (0 == dir.Count)
                    return null;
                return new ArcFile (file, this, dir);
            }
        }

        public override IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            return new MngFrameDecoder (input);
        }

        ImageFormat MngFormat { get { return s_MngFormat.Value; } }

        static readonly ResourceInstance<ImageFormat> s_MngFormat = new ResourceInstance<ImageFormat> ("MNG");
    }

    internal sealed class MngFrameDecoder : IImageDecoder
    {
        IBinaryStream       m_input;
        ImageData           m_image;

        public Stream            Source { get { m_input.Position = 0; return m_input.AsStream; } }
        public ImageFormat SourceFormat { get { return ImageFormat.Png; } }
        public ImageMetaData       Info { get; private set; }

        public ImageData          Image {
            get {
                if (null == m_image)
                {
                    var decoder = new PngBitmapDecoder (Source, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                    var frame = decoder.Frames[0];
                    frame.Freeze();
                    m_image = new ImageData (frame, Info);
                }
                return m_image;
            }
        }

        public MngFrameDecoder (IBinaryStream input)
        {
            var png = new PrefixStream (PngFormat.HeaderBytes, input.AsStream);
            m_input = new BinaryStream (png, input.Name);
            try
            {
                Info = ImageFormat.Png.ReadMetaData (m_input);
                if (null == Info)
                    throw new InvalidFormatException();
            }
            catch
            {
                m_input.Dispose();
                throw;
            }
        }

        #region IDisposable members
        bool m_disposed = false;
        public void Dispose ()
        {
            if (!m_disposed)
            {
                m_input.Dispose();
                m_disposed = true;
            }
        }
        #endregion
    }
}
