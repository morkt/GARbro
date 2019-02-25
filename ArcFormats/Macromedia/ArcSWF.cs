//! \file       ArcSWF.cs
//! \date       2018 Sep 16
//! \brief      Shockwave Flash presentation.
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
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameRes.Compression;

namespace GameRes.Formats.Macromedia
{
    internal class SwfEntry : Entry
    {
        public SwfChunk     Chunk;
    }

    internal class SwfSoundEntry : SwfEntry
    {
        public readonly List<SwfChunk>  SoundStream = new List<SwfChunk>();
    }

    [Export(typeof(ArchiveFormat))]
    public class SwfOpener : ArchiveFormat
    {
        public override string         Tag { get { return "SWF"; } }
        public override string Description { get { return "Shockwave Flash presentation"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public SwfOpener ()
        {
            Signatures = new uint[] { 0x08535743, 0x08535746, 0 };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (0, "CWS") &&
                !file.View.AsciiEqual (0, "FWS"))
                return null;
            bool is_compressed = file.View.ReadByte (0) == 'C';
            int version = file.View.ReadByte (3);
            using (var reader = new SwfReader (file.CreateStream(), version, is_compressed))
            {
                var chunks = reader.Parse();
                var base_name = Path.GetFileNameWithoutExtension (file.Name);
                var dir = chunks.Where (t => t.Length > 2 && TypeMap.ContainsKey (t.Type))
                    .Select (t => new SwfEntry {
                        Name = string.Format ("{0}#{1:D5}", base_name, t.Id),
                        Type = GetTypeFromId (t.Type),
                        Chunk = t,
                        Offset = 0,
                        Size = (uint)t.Length
                    } as Entry).ToList();
                SwfSoundEntry current_stream = null;
                foreach (var chunk in chunks.Where (t => IsSoundStream (t)))
                {
                    switch (chunk.Type)
                    {
                    case Types.SoundStreamHead:
                    case Types.SoundStreamHead2:
                        if ((chunk.Data[1] & 0x30) != 0x20) // not mp3 stream
                        {
                            current_stream = null;
                            continue;
                        }
                        current_stream = new SwfSoundEntry {
                            Name = string.Format ("{0}#{1:D5}", base_name, chunk.Id),
                            Type = "audio",
                            Chunk = chunk,
                            Offset = 0,
                        };
                        dir.Add (current_stream);
                        break;

                    case Types.SoundStreamBlock:
                        if (current_stream != null)
                        {
                            current_stream.Size += (uint)(chunk.Data.Length - 4);
                            current_stream.SoundStream.Add (chunk);
                        }
                        break;
                    }
                }
                return new ArcFile (file, this, dir);
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var swent = (SwfEntry)entry;
            Extractor extract;
            if (!ExtractMap.TryGetValue (swent.Chunk.Type, out extract))
                extract = ExtractChunk;
            return extract (swent);
        }

        static string GetTypeFromId (Types type_id)
        {
            string type;
            if (TypeMap.TryGetValue (type_id, out type))
                return type;
            return type_id.ToString();
        }

        static Stream ExtractChunk (SwfEntry entry)
        {
            return new BinMemoryStream (entry.Chunk.Data);
        }

        static Stream ExtractChunkContents (SwfEntry entry)
        {
            var source = entry.Chunk;
            return new BinMemoryStream (source.Data, 2, source.Length-2);
        }

        static Stream ExtractSoundStream (SwfEntry entry)
        {
            var swe = (SwfSoundEntry)entry;
            var output = new MemoryStream ((int)swe.Size);
            foreach (var chunk in swe.SoundStream)
                output.Write (chunk.Data, 4, chunk.Data.Length-4);
            output.Position = 0;
            return output;
        }

        static Stream ExtractAudio (SwfEntry entry)
        {
            var chunk = entry.Chunk;
            int flags = chunk.Data[2];
            int format = flags >> 4;
            if (2 == format)
                return new BinMemoryStream (chunk.Data, 9, chunk.Length-9);
            int sample_rate = (flags >> 2) & 3;
            int bits_per_sample = (flags & 2) != 0 ? 16 : 8;
            int channels = (flags & 1) + 1;

            return new BinMemoryStream (chunk.Data, 2, chunk.Length-2);
        }

        public override IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            var swent = (SwfEntry)entry;
            switch (swent.Chunk.Type)
            {
            case Types.DefineBitsLossless:
            case Types.DefineBitsLossless2:
                return new LosslessImageDecoder (swent.Chunk);

            case Types.DefineBitsJpeg2:
                return new SwfJpeg2Decoder (swent.Chunk);

            case Types.DefineBitsJpeg3:
                return new SwfJpeg3Decoder (swent.Chunk);

            case Types.DefineBitsJpeg:
                return OpenBitsJpeg (swent.Chunk);

            default:
                return base.OpenImage (arc, entry);
            }
        }

        IImageDecoder OpenBitsJpeg (SwfChunk chunk)
        {
            int jpeg_pos = 0;
            for (int i = 0; i < chunk.Data.Length - 2; ++i)
            {
                if (chunk.Data[i] == 0xFF && chunk.Data[i+1] == 0xD8)
                {
                    jpeg_pos = i;
                    break;
                }
            }
            var input = new BinMemoryStream (chunk.Data, jpeg_pos, chunk.Data.Length - jpeg_pos);
            return ImageFormatDecoder.Create (input);
        }

        delegate Stream Extractor (SwfEntry entry);

        static Dictionary<Types, Extractor> ExtractMap = new Dictionary<Types, Extractor> {
//            { Types.DoAction,            ExtractChunkContents },
            { Types.DefineBitsJpeg,      ExtractChunkContents },
            { Types.DefineBitsLossless,  ExtractChunk },
            { Types.DefineBitsLossless2, ExtractChunk },
            { Types.DefineSound,         ExtractAudio },
            { Types.SoundStreamHead,     ExtractSoundStream },
            { Types.SoundStreamHead2,    ExtractSoundStream },
        };

        static Dictionary<Types, string> TypeMap = new Dictionary<Types, string> {
            { Types.DefineBitsJpeg,         "image" },
            { Types.DefineBitsJpeg2,        "image" },
            { Types.DefineBitsJpeg3,        "image" },
            { Types.DefineBitsLossless,     "image" },
            { Types.DefineBitsLossless2,    "image" },
            { Types.DefineSound,            "audio" },
            { Types.DoAction,               "" },

            { Types.JpegTables,             "JpegTables" },
            /*
            { Types.DefineText,             "Text" },
            { Types.DefineText2,            "Text2" },
            { Types.DefineVideoStream,      "VideoStream" },
            { Types.VideoFrame,             "VideoFrame" },
            */
        };

        internal static bool IsSoundStream (SwfChunk chunk)
        {
            return chunk.Type == Types.SoundStreamHead
                || chunk.Type == Types.SoundStreamHead2
                || chunk.Type == Types.SoundStreamBlock;
        }
    }

    internal enum Types : short
    {
        End                 = 0,
        ShowFrame           = 1,
        DefineShape         = 2,
        DefineBitsJpeg      = 6,
        JpegTables          = 8,
        DefineText          = 11,
        DoAction            = 12,
        DefineSound         = 14,
        SoundStreamHead     = 18,
        SoundStreamBlock    = 19,
        DefineBitsLossless  = 20,
        DefineBitsJpeg2     = 21,
        DefineShape2        = 22,
        DefineShape3        = 32,
        DefineText2         = 33,
        DefineBitsJpeg3     = 35,
        DefineBitsLossless2 = 36,
        DefineSprite        = 39,
        SoundStreamHead2    = 45,
        ExportAssets        = 56,
        DefineVideoStream   = 60,
        VideoFrame          = 61,
        FileAttributes      = 69,
        Font3               = 75,
        DefineBinary        = 87,
    };

    internal class SwfChunk
    {
        public Types    Type;
        public byte[]   Data;

        public int Length { get { return Data.Length; } }
        public int     Id { get { return Data.Length > 2 ? Data.ToUInt16 (0) : -1; } }

        public SwfChunk (Types id, int length)
        {
            Type = id;
            Data = length > 0 ? new byte[length] : Array.Empty<byte>();
        }
    }

    internal sealed class SwfReader : IDisposable
    {
        IBinaryStream   m_input;
        MsbBitStream    m_bits;
        int             m_version;

        Int32Rect       m_dim;

        public SwfReader (IBinaryStream input, int version, bool is_compressed)
        {
            m_input = input;
            m_version = version;
            m_input.Position = 8;
            if (is_compressed)
            {
                var zstream = new ZLibStream (input.AsStream, CompressionMode.Decompress);
                m_input = new BinaryStream (zstream, m_input.Name);
            }
            m_bits = new MsbBitStream (m_input.AsStream, true);
        }

        int     m_frame_rate;
        int     m_frame_count;

        List<SwfChunk>  m_chunks = new List<SwfChunk>();

        public List<SwfChunk> Parse ()
        {
            ReadDimensions();
            m_bits.Reset();
            m_frame_rate = m_input.ReadUInt16();
            m_frame_count = m_input.ReadUInt16();
            for (;;)
            {
                var chunk = ReadChunk();
                if (null == chunk)
                    break;
                m_chunks.Add (chunk);
            }
            return m_chunks;
        }

        void ReadDimensions ()
        {
            int rsize = m_bits.GetBits (5);
            m_dim.X = GetSignedBits (rsize);
            m_dim.Y = GetSignedBits (rsize);
            m_dim.Width  = GetSignedBits (rsize) - m_dim.X;
            m_dim.Height = GetSignedBits (rsize) - m_dim.Y;
        }

        byte[]  m_buffer = new byte[4];

        SwfChunk ReadChunk ()
        {
            if (m_input.Read (m_buffer, 0, 2) != 2)
                return null;
            int length = m_buffer.ToUInt16 (0);
            Types id = (Types)(length >> 6);
            length &= 0x3F;
            if (0x3F == length)
                length = m_input.ReadInt32();
            if (Types.DefineSprite == id)
                length = 4;
            var chunk = new SwfChunk (id, length);
            if (length > 0)
            {
                if (m_input.Read (chunk.Data, 0, length) < length)
                    return null;
            }
            return chunk;
        }

        int GetSignedBits (int count)
        {
            int v = m_bits.GetBits (count);
            if ((v >> (count - 1)) != 0)
                v |= -1 << count;
            return v;
        }

        #region IDisposable Members
        bool m_disposed = false;
        public void Dispose ()
        {
            if (!m_disposed)
            {
                m_input.Dispose();
                m_bits.Dispose();
                m_disposed = true;
            }
        }
        #endregion
    }

    internal sealed class LosslessImageDecoder : BinaryImageDecoder
    {
        Types           m_type;
        int             m_colors;
        int             m_data_pos;

        public PixelFormat       Format { get; private set; }
        private bool           HasAlpha { get { return m_type == Types.DefineBitsLossless2; } }

        public LosslessImageDecoder (SwfChunk chunk) : base (new BinMemoryStream (chunk.Data))
        {
            m_type = chunk.Type;
            byte format = chunk.Data[2];
            int bpp;
            switch (format)
            {
            case 3:
                bpp = 8; Format = PixelFormats.Indexed8;
                break;
            case 4:
                bpp = 16; Format = PixelFormats.Bgr565;
                break;
            case 5:
                bpp = 32;
                Format = HasAlpha ? PixelFormats.Bgra32 : PixelFormats.Bgr32;
                break;
            default: throw new InvalidFormatException();
            }
            uint width  = chunk.Data.ToUInt16 (3);
            uint height = chunk.Data.ToUInt16 (5);
            m_colors = 0;
            m_data_pos = 7;
            if (3 == format)
                m_colors = chunk.Data[m_data_pos++] + 1;
            Info = new ImageMetaData {
                Width = width, Height = height, BPP = bpp
            };
        }

        protected override ImageData GetImageData ()
        {
            m_input.Position = m_data_pos;
            using (var input = new ZLibStream (m_input.AsStream, CompressionMode.Decompress, true))
            {
                BitmapPalette palette = null;
                if (8 == Info.BPP)
                {
                    var pal_format = HasAlpha ? PaletteFormat.RgbA : PaletteFormat.RgbX;
                    palette = ImageFormat.ReadPalette (input, m_colors, pal_format);
                }
                var pixels = new byte[(int)Info.Width * (int)Info.Height * (Info.BPP / 8)];
                input.Read (pixels, 0, pixels.Length);
                if (32 == Info.BPP)
                {
                    for (int i = 0; i < pixels.Length; i += 4)
                    {
                        byte a = pixels[i];
                        byte r = pixels[i+1];
                        byte g = pixels[i+2];
                        byte b = pixels[i+3];
                        pixels[i]   = b;
                        pixels[i+1] = g;
                        pixels[i+2] = r;
                        pixels[i+3] = a;
                    }
                }
                return ImageData.Create (Info, Format, palette, pixels);
            }
        }
    }

    internal sealed class SwfJpeg2Decoder : IImageDecoder
    {
        byte[]          m_input;
        ImageData       m_image;

        public Stream            Source { get { return Stream.Null; } }
        public ImageFormat SourceFormat { get { return null; } }
        public ImageMetaData       Info { get; private set; }
        public ImageData          Image { get { return m_image ?? (m_image = Unpack()); } }

        public SwfJpeg2Decoder (SwfChunk chunk)
        {
            m_input = chunk.Data;
        }

        ImageData Unpack ()
        {
            int jpeg_pos = FindJpegSignature();
            if (jpeg_pos < 0)
                throw new InvalidFormatException();
            using (var jpeg = new BinMemoryStream (m_input, jpeg_pos, m_input.Length-jpeg_pos))
            {
                var decoder = new JpegBitmapDecoder (jpeg, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                var frame = decoder.Frames[0];
                Info = new ImageMetaData {
                    Width = (uint)frame.PixelWidth,
                    Height = (uint)frame.PixelHeight,
                    BPP = frame.Format.BitsPerPixel,
                };
                return new ImageData (frame, Info);
            }
        }

        int FindJpegSignature ()
        {
            int jpeg_pos = 2;
            while (jpeg_pos < m_input.Length-4)
            {
                if (m_input[jpeg_pos] != 0xFF)
                    jpeg_pos++;
                else if (m_input[jpeg_pos+1] == 0xD8)
                    return jpeg_pos;
                else if (m_input[jpeg_pos+1] != 0xD9)
                    jpeg_pos++;
                else if (m_input[jpeg_pos+2] != 0xFF)
                    jpeg_pos += 3;
                else if (m_input[jpeg_pos+3] != 0xD8)
                    jpeg_pos += 2;
                else
                    return jpeg_pos+4;
            }
            return -1;
        }

        public void Dispose ()
        {
        }
    }

    internal sealed class SwfJpeg3Decoder : IImageDecoder
    {
        byte[]          m_input;
        ImageData       m_image;
        int             m_jpeg_length;

        public Stream            Source { get { return Stream.Null; } }
        public ImageFormat SourceFormat { get { return null; } }
        public ImageMetaData       Info { get; private set; }
        public PixelFormat       Format { get; private set; }
        public ImageData          Image { get { return m_image ?? (m_image = Unpack()); } }

        public SwfJpeg3Decoder (SwfChunk chunk)
        {
            m_input = chunk.Data;
            m_jpeg_length = m_input.ToInt32 (2);
        }

        ImageData Unpack ()
        {
            BitmapSource image;
            using (var jpeg = new BinMemoryStream (m_input, 6, m_jpeg_length))
            {
                var decoder = new JpegBitmapDecoder (jpeg, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                image = decoder.Frames[0];
            }
            Info = new ImageMetaData {
                Width = (uint)image.PixelWidth,
                Height = (uint)image.PixelHeight,
                BPP = image.Format.BitsPerPixel,
            };
            byte[] alpha = new byte[image.PixelWidth * image.PixelHeight];
            using (var input = new BinMemoryStream (m_input, 6 + m_jpeg_length, m_input.Length - (6+m_jpeg_length)))
            using (var alpha_data = new ZLibStream (input, CompressionMode.Decompress))
            {
                alpha_data.Read (alpha, 0, alpha.Length);
            }
            if (image.Format.BitsPerPixel != 32)
                image = new FormatConvertedBitmap (image, PixelFormats.Bgr32, null, 0);
            int stride = image.PixelWidth * 4;
            var pixels = new byte[stride * image.PixelHeight];
            image.CopyPixels (pixels, stride, 0);
            ApplyAlpha (pixels, alpha);
            return ImageData.Create (Info, PixelFormats.Bgra32, null, pixels, stride);
        }

        void ApplyAlpha (byte[] pixels, byte[] alpha)
        {
            int src = 0;
            for (int dst = 3; dst < pixels.Length; dst += 4)
            {
                pixels[dst] = alpha[src++];
            }
        }

        public void Dispose ()
        {
        }
    }
}
