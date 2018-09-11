//! \file       ImagePGX.cs
//! \date       Tue Dec 08 02:50:45 2015
//! \brief      Glib2 image format.
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

using GameRes.Utility;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Media;

namespace GameRes.Formats.Glib2
{
    internal class PgxMetaData : ImageMetaData
    {
        public int  PackedSize;
        public int  Flags;
    }

    internal class StxLayerInfo
    {
        public string       Path;
        public Rectangle?   Rect;
        public string       Effect;
        public int          Blend;
    }

    [Export(typeof(ImageFormat))]
    public class PgxFormat : ImageFormat
    {
        public override string         Tag { get { return "PGX"; } }
        public override string Description { get { return "Glib2 engine image format"; } }
        public override uint     Signature { get { return 0x00584750; } } // 'PGX'

        static readonly InfoReader InfoCache = new InfoReader();

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            var header = stream.ReadHeader (0x18);
            return new PgxMetaData
            {
                Width   = header.ToUInt32 (8),
                Height  = header.ToUInt32 (12),
                BPP     = (header.ToInt16 (0x10) & 1) == 0 ? 24 : 32,
                PackedSize = header.ToInt32 (0x14),
                Flags   = header.ToUInt16 (0x12),
            };
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            var meta = (PgxMetaData)info;
            stream.Position = 0x20;
            if (0 != (meta.Flags & 0x1000))
            {
                ReadGms (stream);
            }
            PixelFormat format = 32 == meta.BPP ? PixelFormats.Bgra32 : PixelFormats.Bgr32;
            int stride = (int)meta.Width * 4;
            var pixels = new byte[stride * (int)meta.Height];
//            stream.Seek (-meta.PackedSize, SeekOrigin.End);
            LzssUnpack (stream, pixels);
            var layer = InfoCache.GetInfo (info.FileName);
            if (null != layer && null != layer.Rect)
            {
                info.OffsetX = layer.Rect.Value.X;
                info.OffsetY = layer.Rect.Value.Y;
            }
            return ImageData.Create (info, format, null, pixels);
        }

        void ReadGms (IBinaryStream input)
        {
            var header = new byte[0x10];
            if (0x10 != input.Read (header, 0, 0x10))
                throw new EndOfStreamException();
            byte swap = header[13];
            header[13] = header[9];
            header[9] = swap;
            swap = header[15];
            header[15] = header[11];
            header[11] = swap;
            swap = header[4];
            header[4] = header[8];
            header[8] = swap;
            swap = header[6];
            header[6] = header[10];
            header[10] = swap;
            int unpacked_size = LittleEndian.ToInt32 (header, 12);
            var gms_info = new byte[unpacked_size];
            LzssUnpack (input, gms_info);

            // foreach (byte in gms_info) byte ^= 0xFF
            // decrypted array contains image meta data (see InfoReader class)
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("PgxFormat.Write not implemented");
        }

        static void LzssUnpack (IBinaryStream input, byte[] output)
        {
            GLib.GOpener.LzssUnpack (input.AsStream, output);
        }
    }

    internal class InfoReader
    {
        string                              m_last_info_dir;
        Dictionary<string, StxLayerInfo>    m_layer_map;

        internal class StxEntry
        {
            public string   FullName;
            public string   Name;
            public int      Attr;
            public uint     InfoOffset;
            public uint     InfoSize;
        }

        public StxLayerInfo GetInfo (string image_name)
        {
            try
            {
                var info_name = VFS.CombinePath (VFS.GetDirectoryName (image_name), "info");
                if (!VFS.FileExists (info_name))
                    return null;
                if (string.IsNullOrEmpty (m_last_info_dir)
                    || string.Join (":", VFS.FullPath) != m_last_info_dir)
                    ParseInfo (info_name);

                var layer_name = Path.GetFileName (image_name);
                return GetLayerInfo (layer_name);
            }
            catch (Exception X)
            {
                Trace.WriteLine (X.Message, "[Glib2] STX parse error");
                return null;
            }
        }

        StxLayerInfo GetLayerInfo (string layer_name)
        {
            if (null == m_layer_map)
                return null;
            StxLayerInfo info;
            m_layer_map.TryGetValue (layer_name, out info);
            return info;
        }

        void ParseInfo (string info_name)
        {
            if (null == m_layer_map)
                m_layer_map = new Dictionary<string, StxLayerInfo>();
            else
                m_layer_map.Clear();

            using (var info = VFS.OpenView (info_name))
            {
                if (!info.View.AsciiEqual (0, "CDBD"))
                    return;
                int count = info.View.ReadInt32 (4);
                uint current_offset = 0x10;
                uint info_base = current_offset + info.View.ReadUInt32 (8);
                uint info_total_size = info.View.ReadUInt32 (12);
                info.View.Reserve (0, info_base+info_total_size);
                uint names_base = current_offset + (uint)count * 0x18;
                var dir = new List<StxEntry> (count);
                for (int i = 0; i < count; ++i)
                {
                    uint name_offset = names_base + info.View.ReadUInt32 (current_offset);
                    int parent_dir = info.View.ReadInt32 (current_offset+8);
                    int attr = info.View.ReadInt32 (current_offset+0xC);
                    uint info_offset = info.View.ReadUInt32 (current_offset+0x10);
                    uint info_size   = info.View.ReadUInt32 (current_offset+0x14);

                    var name = info.View.ReadString (name_offset, info_base-name_offset);
                    string path_name = name;
                    if (parent_dir != -1)
                        path_name = Path.Combine (dir[parent_dir].FullName, path_name);

                    if (attr != -1 && info_size != 0)
                        info_offset += info_base;
                    var entry = new StxEntry {
                        FullName = path_name,
                        Name = name,
                        Attr = attr,
                        InfoOffset = info_offset,
                        InfoSize = info_size,
                    };
                    if (name == "filename" && parent_dir != -1 && info_size != 0)
                    {
                        uint filename_length = info.View.ReadUInt32 (info_offset);
                        var filename = info.View.ReadString (info_offset+4, filename_length);
                        m_layer_map[filename] = new StxLayerInfo {
                            Path = dir[parent_dir].FullName + Path.DirectorySeparatorChar,
                        };
                    }
                    dir.Add (entry);
                    current_offset += 0x18;
                }
                foreach (var layer in m_layer_map.Values)
                {
                    foreach (var field in dir.Where (e => e.Attr != -1 && e.FullName.StartsWith (layer.Path)))
                    {
                        if ("rect" == field.Name && 0x14 == field.InfoSize)
                        {
                            int left   = info.View.ReadInt32 (field.InfoOffset+4);
                            int top    = info.View.ReadInt32 (field.InfoOffset+8);
                            int right  = info.View.ReadInt32 (field.InfoOffset+12);
                            int bottom = info.View.ReadInt32 (field.InfoOffset+16);
                            layer.Rect = new Rectangle (left, top, right-left, bottom-top);
                        }
                        else if ("effect" == field.Name && field.InfoSize > 4)
                        {
                            // "norm"
                            uint effect_length = info.View.ReadUInt32 (field.InfoOffset);
                            layer.Effect = info.View.ReadString (field.InfoOffset+4, effect_length);
                            if (layer.Effect != "norm")
                                Trace.WriteLine (string.Format ("{0}: {1}effect = {2}",
                                    info_name, layer.Path, layer.Effect), "[Glib2.STX]");
                        }
                        else if ("blend" == field.Name && 4 == field.InfoSize)
                        {
                            // 0xFF -> opaque
                            layer.Blend = info.View.ReadInt32 (field.InfoOffset);
                            if (layer.Blend != 0xFF)
                                Trace.WriteLine (string.Format ("{0}: {1}blend = {2}",
                                    info_name, layer.Path, layer.Blend), "[Glib2.STX]");
                        }
                    }
                }
            }
            m_last_info_dir = string.Join (":", VFS.FullPath);
        }
    }
}
