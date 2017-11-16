//! \file       ArcPCK.cs
//! \date       2017 Nov 16
//! \brief      TamamoSystem resource archive.
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
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameRes.Cryptography;
using GameRes.Formats.Properties;
using GameRes.Formats.Strings;

namespace GameRes.Formats.Tamamo
{
    [Serializable]
    public class PckScheme : ResourceScheme
    {
        public Dictionary<string, byte[]> KnownKeys;
    }

    public class PckOptions : ResourceOptions
    {
        public byte[] Key { get; set; }
    }

    internal class PckArchive : ArcFile
    {
        public readonly Blowfish Encryption;

        public PckArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, byte[] key)
            : base (arc, impl, dir)
        {
            Encryption = new Blowfish (key);
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class PckOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PCK/TAMAMO"; } }
        public override string Description { get { return "TamamoSystem resource archive"; } }
        public override uint     Signature { get { return 0x4B434150; } } // 'PACK'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (4, "_FILE001"))
                return null;
            int count = file.View.ReadInt32 (0xC);
            if (!IsSaneCount (count))
                return null;
            var key = QueryKey (file.Name);
            if (null == key)
                return null;
            uint index_length = file.View.ReadUInt32 (0x10);
            var index = file.View.ReadBytes (0x14, index_length);
            if (index.Length != index_length)
                return null;
            var bf = new Blowfish (key);
            bf.Decipher (index, index.Length);

            long data_offset = 0x14 + index_length;
            int pos = 0;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                uint size = index.ToUInt32 (pos);
                pos += 4;
                int name_end = Array.IndexOf<byte> (index, 0, pos);
                if (-1 == name_end)
                    return null;
                var name = Encodings.cp932.GetString (index, pos, name_end-pos);
                pos = name_end+1;
                uint enc_size = index.ToUInt32 (pos);
                pos += 4;
                var entry = FormatCatalog.Instance.Create<PackedEntry> (name);
                entry.Offset = data_offset;
                entry.Size = enc_size;
                entry.UnpackedSize = size;
                entry.IsPacked = enc_size != size;
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                data_offset += enc_size + 1;
            }
            return new PckArchive (file, this, dir, key);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var parc = (PckArchive)arc;
            var pent = (PackedEntry)entry;
            byte data_type = arc.File.View.ReadByte (pent.Offset);
            Stream input = arc.File.CreateStream (pent.Offset+1, pent.Size);
            if (data_type != 0)
            {
                input = new InputCryptoStream (input, parc.Encryption.CreateDecryptor());
                if (data_type != 3)
                {
                    input = new LimitStream (input, pent.UnpackedSize);
                }
                else
                {
//                    input = new Bzip2InputStream (input); // requires #ZipLib/DonNetZip
                }
            }
            return input;
        }

        public override IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            var decoder = base.OpenImage (arc, entry);
//            if (!entry.Name.StartsWith ("ev"))    // XXX
//                return decoder;
            if (1024 != decoder.Info.Width || 512 != decoder.Info.Height)
                return decoder;
            var source = decoder.Image.Bitmap;
            if (PixelFormats.Bgra32 == source.Format)
                return decoder;
            using (decoder)
            {
                var ev_bitmap = new WriteableBitmap (800, 600, ImageData.DefaultDpiX, ImageData.DefaultDpiY,
                                                     source.Format, source.Palette);
                var rect = new Int32Rect (0, 0, 800, 512);
                int buffer_size = ev_bitmap.BackBufferStride * ev_bitmap.PixelHeight;
                ev_bitmap.Lock();
                source.CopyPixels (rect, ev_bitmap.BackBuffer, buffer_size, ev_bitmap.BackBufferStride);
                ev_bitmap.AddDirtyRect (rect);
                ev_bitmap.Unlock();
                CopyRegion (source, new Int32Rect (800, 0, 88, 400), ev_bitmap, 0, 512);
                CopyRegion (source, new Int32Rect (888, 0, 88, 400), ev_bitmap, 400, 512);
                return new BitmapSourceDecoder (ev_bitmap);
            }
        }

        void CopyRegion (BitmapSource source, Int32Rect rect, WriteableBitmap target, int dst_x, int dst_y)
        {
            int bpp = source.Format.BitsPerPixel / 8;
            int width = rect.Width;
            int height = rect.Height;
            int src_stride = width * bpp;
            var src_pixels = new byte[src_stride * height];
            source.CopyPixels (rect, src_pixels, src_stride, 0);

            var transformed = new byte[src_pixels.Length];
            int dst_stride = height * bpp;
            for (int y = 0; y < height; ++y)
            {
                int src = y * src_stride;
                int dst = (width - 1) * dst_stride + y * bpp;
                for (int x = 0; x < width; ++x)
                {
                    for (int p = 0; p < bpp; ++p)
                    {
                        transformed[dst+p] = src_pixels[src+p];
                    }
                    src += bpp;
                    dst -= dst_stride;
                }
            }
            var target_rect = new Int32Rect (0, 0, height, width);
            target.WritePixels (target_rect, transformed, dst_stride, dst_x, dst_y);
        }

        PckScheme DefaultScheme = new PckScheme {
            KnownKeys = new Dictionary<string, byte[]>()
        };

        public Dictionary<string, byte[]> KnownKeys { get { return DefaultScheme.KnownKeys;  } }

        public override ResourceScheme Scheme
        {
            get { return DefaultScheme; }
            set { DefaultScheme = (PckScheme)value; }
        }

        public override object GetAccessWidget ()
        {
            return new GUI.WidgetPCK (KnownKeys.Keys.OrderBy (x => x));
        }

        public override ResourceOptions GetDefaultOptions ()
        {
            return new PckOptions {
                Key = GetKeyForTitle (Settings.Default.PCKTitle)
            };
        }

        byte[] QueryKey (string arc_name)
        {
            if (0 == KnownKeys.Count)
                return null;
            if (1 == KnownKeys.Count)
                return KnownKeys.Values.First();
            var title = FormatCatalog.Instance.LookupGame (arc_name);
            var key = GetKeyForTitle (title);
            if (key != null)
                return key;
            var options = Query<PckOptions> (arcStrings.ArcEncryptedNotice);
            return options.Key;
        }

        byte[] GetKeyForTitle (string title)
        {
            byte[] key = null;
            if (!string.IsNullOrEmpty (title))
                KnownKeys.TryGetValue (title, out key);
            return key;
        }
    }

    internal class BitmapSourceDecoder : IImageDecoder
    {
        public Stream            Source { get { return null; } }
        public ImageFormat SourceFormat { get { return null; } }
        public ImageMetaData       Info { get; private set; }
        public ImageData          Image { get; private set; }

        public BitmapSourceDecoder (BitmapSource bitmap)
        {
            Info = new ImageMetaData {
                Width = (uint)bitmap.PixelWidth,
                Height = (uint)bitmap.PixelHeight,
                BPP = bitmap.Format.BitsPerPixel,
            };
            Image = new ImageData (bitmap);
        }

        public void Dispose ()
        {
        }
    }
}
