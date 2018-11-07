//! \file       ImageERI.cs
//! \date       Tue May 26 12:04:30 2015
//! \brief      Entis rasterized image format.
//
// Copyright (C) 2015 by morkt
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
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Media;
using GameRes.Utility;

namespace GameRes.Formats.Entis
{
    internal class EriMetaData : ImageMetaData
    {
        public int      StreamPos;
        public int      Version;
        public CvType   Transformation;
        public EriCode  Architecture;
        public EriType FormatType;
        public bool     VerticalFlip;
        public int      ClippedPixel;
        public EriSampling SamplingFlags;
        public ulong    QuantumizedBits;
        public ulong    AllottedBits;
        public int      BlockingDegree;
        public int      LappedBlock;
        public int      FrameTransform;
        public int      FrameDegree;
        public EriFileHeader Header;
        public string   Description;
    }

    public enum CvType
    {
        Lossless_EMI =  0x03010000,
        Lossless_ERI =  0x03020000,
        DCT_ERI      =  0x00000001,
        LOT_ERI      =  0x00000005,
        LOT_ERI_MSS  =  0x00000105,
    }

    internal class EriFileHeader
    {
        public int      Version;
        public int      ContainedFlag;
        public int      KeyFrameCount;
        public int      FrameCount;
        public int      AllFrameTime;
    }

    public enum EriCode
    {
        ArithmeticCode      = 32,
        RunlengthGamma      = -1,
        RunlengthHuffman    = -4,
        Nemesis             = -16,
    }

    [Flags]
    public enum EriType
    {
        RGB         = 0x00000001,
        Gray        = 0x00000002,
        BGR         = 0x00000003,
        YUV         = 0x00000004,
        HSB         = 0x00000006,
        RGBA        = 0x04000001,
        BGRA        = 0x04000003,
        Mask        = 0x0000FFFF,
        WithPalette = 0x01000000,
        UseClipping = 0x02000000,
        WithAlpha   = 0x04000000,
        SideBySide  = 0x10000000,
    }

    public enum EriSampling
    {
        YUV_4_4_4 = 0x00040404,
        YUV_4_2_2 = 0x00040202,
        YUV_4_1_1 = 0x00040101,
    }

    internal class EriFile : BinaryReader
    {
        internal struct Section
        {
            public AsciiString  Id;
            public long         Length;
        }

        public EriFile (Stream stream) : base (stream, Encoding.Unicode, true)
        {
        }

        public Section ReadSection ()
        {
            var section = new Section();
            section.Id = new AsciiString (8);
            if (8 != this.Read (section.Id.Value, 0, 8))
                throw new EndOfStreamException();
            section.Length = this.ReadInt64();
            return section;
        }

        public long FindSection (string name)
        {
            var id = new AsciiString (8);
            for (;;)
            {
                if (8 != this.Read (id.Value, 0, 8))
                    throw new EndOfStreamException();
                var length = this.ReadInt64();
                if (length < 0)
                    throw new EndOfStreamException();
                if (id == name)
                    return length;
                this.BaseStream.Seek (length, SeekOrigin.Current);
            }
        }
    }

    [Export(typeof(ImageFormat))]
    public class EriFormat : ImageFormat
    {
        public override string         Tag { get { return "ERI"; } }
        public override string Description { get { return "Entis rasterized image format"; } }
        public override uint     Signature { get { return 0x69746e45u; } } // 'Enti'

        public EriFormat ()
        {
            Extensions = new [] { "eri", "emi" };
            Signatures = new uint[] { 0x69746E45, 0x54534956 };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            var header = stream.ReadHeader (0x40);
            uint id = header.ToUInt32 (8);
            if (0x03000100 != id && 0x02000100 != id)
                return null;
            if (!header.AsciiEqual (0x10, "Entis Rasterized Image") &&
                !header.AsciiEqual (0x10, "Moving Entis Image") &&
                !header.AsciiEqual (0x10, "EMSAC-Image"))
                return null;
            using (var reader = new EriFile (stream.AsStream))
            {
                var section = reader.ReadSection();
                if (section.Id != "Header  " || section.Length <= 0)
                    return null;
                int header_size = (int)section.Length;
                int stream_pos = 0x50 + header_size;
                EriFileHeader file_header = null;
                EriMetaData info = null;
                string desc = null;
                while (header_size > 0x10)
                {
                    section = reader.ReadSection();
                    header_size -= 0x10;
                    if (section.Length <= 0 || section.Length > header_size)
                        break;
                    if ("FileHdr " == section.Id)
                    {
                        file_header = new EriFileHeader { Version = reader.ReadInt32() };
                        if (file_header.Version > 0x00020100)
                            throw new InvalidFormatException ("Invalid ERI file version");
                        file_header.ContainedFlag    = reader.ReadInt32();
                        file_header.KeyFrameCount    = reader.ReadInt32();
                        file_header.FrameCount       = reader.ReadInt32();
                        file_header.AllFrameTime     = reader.ReadInt32();
                    }
                    else if ("ImageInf" == section.Id)
                    {
                        int version = reader.ReadInt32();
                        if (version != 0x00020100 && version != 0x00020200)
                            return null;
                        info = new EriMetaData { StreamPos = stream_pos, Version = version };
                        info.Transformation = (CvType)reader.ReadInt32();
                        info.Architecture = (EriCode)reader.ReadInt32();
                        info.FormatType = (EriType)reader.ReadInt32();
                        int w = reader.ReadInt32();
                        int h = reader.ReadInt32();
                        info.Width  = (uint)Math.Abs (w);
                        info.Height = (uint)Math.Abs (h);
                        info.VerticalFlip = h < 0;
                        info.BPP = reader.ReadInt32();
                        info.ClippedPixel = reader.ReadInt32();
                        info.SamplingFlags = (EriSampling)reader.ReadInt32();
                        info.QuantumizedBits = reader.ReadUInt64();
                        info.AllottedBits = reader.ReadUInt64();
                        info.BlockingDegree = reader.ReadInt32();
                        info.LappedBlock = reader.ReadInt32();
                        info.FrameTransform = reader.ReadInt32();
                        info.FrameDegree = reader.ReadInt32();
                    }
                    else if ("descript" == section.Id)
                    {
                        if (0xFEFF == reader.PeekChar())
                        {
                            reader.Read();
                            var desc_chars = reader.ReadChars ((int)section.Length/2 - 1);
                            desc = new string (desc_chars);
                        }
                        else
                        {
                            var desc_chars = reader.ReadBytes ((int)section.Length);
                            desc = Encoding.UTF8.GetString (desc_chars);
                        }
                    }
                    else
                    {
                        reader.BaseStream.Seek (section.Length, SeekOrigin.Current);
                    }
                    header_size -= (int)section.Length;
                }
                if (info != null)
                {
                    if (file_header != null)
                        info.Header = file_header;
                    if (desc != null)
                        info.Description = desc;
                }
                return info;
            }
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            var reader = ReadImageData (stream, (EriMetaData)info);
            return ImageData.Create (info, reader.Format, reader.Palette, reader.Data, reader.Stride);
        }

        internal static Color[] ReadPalette (Stream input, int palette_length)
        {
            int colors = palette_length / 4;
            if (colors <= 0 || colors > 0x100)
                throw new InvalidFormatException();
            return ImageFormat.ReadColorMap (input, colors);
        }

        internal EriReader ReadImageData (IBinaryStream stream, EriMetaData meta)
        {
            stream.Position = meta.StreamPos;
            Color[] palette = null;
            using (var input = new EriFile (stream.AsStream))
            {
                for (;;) // ReadSection throws an exception in case of EOF
                {
                    var section = input.ReadSection();
                    if ("Stream  " == section.Id)
                        continue;
                    if ("ImageFrm" == section.Id)
                        break;
                    if ("Palette " == section.Id && meta.BPP <= 8 && section.Length <= 0x400)
                    {
                        palette = ReadPalette (stream.AsStream, (int)section.Length);
                        continue;
                    }
                    input.BaseStream.Seek (section.Length, SeekOrigin.Current);
                }
            }
            var reader = new EriReader (stream.AsStream, meta, palette);
            reader.DecodeImage();

            if (!string.IsNullOrEmpty (meta.Description))
            {
                var tags = ParseTagInfo (meta.Description);
                string ref_file;
                if (tags.TryGetValue ("reference-file", out ref_file))
                {
                    ref_file = ref_file.TrimEnd (null);
                    if (!string.IsNullOrEmpty (ref_file))
                    {
                        if ((meta.BPP + 7) / 8 < 3)
                            throw new InvalidFormatException();

                        ref_file = VFS.CombinePath (VFS.GetDirectoryName (meta.FileName), ref_file);
                        using (var ref_src = VFS.OpenBinaryStream (ref_file))
                        {
                            var ref_info = ReadMetaData (ref_src) as EriMetaData;
                            if (null == ref_info)
                                throw new FileNotFoundException ("Referenced image not found", ref_file);
                            ref_info.FileName = ref_file;
                            var ref_reader = ReadImageData (ref_src, ref_info);
                            reader.AddImageBuffer (ref_reader);
                        }
                    }
                }
            }
            return reader;
        }

        static readonly Regex s_TagRe = new Regex (@"^\s*#\s*(\S+)");

        Dictionary<string, string> ParseTagInfo (string desc)
        {
            var dict = new Dictionary<string, string>();
            if (string.IsNullOrEmpty (desc))
            {
                return dict;
            }
            if ('#' != desc[0])
            {
                dict["comment"] = desc;
                return dict;
            }
            var tag_value = new StringBuilder();
            using (var reader = new StringReader (desc))
            {
                string line = reader.ReadLine();
                while (null != line)
                {
                    var match = s_TagRe.Match (line);
                    if (!match.Success)
                        break;
                    string tag = match.Groups[1].Value;

                    tag_value.Clear();
                    for (;;)
                    {
                        line = reader.ReadLine();
                        if (null == line)
                            break;
                        if (line.StartsWith ("#"))
                        {
                            if (line.Length < 2 || '#' != line[1])
                                break;
                            line = line.Substring (1);
                        }
                        tag_value.AppendLine (line);
                    }
                    dict[tag] = tag_value.ToString();
                }
            }
            return dict;
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("EriFormat.Write not implemented");
        }
    }
}
