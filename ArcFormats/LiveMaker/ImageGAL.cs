//! \file       ImageGAL.cs
//! \date       Wed Jun 08 03:07:41 2016
//! \brief      LiveMaker image format.
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

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameRes.Compression;
using GameRes.Formats.Strings;
using GameRes.Utility;

namespace GameRes.Formats.LiveMaker
{
    internal class GalMetaData : ImageMetaData
    {
        public int  Version;
        public int  FrameCount;
        public bool Shuffled;
        public int  Compression;
        public uint Mask;
        public int  BlockWidth;
        public int  BlockHeight;
        public int  DataOffset;
    }

    internal class GalOptions : ResourceOptions
    {
        public uint Key;
    }

    [Serializable]
    public class GalScheme : ResourceScheme
    {
        public Dictionary<string, string> KnownKeys;
    }

    [Export(typeof(ImageFormat))]
    public class GalFormat : ImageFormat
    {
        public override string         Tag { get { return "GAL"; } }
        public override string Description { get { return "LiveMaker image format"; } }
        public override uint     Signature { get { return 0x656C6147; } } // 'Gale'

        public static Dictionary<string, string> KnownKeys = new Dictionary<string, string>();

        public override ResourceScheme Scheme
        {
            get { return new GalScheme { KnownKeys = KnownKeys }; }
            set { KnownKeys = ((GalScheme)value).KnownKeys; }
        }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            var header = new byte[0x30];
            if (11 != stream.Read (header, 0, 11))
                return null;
            int version = header[4] * 100 + header[5] * 10 + header[6] - 5328;
            if (version < 100 || version > 107)
                return null;
            int header_size = LittleEndian.ToInt32 (header, 7);
            if (header_size < 0x28 || header_size > 0x100)
                return null;
            if (header_size > header.Length)
                header = new byte[header_size];
            if (header_size != stream.Read (header, 0, header_size))
                return null;
            if (version != LittleEndian.ToInt32 (header, 0))
                return null;
            return new GalMetaData
            {
                Width   = LittleEndian.ToUInt32 (header, 4),
                Height  = LittleEndian.ToUInt32 (header, 8),
                BPP     = LittleEndian.ToInt32 (header, 0xC),
                Version = version,
                FrameCount = LittleEndian.ToInt32 (header, 0x10),
                Shuffled = header[0x15] != 0,
                Compression = header[0x16],
                Mask = LittleEndian.ToUInt32 (header, 0x18),
                BlockWidth  = LittleEndian.ToInt32 (header, 0x1C),
                BlockHeight = LittleEndian.ToInt32 (header, 0x20),
                DataOffset = header_size + 11,
            };
        }

        uint? LastKey = null;

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            var meta = (GalMetaData)info;
            uint key = 0;
            if (meta.Shuffled)
            {
                if (LastKey != null)
                    key = LastKey.Value;
                else
                    key = QueryKey();
            }
            try
            {
                using (var reader = new GalReader (stream, meta, key))
                {
                    reader.Unpack();
                    if (meta.Shuffled)
                        LastKey = key;
                    return ImageData.Create (info, reader.Format, reader.Palette, reader.Data, reader.Stride);
                }
            }
            catch
            {
                LastKey = null;
                throw;
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("GalFormat.Write not implemented");
        }

        public override ResourceOptions GetDefaultOptions ()
        {
            return new GalOptions { Key = KeyFromString (Properties.Settings.Default.GALKey) };
        }

        public override object GetAccessWidget ()
        {
            return new GUI.WidgetGAL();
        }

        internal uint QueryKey ()
        {
            if (!KnownKeys.Any())
                return 0;
            var options = Query<GalOptions> (arcStrings.ArcImageEncrypted);
            return options.Key;
        }

        public static uint KeyFromString (string key)
        {
            if (string.IsNullOrWhiteSpace (key) || key.Length < 4)
                return 0;
            return (uint)(key[0] | key[1] << 8 | key[2] << 16 | key[3] << 24);
        }
    }

    internal class GalReader : IDisposable
    {
        protected IBinaryStream   m_input;
        protected GalMetaData     m_info;
        protected byte[]          m_output;
        protected List<Frame>     m_frames;
        protected uint            m_key;

        public byte[]           Data { get { return m_output; } }
        public PixelFormat    Format { get; private set; }
        public BitmapPalette Palette { get; private set; }
        public int            Stride { get; private set; }

        public GalReader (IBinaryStream input, GalMetaData info, uint key)
        {
            m_info = info;
            if (m_info.Compression < 0 || m_info.Compression > 2)
                throw new InvalidFormatException();
            m_frames = new List<Frame> (m_info.FrameCount);
            m_key = key;
            m_input = input;
        }

        internal class Frame
        {
            public int          Width;
            public int          Height;
            public int          BPP;
            public int          Stride;
            public int          AlphaStride;
            public List<Layer>  Layers;
            public Color[]      Palette;

            public Frame (int layer_count)
            {
                Layers = new List<Layer> (layer_count);
            }

            public void SetStride ()
            {
                Stride = (Width * BPP + 7) / 8;
                AlphaStride = (Width + 3) & ~3;
                if (BPP >= 8)
                    Stride = (Stride + 3) & ~3;
            }
        }

        internal class Layer
        {
            public byte[]   Pixels;
            public byte[]   Alpha;
        }

        public void Unpack ()
        {
            m_input.Position = m_info.DataOffset;
            uint name_length = m_input.ReadUInt32();
            m_input.Seek (name_length, SeekOrigin.Current);
            uint mask = m_input.ReadUInt32();
            m_input.Seek (9, SeekOrigin.Current);
            int layer_count = m_input.ReadInt32();
            if (layer_count < 1)
                throw new InvalidFormatException();

            // XXX only first frame is interpreted.

            var frame = new Frame (layer_count);
            frame.Width  = m_input.ReadInt32();
            frame.Height = m_input.ReadInt32();
            frame.BPP    = m_input.ReadInt32();
            if (frame.BPP <= 0)
                throw new InvalidFormatException();
            if (frame.BPP <= 8)
                frame.Palette = ImageFormat.ReadColorMap (m_input.AsStream, 1 << frame.BPP);
            frame.SetStride();
            m_frames.Add (frame);
            for (int i = 0; i < layer_count; ++i)
            {
                m_input.ReadInt32();    // left
                m_input.ReadInt32();    // top
                m_input.ReadByte();     // visibility
                m_input.ReadInt32();    // (-1) TransColor
                m_input.ReadInt32();    // (0xFF) alpha
                m_input.ReadByte();     // AlphaOn
                name_length = m_input.ReadUInt32();
                m_input.Seek (name_length, SeekOrigin.Current);
                if (m_info.Version >= 107)
                    m_input.ReadByte(); // lock
                var layer = new Layer();
                int layer_size = m_input.ReadInt32();
                layer.Pixels = UnpackLayer (frame, layer_size);
                int alpha_size = m_input.ReadInt32();
                if (alpha_size != 0)
                {
                    layer.Alpha = UnpackLayer (frame, alpha_size, true);
                }
                frame.Layers.Add (layer);
            }
            Flatten (0);
        }

        protected byte[] UnpackLayer (Frame frame, int length, bool is_alpha = false)
        {
            var layer_start = m_input.Position;
            var layer_end = layer_start + length;
            var packed = new StreamRegion (m_input.AsStream, layer_start, length, true);
            try
            {
                if (0 == m_info.Compression || 2 == m_info.Compression && is_alpha)
                    return ReadZlib (frame, packed, is_alpha);
                if (2 == m_info.Compression)
                    return ReadJpeg (frame, packed);
                return ReadBlocks (frame, packed, is_alpha);
            }
            finally
            {
                packed.Dispose();
                m_input.Position = layer_end;
            }
        }

        byte[] ReadBlocks (Frame frame, Stream packed, bool is_alpha)
        {
            if (m_info.BlockWidth <= 0 || m_info.BlockHeight <= 0)
                return ReadRaw (frame, packed, is_alpha);
            int blocks_w = (frame.Width  + m_info.BlockWidth  - 1) / m_info.BlockWidth;
            int blocks_h = (frame.Height + m_info.BlockHeight - 1) / m_info.BlockHeight;
            int blocks_count = blocks_w * blocks_h;
            var data = new byte[blocks_count * 8];
            packed.Read (data, 0, data.Length);
            var refs = new int[blocks_count * 2];
            Buffer.BlockCopy (data, 0, refs, 0, data.Length);
            if (m_info.Shuffled)
                ShuffleBlocks (refs, blocks_count);

            int bpp = is_alpha ? 8 : frame.BPP;
            int stride = is_alpha ? frame.AlphaStride : frame.Stride;
            var pixels = new byte[stride * frame.Height];
            int i = 0;
            for (int y = 0; y < frame.Height; y += m_info.BlockHeight)
            {
                int height = Math.Min (m_info.BlockHeight, frame.Height - y);
                for (int x = 0; x < frame.Width; x += m_info.BlockWidth)
                {
                    int dst = y * stride + (x * bpp + 7) / 8;
                    int width = Math.Min (m_info.BlockWidth, frame.Width - x);
                    int chunk_size = (width * bpp + 7) / 8;
                    if (-1 == refs[i])
                    {
                        for (int j = 0; j < height; ++j)
                        {
                            packed.Read (pixels, dst, chunk_size);
                            dst += stride;
                        }
                    }
                    else if (-2 == refs[i])
                    {
                        int src_x = m_info.BlockWidth  * (refs[i+1] % blocks_w);
                        int src_y = m_info.BlockHeight * (refs[i+1] / blocks_w);
                        int src = src_y * stride + (src_x * bpp + 7) / 8;
                        for (int j = 0; j < height; ++j)
                        {
                            Buffer.BlockCopy (pixels, src, pixels, dst, chunk_size);
                            src += stride;
                            dst += stride;
                        }
                    }
                    else
                    {
                        int frame_ref = refs[i];
                        int layer_ref = refs[i+1];
                        if (frame_ref >= m_frames.Count || layer_ref >= m_frames[frame_ref].Layers.Count)
                            throw new InvalidFormatException();
                        var layer = m_frames[frame_ref].Layers[layer_ref];
                        byte[] src = is_alpha ? layer.Alpha : layer.Pixels;
                        for (int j = 0; j < height; ++j)
                        {
                            Buffer.BlockCopy (src, dst, pixels, dst, chunk_size);
                            dst += stride;
                        }
                    }
                    i += 2;
                }
            }
            return pixels;
        }

        byte[] ReadRaw (Frame frame, Stream packed, bool is_alpha)
        {
            int stride = is_alpha ? frame.AlphaStride : frame.Stride;
            var pixels = new byte[frame.Height * stride];
            if (m_info.Shuffled)
            {
                foreach (var dst in RandomSequence (frame.Height, m_key))
                {
                    packed.Read (pixels, dst*stride, stride);
                }
            }
            else
            {
                packed.Read (pixels, 0, pixels.Length);
            }
            return pixels;
        }

        byte[] ReadZlib (Frame frame, Stream packed, bool is_alpha)
        {
            using (var zs = new ZLibStream (packed, CompressionMode.Decompress))
                return ReadBlocks (frame, zs, is_alpha);
        }

        byte[] ReadJpeg (Frame frame, Stream packed)
        {
            var decoder = new JpegBitmapDecoder (packed, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            var bitmap = decoder.Frames[0];
            frame.BPP = bitmap.Format.BitsPerPixel;
            int stride = bitmap.PixelWidth * bitmap.Format.BitsPerPixel / 8;
            var pixels = new byte[bitmap.PixelHeight * stride];
            bitmap.CopyPixels (pixels, stride, 0);
            frame.Stride = stride;
            return pixels;
        }

        protected void Flatten (int frame_num)
        {
            // XXX only first layer is considered.

            var frame = m_frames[frame_num];
            var layer = frame.Layers[0];
            if (null == layer.Alpha)
            {
                m_output = layer.Pixels;
                if (null != frame.Palette)
                    Palette = new BitmapPalette (frame.Palette);
                if (8 == frame.BPP)
                    Format = PixelFormats.Indexed8;
                else if (16 == frame.BPP)
                    Format = PixelFormats.Bgr565;
                else if (24 == frame.BPP)
                    Format = PixelFormats.Bgr24;
                else if (32 == frame.BPP)
                    Format = PixelFormats.Bgr32;
                else if (4 == frame.BPP)
                    Format = PixelFormats.Indexed4;
                else
                    throw new NotSupportedException();
                Stride = frame.Stride;
            }
            else
            {
                m_output = new byte[frame.Width * frame.Height * 4];
                switch (frame.BPP)
                {
                case 4:  Flatten4bpp (frame, layer); break;
                case 8:  Flatten8bpp (frame, layer); break;
                case 16: Flatten16bpp (frame, layer); break;
                case 24: Flatten24bpp (frame, layer); break;
                case 32: Flatten32bpp (frame, layer); break;
                default: throw new NotSupportedException ("Not supported color depth");
                }
                Format = PixelFormats.Bgra32;
                Stride = frame.Width * 4;
            }
        }

        void Flatten4bpp (Frame frame, Layer layer)
        {
            int dst = 0;
            int src = 0;
            int a = 0;
            for (int y = 0; y < frame.Height; ++y)
            {
                for (int x = 0; x < frame.Width; ++x)
                {
                    byte pixel = layer.Pixels[src + x/2];
                    int index = 0 == (x & 1) ? (pixel & 0xF) : (pixel >> 4);
                    var color = frame.Palette[index];
                    m_output[dst++] = color.B;
                    m_output[dst++] = color.G;
                    m_output[dst++] = color.R;
                    m_output[dst++] = layer.Alpha[a+x];
                }
                src += frame.Stride;
                a += frame.AlphaStride;
            }
        }

        void Flatten8bpp (Frame frame, Layer layer)
        {
            int dst = 0;
            int src = 0;
            int a = 0;
            for (int y = 0; y < frame.Height; ++y)
            {
                for (int x = 0; x < frame.Width; ++x)
                {
                    var color = frame.Palette[ layer.Pixels[src+x] ];
                    m_output[dst++] = color.B;
                    m_output[dst++] = color.G;
                    m_output[dst++] = color.R;
                    m_output[dst++] = layer.Alpha[a+x];
                }
                src += frame.Stride;
                a += frame.AlphaStride;
            }
        }

        void Flatten16bpp (Frame frame, Layer layer)
        {
            int src = 0;
            int dst = 0;
            int a = 0;
            for (int y = 0; y < frame.Height; ++y)
            {
                for (int x = 0; x < frame.Width; ++x)
                {
                    int pixel = LittleEndian.ToUInt16 (layer.Pixels, src + x*2);
                    m_output[dst++] = (byte)((pixel & 0x001F) * 0xFF / 0x001F);
                    m_output[dst++] = (byte)((pixel & 0x07E0) * 0xFF / 0x07E0);
                    m_output[dst++] = (byte)((pixel & 0xF800) * 0xFF / 0xF800);
                    m_output[dst++] = layer.Alpha[a+x];
                }
                src += frame.Stride;
                a += frame.AlphaStride;
            }
        }

        void Flatten24bpp (Frame frame, Layer layer)
        {
            int src = 0;
            int dst = 0;
            int a = 0;
            int gap = frame.Stride - frame.Width * 3;
            for (int y = 0; y < frame.Height; ++y)
            {
                for (int x = 0; x < frame.Width; ++x)
                {
                    m_output[dst++] = layer.Pixels[src++];
                    m_output[dst++] = layer.Pixels[src++];
                    m_output[dst++] = layer.Pixels[src++];
                    m_output[dst++] = layer.Alpha[a+x];
                }
                src += gap;
                a += frame.AlphaStride;
            }
        }

        void Flatten32bpp (Frame frame, Layer layer)
        {
            int src = 0;
            int dst = 0;
            int a = 0;
            for (int y = 0; y < frame.Height; ++y)
            {
                for (int x = 0; x < frame.Width; ++x)
                {
                    m_output[dst++] = layer.Pixels[src];
                    m_output[dst++] = layer.Pixels[src+1];
                    m_output[dst++] = layer.Pixels[src+2];
                    m_output[dst++] = layer.Alpha[a+x];
                    src += 4;
                }
                a += frame.AlphaStride;
            }
        }

        void ShuffleBlocks (int[] refs, int count)
        {
            var copy = refs.Clone() as int[];
            int src = 0;
            foreach (var index in RandomSequence (count, m_key))
            {
                refs[index*2]   = copy[src++];
                refs[index*2+1] = copy[src++];
            }
        }

        static IEnumerable<int> RandomSequence (int count, uint seed)
        {
            var tp = new TpRandom (seed);
            var order = Enumerable.Range (0, count).ToList<int>();
            for (int i = 0; i < count; ++i)
            {
                int n = (int)(tp.GetRand32() % (uint)order.Count);
                yield return order[n];
                order.RemoveAt (n);
            }
        }

        #region IDisposable Members
        bool m_disposed = false;

        public void Dispose ()
        {
            Dispose (true);
            GC.SuppressFinalize (this);
        }

        protected virtual void Dispose (bool disposing)
        {
            if (!m_disposed)
            {
                m_disposed = true;
            }
        }
        #endregion
    }
}
