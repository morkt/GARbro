//! \file       ImageGALX.cs
//! \date       2017 Dec 29
//! \brief      LiveMaker GaleX image format.
//
// Copyright (C) 2017-2018 by morkt
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
using System.ComponentModel.Composition;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Xml;
using GameRes.Compression;
using GameRes.Utility;

namespace GameRes.Formats.LiveMaker
{
    internal class GalXMetaData : GalMetaData
    {
        public XmlNode  FrameXml;
    }

    [Export(typeof(ImageFormat))]
    public class GalXFormat : ImageFormat
    {
        public override string         Tag { get { return "GAL/X200"; } }
        public override string Description { get { return "LiveMaker image format"; } }
        public override uint     Signature { get { return 0x656C6147; } } // 'Gale'
        public override bool      CanWrite { get { return false; } }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (12);
            if (!header.AsciiEqual ("GaleX200"))
                return null;
            int header_size = LittleEndian.ToInt32 (header, 8);
            using (var zheader = new StreamRegion (file.AsStream, 12, header_size, true))
            using (var xheader = new ZLibStream (zheader, CompressionMode.Decompress))
            {
                var xml = ReadXml (xheader);
                var frames = xml.DocumentElement.SelectSingleNode ("/Frames");
                var attr = frames.Attributes;
                return new GalXMetaData
                {
                    Width       = UInt32.Parse (attr["Width"].Value),
                    Height      = UInt32.Parse (attr["Height"].Value),
                    BPP         = Int32.Parse (attr["Bpp"].Value), 
                    Version     = Int32.Parse (attr["Version"].Value),
                    FrameCount  = Int32.Parse (attr["Count"].Value),
                    Shuffled    = attr["Randomized"].Value != "0",
                    Compression = Int32.Parse (attr["CompType"].Value),
                    Mask        = (uint)Int32.Parse (attr["BGColor"].Value),
                    BlockWidth  = Int32.Parse (attr["BlockWidth"].Value),
                    BlockHeight = Int32.Parse (attr["BlockHeight"].Value),
                    DataOffset  = header_size + 12,
                    FrameXml    = frames,
                };
            }
        }

        static readonly Regex FrameRe = new Regex (@"<Frame [^>]+>");

        internal XmlDocument ReadXml (Stream input)
        {
            // GaleXml contains duplicate attributes which causes LoadXml to fail.
            // this is a silly hack to prevent such failure.
            using (var reader = new StreamReader (input))
            {
                var text = reader.ReadToEnd();
                text = FrameRe.Replace (text, "<Frame>");
                var xml = new XmlDocument();
                xml.LoadXml (text);
                return xml;
            }
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            var meta = (GalXMetaData)info;
            if (meta.Shuffled)
                throw new NotImplementedException ("Encrypted GaleX images not implemented.");
            using (var reader = new GalXReader (stream, meta, 0))
            {
                reader.Unpack();
                return ImageData.Create (info, reader.Format, reader.Palette, reader.Data, reader.Stride);
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("GalXFormat.Write not implemented");
        }
    }

    internal class GalXReader : GalReader
    {
        readonly XmlNode    FrameXml;

        public GalXReader (IBinaryStream input, GalXMetaData info, uint key) : base (input, info, key)
        {
            FrameXml = info.FrameXml;
        }

        new public void Unpack ()
        {
            m_input.Position = m_info.DataOffset;
            var layers = FrameXml.SelectSingleNode ("Frame/Layers");
            var frame = GetFrameFromLayers (layers);
            m_frames.Add (frame);

            var layer_nodes = layers.SelectNodes ("Layer");
            foreach (XmlNode node in layer_nodes)
            {
                bool alpha_on = node.Attributes["AlphaOn"].Value != "0";
                int layer_size = m_input.ReadInt32();
                var layer = new Layer();
                layer.Pixels = UnpackLayer (frame, layer_size);
                if (alpha_on)
                {
                    int alpha_size = m_input.ReadInt32();
                    layer.Alpha = UnpackLayer (frame, alpha_size, true);
                }
                frame.Layers.Add (layer);
            }
            Flatten (0);
        }

        internal Frame GetFrameFromLayers (XmlNode layers)
        {
            var attr = layers.Attributes;
            int layer_count = Int32.Parse (attr["Count"].Value);
            var frame = new Frame (layer_count);
            frame.Width  = Int32.Parse (attr["Width"].Value);
            frame.Height = Int32.Parse (attr["Height"].Value);
            frame.BPP    = Int32.Parse (attr["Bpp"].Value);
            frame.SetStride();
            if (frame.BPP <= 8)
                frame.Palette = ReadColorMap (layers.SelectSingleNode ("RGB").InnerText);
            return frame;
        }

        internal static Color[] ReadColorMap (string rgb)
        {
            int colors = Math.Min (0x100, rgb.Length / 6);
            var color_map = new Color[colors];
            int pos = 0;
            for (int i = 0; i < colors; ++i)
            {
                byte r = HexToByte (rgb, pos);
                byte g = HexToByte (rgb, pos+2);
                byte b = HexToByte (rgb, pos+4);
                color_map[i] = Color.FromRgb (r, g, b);
                pos += 6;
            }
            return color_map;
        }

        internal static byte HexToByte (string hex, int pos)
        {
            int hi = "0123456789ABCDEF".IndexOf (char.ToUpper (hex[pos]));
            int lo = "0123456789ABCDEF".IndexOf (char.ToUpper (hex[pos+1]));
            return (byte)(hi << 4 | lo);
        }
    }
}
