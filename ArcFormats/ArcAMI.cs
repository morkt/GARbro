//! \file       ArcAMI.cs
//! \date       Thu Jul 03 09:40:40 2014
//! \brief      Muv-Luv Amaterasu Translation archive.
//

using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ZLibNet;

namespace GameRes.Formats
{
    public class AmiEntry : PackedEntry
    {
        private Lazy<Tuple<string,string>> m_item;
        public override string Name
        {
            get { return m_item.Value.Item1; }
            set { m_item = new Lazy<Tuple<string, string>>(() => new Tuple<string, string>(value, Type)); }
        }
        public override string Type
        {
            get { return m_item.Value.Item2; }
            set { m_item = new Lazy<Tuple<string, string>>(() => new Tuple<string, string>(Name, value)); }
        }

        public AmiEntry (Func<Tuple<string, string>> factory)
        {
            m_item = new Lazy<Tuple<string, string>> (factory);
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class AmiOpener : ArchiveFormat
    {
        public override string Tag { get { return "AMI"; } }
        public override string Description { get { return Strings.arcStrings.AMIDescription; } }
        public override uint Signature { get { return 0x00494d41; } }
        public override bool IsHierarchic { get { return false; } }

        public AmiOpener ()
        {
            Extensions = new string[] { "ami", "amr" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (4);
            if (count <= 0)
                return null;
            uint base_offset = file.View.ReadUInt32 (8);
            long max_offset = file.MaxOffset;
            if (base_offset >= max_offset)
                return null;

            uint cur_offset = 16;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                if (cur_offset+16 > base_offset)
                    return null;
                uint id = file.View.ReadUInt32 (cur_offset);
                uint offset = file.View.ReadUInt32 (cur_offset+4);
                uint size = file.View.ReadUInt32 (cur_offset+8);
                uint packed_size = file.View.ReadUInt32 (cur_offset+12);

                var entry = new AmiEntry (() => {
                    uint signature = file.View.ReadUInt32 (offset);
                    string ext, type;
                    if (0x00524353 == signature) {
                        ext = "scr"; type = "script";
                    } else if (0 != packed_size) {
                        ext = "grp"; type = "image";
                    } else {
                        ext = "dat"; type = "";
                    }
                    string name = string.Format ("{0:x8}.{1}", id, ext);
                    return new Tuple<string,string> (name, type);
                });

                entry.Offset = offset;
                entry.UnpackedSize = size;
                entry.IsPacked = 0 != packed_size;
                entry.Size   = entry.IsPacked ? packed_size : size;
                if (!entry.CheckPlacement (max_offset))
                    return null;
                dir.Add (entry);
                cur_offset += 16;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            var packed_entry = entry as AmiEntry;
            if (null == packed_entry || !packed_entry.IsPacked)
                return input;
            else
                return new ZLibStream (input, CompressionMode.Decompress);
        }
    }

    [Export(typeof(ImageFormat))]
    public class GrpFormat : ImageFormat
    {
        public override string Tag { get { return "GRP"; } }
        public override string Description { get { return Strings.arcStrings.GRPDescription; } }
        public override uint Signature { get { return 0x00505247; } }

        public override ImageMetaData ReadMetaData (Stream stream)
        {
            using (var file = new BinaryReader (stream, Encoding.ASCII, true))
            {
                if (file.ReadUInt32() != Signature)
                    return null;
                var meta = new ImageMetaData();
                meta.OffsetX = file.ReadInt16();
                meta.OffsetY = file.ReadInt16();
                meta.Width   = file.ReadUInt16();
                meta.Height  = file.ReadUInt16();
                meta.BPP     = 32;
                stream.Position = 0;
                return meta;
            }
        }

        public override ImageData Read (Stream file, ImageMetaData info)
        {
            int width = (int)info.Width;
            int height = (int)info.Height;
            int stride = width*4;
            byte[] pixels = new byte[stride*height];
            file.Position = 12;
            for (int row = height-1; row >= 0; --row)
            {
                if (stride != file.Read (pixels, row*stride, stride))
                    throw new InvalidFormatException();
            }
            var bitmap = BitmapSource.Create (width, height, 96, 96,
                PixelFormats.Bgra32, null, pixels, stride);
            bitmap.Freeze();

            return new ImageData (bitmap, info);
        }

        public override void Write (Stream stream, ImageData image)
        {
            using (var file = new BinaryWriter (stream, Encoding.ASCII, true))
            {
                file.Write (Signature);
                file.Write ((short)image.OffsetX);
                file.Write ((short)image.OffsetY);
                file.Write ((ushort)image.Width);
                file.Write ((ushort)image.Height);

                var bitmap = image.Bitmap;
                if (bitmap.Format != PixelFormats.Bgra32)
                {
                    var converted_bitmap = new FormatConvertedBitmap();
                    converted_bitmap.BeginInit();
                    converted_bitmap.Source = image.Bitmap;
                    converted_bitmap.DestinationFormat = PixelFormats.Bgra32;
                    converted_bitmap.EndInit();
                    bitmap = converted_bitmap;
                }
                int stride = (int)image.Width * 4;
                byte[] row_data = new byte[stride];
                Int32Rect rect = new Int32Rect (0, (int)image.Height, (int)image.Width, 1);
                for (uint row = 0; row < image.Height; ++row)
                {
                    --rect.Y;
                    bitmap.CopyPixels (rect, row_data, stride, 0);
                    file.Write (row_data);
                }
            }
        }
    }

    public class AmiScriptData : ScriptData
    {
        public uint Id;
        public uint Type;
    }

    [Export(typeof(ScriptFormat))]
    public class ScrFormat : ScriptFormat
    {
        public override string Tag { get { return "SCR"; } }
        public override string Description { get { return Strings.arcStrings.SCRDescription; } }
        public override uint Signature { get { return 0x00524353; } }

        public override ScriptData Read (string name, Stream stream)
        {
            if (Signature != FormatCatalog.ReadSignature (stream))
                return null;
            uint script_id = Convert.ToUInt32 (name, 16);
            uint max_offset = (uint)Math.Min (stream.Length, 0xffffffff);

            using (var file = new BinaryReader (stream, Encodings.cp932, true))
            {
                uint script_type = file.ReadUInt32();
                var script = new AmiScriptData {
                    Id = script_id,
                    Type = script_type
                };
                uint count = file.ReadUInt32();
                for (uint i = 0; i < count; ++i)
                {
                    uint offset = file.ReadUInt32();
                    if (offset >= max_offset)
                        throw new InvalidFormatException ("Invalid offset in script data file");
                    int size = file.ReadInt32();
                    uint id = file.ReadUInt32();
                    var header_pos = file.BaseStream.Position;
                    file.BaseStream.Position = offset;
                    byte[] line = file.ReadBytes (size);
                    if (line.Length != size)
                        throw new InvalidFormatException ("Premature end of file");
                    string text = Encodings.cp932.GetString (line);

                    script.TextLines.Add (new ScriptLine { Id = id, Text = text });
                    file.BaseStream.Position = header_pos;
                }
                return script;
            }
        }

        public string GetName (ScriptData script_data)
        {
            var script = script_data as AmiScriptData;
            if (null != script)
                return script.Id.ToString ("x8");
            else
                return null;
        }

        struct IndexEntry
        {
            public uint offset, size, id;
        }

        public override void Write (Stream stream, ScriptData script_data)
        {
            var script = script_data as AmiScriptData;
            if (null == script)
                throw new ArgumentException ("Illegal ScriptData", "script_data");
            using (var file = new BinaryWriter (stream, Encodings.cp932, true))
            {
                file.Write (Signature);
                file.Write (script.Type);
                uint count = (uint)script.TextLines.Count;
                file.Write (count);
                var index_pos = file.BaseStream.Position;
                file.Seek ((int)count*12, SeekOrigin.Current);
                var index = new IndexEntry[count];
                int i = 0;
                foreach (var line in script.TextLines)
                {
                    var text = Encodings.cp932.GetBytes (line.Text);
                    index[i].offset = (uint)file.BaseStream.Position;
                    index[i].size   = (uint)text.Length;
                    index[i].id     = line.Id;
                    file.Write (text);
                    file.Write ((byte)0);
                    ++i;
                }
                var end_pos = file.BaseStream.Position;
                file.BaseStream.Position = index_pos;
                foreach (var entry in index)
                {
                    file.Write (entry.offset);
                    file.Write (entry.size);
                    file.Write (entry.id);
                }
                file.BaseStream.Position = end_pos;
            }
        }
    }
}
