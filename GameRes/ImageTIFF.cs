//! \file       ImageTIFF.cs
//! \date       Mon Jul 07 06:39:45 2014
//! \brief      TIFF image implementation.
//
// Copyright (C) 2014-2016 by morkt
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
using System.IO;
using System.Text;
using System.ComponentModel.Composition;
using System.Windows.Media.Imaging;
using GameRes.Utility;
using System.Collections.Generic;

namespace GameRes
{
    [Export(typeof(ImageFormat))]
    public class TifFormat : ImageFormat
    {
        public override string         Tag { get { return "TIFF"; } }
        public override string Description { get { return "Tagged Image File Format"; } }
        public override uint     Signature { get { return 0; } }
        public override bool      CanWrite { get { return true; } }

        public TifFormat ()
        {
            Extensions = new string[] { "tif", "tiff" };
            Signatures = new uint[] { 0x002a4949, 0x2a004d4d };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var decoder = new TiffBitmapDecoder (file.AsStream,
                BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];
            frame.Freeze();
            return new ImageData (frame, info);
        }

        public override void Write (Stream file, ImageData image)
        {
            var encoder = new TiffBitmapEncoder();
            encoder.Compression = TiffCompressOption.Zip;
            encoder.Frames.Add (BitmapFrame.Create (image.Bitmap, null, null, null));
            encoder.Save (file);
        }

        enum TIFF
        {
            ImageWidth      = 0x100,
            ImageHeight     = 0x101,
            BitsPerSample   = 0x102,
            Compression     = 0x103,
            SamplesPerPixel = 0x115,
            XResolution     = 0x11a,
            YResolution     = 0x11b,
            XPosition       = 0x11e,
            YPosition       = 0x11f,
        }
        enum TagType
        {
            Byte        = 1,
            Ascii       = 2,
            Short       = 3,
            Long        = 4,
            Rational    = 5,
            SByte       = 6,
            Undefined   = 7,
            SShort      = 8,
            SLong       = 9,
            SRational   = 10,
            Float       = 11,
            Double      = 12,
            LastKnown   = Double,
        }
        enum MetaParsed
        {
            None    = 0,
            Width   = 1,
            Height  = 2,
            BPP     = 4,
            PosX    = 8,
            PosY    = 16,
            Sufficient = Width|Height|BPP,
            Complete = Sufficient|PosX|PosY,
        }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            using (var file = new Parser (stream))
                return file.ReadMetaData();
        }

        internal sealed class Parser : IDisposable
        {
            private IBinaryStream   m_file;
            private readonly bool   m_is_bigendian;
            private readonly uint   m_first_ifd;
            private readonly uint[] m_type_size = { 0, 1, 1, 2, 4, 8, 1, 1, 2, 4, 8, 4, 8 };

            private delegate ushort UInt16Reader ();
            private delegate uint   UInt32Reader ();
            private delegate ulong  UInt64Reader ();

            UInt16Reader ReadUInt16;
            UInt32Reader ReadUInt32;
            UInt64Reader ReadUInt64;

            public Parser (IBinaryStream file)
            {
                m_file = file;
                m_is_bigendian = 0x2a004d4d == m_file.Signature;
                if (m_is_bigendian)
                {
                    ReadUInt16 = () => Binary.BigEndian (m_file.ReadUInt16());
                    ReadUInt32 = () => Binary.BigEndian (m_file.ReadUInt32());
                    ReadUInt64 = () => Binary.BigEndian (m_file.ReadUInt64());
                }
                else
                {
                    ReadUInt16 = () => m_file.ReadUInt16();
                    ReadUInt32 = () => m_file.ReadUInt32();
                    ReadUInt64 = () => m_file.ReadUInt64();
                }
                m_first_ifd = file.ReadHeader (8).ToUInt32 (4);
            }

            public long FindLastIFD ()
            {
                uint ifd = m_first_ifd;
                for (;;)
                {
                    m_file.Position = ifd;
                    uint tag_count = ReadUInt16();
                    ifd += 2 + tag_count*12;
                    uint ifd_next = ReadUInt32();
                    if (0 == ifd_next)
                        break;
                    if (ifd_next == ifd || ifd_next >= m_file.Length)
                        return -1;
                    ifd = ifd_next;
                }
                return ifd;
            }

            public ImageMetaData ReadMetaData ()
            {
                MetaParsed parsed = MetaParsed.None;
                int width = 0, height = 0, bpp = 0, pos_x = 0, pos_y = 0;
                var seen_ifd = new HashSet<uint>();
                uint ifd = m_first_ifd;
                while (ifd != 0 && parsed != MetaParsed.Complete && !seen_ifd.Contains (ifd))
                {
                    m_file.Position = ifd;
                    seen_ifd.Add (ifd);
                    uint tag_count = ReadUInt16();
                    ifd += 2;
                    for (uint i = 0; i < tag_count && parsed != MetaParsed.Complete; ++i)
                    {
                        ushort tag = ReadUInt16();
                        TagType type = (TagType)ReadUInt16();
                        uint count = ReadUInt32();
                        if (0 != count && 0 != type && type <= TagType.LastKnown)
                        {
                            switch ((TIFF)tag)
                            {
                            case TIFF.ImageWidth:
                                if (1 == count)
                                    if (ReadOffsetValue (type, out width))
                                        parsed |= MetaParsed.Width;
                                break;
                            case TIFF.ImageHeight:
                                if (1 == count)
                                    if (ReadOffsetValue (type, out height))
                                        parsed |= MetaParsed.Height;
                                break;
                            case TIFF.XPosition:
                                if (1 == count)
                                    if (ReadOffsetValue (type, out pos_x))
                                        parsed |= MetaParsed.PosX;
                                break;
                            case TIFF.YPosition:
                                if (1 == count)
                                    if (ReadOffsetValue (type, out pos_y))
                                        parsed |= MetaParsed.PosY;
                                break;
                            case TIFF.BitsPerSample:
                                if (count * GetTypeSize (type) > 4)
                                {
                                    var bpp_offset = ReadUInt32();
                                    m_file.Position = bpp_offset;
                                }
                                bpp = 0;
                                for (uint b = 0; b < count; ++b)
                                {
                                    int plane = 0;
                                    ReadValue (type, out plane);
                                    bpp += plane;
                                }
                                parsed |= MetaParsed.BPP;
                                break;
                            default:
                                break;
                            }
                        }
                        ifd += 12;
                        m_file.Position = ifd;
                    }
                    ifd = ReadUInt32();
                }
                if (MetaParsed.Sufficient == (parsed & MetaParsed.Sufficient))
                    return new ImageMetaData() {
                        Width = (uint)width,
                        Height = (uint)height,
                        OffsetX = pos_x,
                        OffsetY = pos_y,
                        BPP = bpp,
                    };
                else
                    return null;
            }

            uint GetTypeSize (TagType type)
            {
                if ((int)type < m_type_size.Length)
                    return m_type_size[(int)type];
                else
                    return 0;
            }

            bool ReadOffsetValue (TagType type, out int value)
            {
                if (GetTypeSize (type) > 4)
                    m_file.Position = ReadUInt32();
                return ReadValue (type, out value);
            }

            bool ReadValue (TagType type, out int value)
            {
                switch (type)
                {
                case TagType.Undefined:
                case TagType.SByte:
                case TagType.Byte:
                    value = m_file.ReadByte();
                    break;
                default:
                case TagType.Ascii:
                    value = 0;
                    return false;
                case TagType.SShort:
                case TagType.Short:
                    value = ReadUInt16();
                    break;
                case TagType.SLong:
                case TagType.Long:
                    value = (int)ReadUInt32();
                    break;
                case TagType.Rational:
                    return ReadRational (out value);
                case TagType.SRational:
                    return ReadSRational (out value);
                case TagType.Float:
                    return ReadFloat (out value);
                case TagType.Double:
                    return ReadDouble (out value);
                }
                return true;
            }

            bool ReadRational (out int value)
            {
                uint numer = ReadUInt32();
                uint denom = ReadUInt32();
                if (1 == denom)
                    value = (int)numer;
                else if (0 == denom)
                {
                    value = 0;
                    return false;
                }
                else
                    value = (int)((double)numer / denom);
                return true;
            }

            bool ReadSRational (out int value)
            {
                int numer = (int)ReadUInt32();
                int denom = (int)ReadUInt32();
                if (1 == denom)
                    value = numer;
                else if (0 == denom)
                {
                    value = 0;
                    return false;
                }
                else
                    value = (int)((double)numer / denom);
                return true;
            }

            bool ReadFloat (out int value)
            {
                var convert_buffer = new byte[4];
                if (4 != m_file.Read (convert_buffer, 0, 4))
                {
                    value = 0;
                    return false;
                }
                if (m_is_bigendian ^ !BitConverter.IsLittleEndian)
                    Array.Reverse (convert_buffer);
                value = (int)BitConverter.ToSingle (convert_buffer, 0);
                return true;
            }

            bool ReadDouble (out int value)
            {
                var convert_buffer = new byte[8];
                if (8 != m_file.Read (convert_buffer, 0, 8))
                {
                    value = 0;
                    return false;
                }
                if (m_is_bigendian ^ !BitConverter.IsLittleEndian)
                    Array.Reverse (convert_buffer);
                long bits = BitConverter.ToInt64 (convert_buffer, 0);
                value = (int)BitConverter.Int64BitsToDouble (bits);
                return true;
            }

            #region IDisposable Members
            public void Dispose ()
            {
                GC.SuppressFinalize (this);
            }
            #endregion
        }
    }
}
