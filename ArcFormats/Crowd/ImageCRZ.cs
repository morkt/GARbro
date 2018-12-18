//! \file       ImageCRZ.cs
//! \date       Tue Mar 28 09:49:56 2017
//! \brief      Crowd encrypted image format.
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
using System.Windows.Media;
using GameRes.Compression;
using GameRes.Utility;

namespace GameRes.Formats.Crowd
{
    internal class CrzMetaData : ImageMetaData
    {
        public int  HeaderSize;
    }

    [Serializable]
    public class CrzScheme : ResourceScheme
    {
        public IDictionary<string, byte[]> KnownKeys;
    }

    [Export(typeof(ImageFormat))]
    public class CrzFormat : ImageFormat
    {
        public override string         Tag { get { return "CRZ"; } }
        public override string Description { get { return "Crowd encrypted image format"; } }
        public override uint     Signature { get { return 0x44445A53; } } // 'SZDD'

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            file.Position = 0xE;
            using (var lz = OpenCrzStream (file))
            {
                int max_header_length = KnownKeys.Keys.Max (x => x.Length);
                var header = new byte[max_header_length + 0x35];
                lz.Read (header, 0, header.Length);
                var id = Binary.GetCString (header, 0, max_header_length);
                byte[] key;
                if (!KnownKeys.TryGetValue (id, out key))
                    return null;
                int seed_pos = id.Length + 1;
                int header_pos = seed_pos + 0x10;

                for (int i = 0; i < 0x24; ++i)
                    header[header_pos+i] ^= (byte)(key[i] ^ header[seed_pos + (i & 0xF)]);

                return new CrzMetaData {
                    Width = header.ToUInt32 (header_pos+4),
                    Height = header.ToUInt32 (header_pos+0x10),
                    BPP = 16,
                    HeaderSize = header.ToInt32 (header_pos+0x18) + 0x34 + seed_pos
                };
            }
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            file.Position = 0xE;
            using (var lz = OpenCrzStream (file))
            {
                var meta = (CrzMetaData)info;
                var header = new byte[meta.HeaderSize];
                lz.Read (header, 0, header.Length);
                int stride = info.iWidth * 2;
                var pixels = new byte[stride * info.iHeight];
                if (pixels.Length != lz.Read (pixels, 0, pixels.Length))
                    throw new InvalidFormatException();
                return ImageData.Create (info, PixelFormats.Bgr555, null, pixels, stride);
            }
        }

        internal Stream OpenCrzStream (IBinaryStream file)
        {
            var lz = new LzssStream (file.AsStream, LzssMode.Decompress, true);
            lz.Config.FrameSize = 0x1000;
            lz.Config.FrameFill = 0x20;
            lz.Config.FrameInitPos = 0x1000 - 0x10;
            return lz;
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("CrzFormat.Write not implemented");
        }

        public static byte[] GenerateKey (string secret)
        {
            var key = new byte[0x24];
            for (int i = 0; i < key.Length; ++i)
            {
                key[i] = (byte)(i * i);
            }
            int k = 0;
            for (int i = 0; i < secret.Length; ++i)
            {
                int t = secret[i];
                int n;
                switch (t & 7)
                {
                case 0:
                    key[k] = (byte)~t;
                    break;
                case 1:
                    key[k] <<= 1;
                    break;
                case 2:
                    key[k] *= key[k];
                    break;
                case 3:
                    key[k] = (byte)~(key[k] << 1);
                    break;
                case 4:
                    key[k] = (byte)((key[k] + 0x32) << 1);
                    break;
                case 5:
                    n = ~key[k];
                    key[k] = (byte)n;
                    k += n & 3;
                    break;
                case 6:
                    n = key[k] >> 1;
                    key[k] = (byte)n;
                    k -= n & 3;
                    break;
                case 7:
                    n = key[k] << 1;;
                    key[k] = (byte)n;
                    k += n & 7;
                    break;
                }
                ++k;
                if (k >= key.Length)
                    k = 0;
            }
            return key;
        }

        CrzScheme DefaultScheme = new CrzScheme { KnownKeys = new Dictionary<string, byte[]>() };

        public override ResourceScheme Scheme
        {
            get { return DefaultScheme; }
            set { DefaultScheme = (CrzScheme)value; }
        }

        IDictionary<string, byte[]> KnownKeys { get { return DefaultScheme.KnownKeys; } }
    }
}
