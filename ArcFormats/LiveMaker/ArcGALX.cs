//! \file       ArcGALX.cs
//! \date       2018 Feb 24
//! \brief      Multi-frame GALX image.
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
using System.Xml;

namespace GameRes.Formats.LiveMaker
{
    internal class GalXEntry : Entry
    {
        public GalXMetaData     Info;
        public XmlNode          Layers;
        public bool             AlphaOn;
    }

    [Export(typeof(ArchiveFormat))]
    public class GalXOpener : ArchiveFormat
    {
        public override string         Tag { get { return "GAL/X"; } }
        public override string Description { get { return "LiveMaker engine multi-frame image"; } }
        public override uint     Signature { get { return 0x656C6147; } } // 'GaleX200'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (4, "X200"))
                return null;
            using (var gal = file.CreateStream())
            {
                var info = GalXFormat.ReadMetaData (gal) as GalXMetaData;
                if (null == info || !IsSaneCount (info.FrameCount))
                    return null;
                var base_name = Path.GetFileNameWithoutExtension (file.Name);
                gal.Position = info.DataOffset;
                var dir = new List<Entry> (info.FrameCount);
                foreach (XmlNode node in info.FrameXml.SelectNodes ("Frame"))
                {
                    var layers = node.SelectSingleNode ("Layers");
                    var entry = new GalXEntry {
                        Name = string.Format ("{0}#{1:D4}", base_name, dir.Count),
                        Type = "image",
                        Offset = gal.Position,
                        Layers = layers,
                        Info = info,
                    };
                    var nodes = layers.SelectNodes ("Layer");
                    entry.AlphaOn = nodes.Count > 0 && nodes[0].Attributes["AlphaOn"].Value != "0";
                    foreach (XmlNode layer in nodes)
                    {
                        bool alpha_on = layer.Attributes["AlphaOn"].Value != "0";
                        uint layer_size = gal.ReadUInt32();
                        gal.Seek (layer_size, SeekOrigin.Current);
                        if (alpha_on)
                        {
                            uint alpha_size = gal.ReadUInt32();
                            gal.Seek (alpha_size, SeekOrigin.Current);
                        }
                    }
                    entry.Size = (uint)(gal.Position - entry.Offset);
                    dir.Add (entry);
                }
                return new ArcFile (file, this, dir);
            }
        }

        public override IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            var galx = (GalXEntry)entry;
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            return new GalXDecoder (input, galx);
        }

        static readonly ResourceInstance<GalXFormat> s_GalXFormat = new ResourceInstance<GalXFormat> ("GAL/X200");

        GalXFormat GalXFormat { get { return s_GalXFormat.Value; } }
    }

    internal class GalXDecoder : GalXReader, IImageDecoder
    {
        ImageData   m_image;
        bool        m_alpha_on;

        public Stream            Source { get { m_input.Position = 0; return m_input.AsStream; } }
        public ImageFormat SourceFormat { get { return null; } }
        public ImageMetaData       Info { get { return m_info; } }

        public ImageData Image {
            get {
                if (null == m_image)
                {
                    UnpackFrame();
                    m_image = ImageData.Create (Info, Format, Palette, Data, Stride);
                }
                return m_image;
            }
        }

        public GalXDecoder (IBinaryStream input, GalXEntry entry) : base (input, entry.Info, 0)
        {
            m_frames.Add (GetFrameFromLayers (entry.Layers));
            m_alpha_on = entry.AlphaOn;
        }

        internal void UnpackFrame ()
        {
            var frame = m_frames[0];
            frame.Layers.Clear();
            m_input.Position = 0;
            int layer_size = m_input.ReadInt32();
            var layer = new Layer();
            layer.Pixels = UnpackLayer (frame, layer_size);
            if (m_alpha_on)
            {
                int alpha_size = m_input.ReadInt32();
                layer.Alpha = UnpackLayer (frame, alpha_size, true);
            }
            frame.Layers.Add (layer);
            Flatten (0);
        }

        bool m_disposed = false;
        protected override void Dispose (bool disposing)
        {
            if (disposing && !m_disposed)
            {
                m_input.Dispose();
                m_disposed = true;
            }
        }
    }
}
