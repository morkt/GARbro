//! \file       ImageGPS.cs
//! \date       Sat Feb 20 14:13:10 2016
//! \brief      ADVEngine compressed bitmap.
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
using GameRes.Compression;
using GameRes.Utility;

namespace GameRes.Formats.Abel
{
    internal class GpsMetaData : ImageMetaData
    {
        public byte Compression;
        public  int HeaderSize;
        public  int PackedSize;
        public  int UnpackedSize;
    }

    [Export(typeof(ImageFormat))]
    public class GpsFormat : ImageFormat
    {
        public override string         Tag { get { return "GPS"; } }
        public override string Description { get { return "ADVEngine compressed bitmap"; } }
        public override uint     Signature { get { return 0x535047; } } // 'GPS'
        public override bool      CanWrite { get { return false; } }

        public GpsFormat ()
        {
            Extensions = new string[] { "gps", "cmp" };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x29);
            var gps = new GpsMetaData();
            if (header.ToUInt32 (4) == 0xCCCCCCCC)
            {
                gps.HeaderSize  = 0x19;
                gps.Compression = 2;
                gps.UnpackedSize = header.ToInt32 (0x9);
                gps.PackedSize  = header.ToInt32 (0xD);
            }
            else
            {
                gps.HeaderSize  = 0x29;
                gps.Compression = header[0x10];
                gps.UnpackedSize = header.ToInt32 (0x11);
                gps.PackedSize  = header.ToInt32 (0x15);
                gps.Width       = header.ToUInt32 (0x19);
                gps.Height      = header.ToUInt32 (0x1D);
            }
            file.Position = gps.HeaderSize;
            // read BMP header
            using (var stream = OpenGpsStream (file, gps.Compression, 0x54))
            using (var input = BinaryStream.FromStream (stream, file.Name))
            {
                var bmp_info = Bmp.ReadMetaData (input);
                if (null == bmp_info)
                    return null;
                gps.BPP = bmp_info.BPP;
                gps.Width = bmp_info.Width;
                gps.Height = bmp_info.Height;
                return gps;
            }
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var gps = (GpsMetaData)info;
            file.Position = gps.HeaderSize;
            using (var stream = OpenGpsStream (file, gps.Compression, gps.UnpackedSize))
            using (var input = BinaryStream.FromStream (stream, file.Name))
                return Bmp.Read (input, info);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("GpsFormat.Write not implemented");
        }

        Stream OpenGpsStream (IBinaryStream input, byte compression, int unpacked_size)
        {
            if (0 == compression)
                return new StreamRegion (input.AsStream, 0x29, true);
            else if (1 == compression)
                return OpenRLEStream (input.AsStream, unpacked_size);
            else if (2 == compression)
                return new LzssStream (input.AsStream, LzssMode.Decompress, true);
            else if (3 == compression)
            {
                using (var lzss = new LzssStream (input.AsStream, LzssMode.Decompress, true))
                    return OpenRLEStream (lzss, unpacked_size);
            }
            else
                throw new InvalidFormatException();
        }

        Stream OpenRLEStream (Stream input, int output_size)
        {
            var output = new byte[output_size];
            UnpackRLE (input, output);
            return new BinMemoryStream (output, "");
        }

        void UnpackRLE (Stream input, byte[] output)
        {
            int dst = 0;
            while (dst < output.Length)
            {
                int count = Math.Min (3, output.Length-dst);
                count = input.Read (output, dst, count);
                if (count < 3)
                    break;
                count = input.ReadByte();
                if (-1 == count)
                    break;
                dst += 3;
                if (count > 1)
                {
                    count = Math.Min ((count-1) * 3, output.Length-dst);
                    Binary.CopyOverlapped (output, dst-3, dst, count);
                    dst += count;
                }
            }
        }
    }
}
