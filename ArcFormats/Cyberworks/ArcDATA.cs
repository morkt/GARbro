//! \file       ArcDATA.cs
//! \date       2025 Apr 14
//! \brief      Tinker Bell resource archive in Resources subdirectory.
//
// Copyright (C) 2016-2017 by morkt
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
using System.ComponentModel.Composition;
using System.Collections.Generic;
using GameRes.Compression;
using GameRes.Formats.Strings;
using GameRes.Utility;
using GameRes.Strings;

namespace GameRes.Formats.Cyberworks
{
    [Serializable]
    public class DataScheme 
    {
        public int ExtraHeaderSize;
    }

    [Serializable]
    public class DataSchemeMap : ResourceScheme 
    {
        public Dictionary<string, DataScheme> KnownSchemes;
    }

    public class DataOptions : ResourceOptions 
    {
        public string Scheme;
    } 

    [Export(typeof(ArchiveFormat))]
    public class DataOpener : ArchiveFormat
    {
        public override string          Tag { get { return "DATA/Csystem"; } }
        public override string  Description { get { return "TinkerBell resource archive in Resources subdirectory"; } }
        public override uint      Signature { get { return 0; } }
        public override bool   IsHierarchic { get { return false; } }
        public override bool       CanWrite { get { return false; } }

        static DataSchemeMap DefaultScheme = new DataSchemeMap { KnownSchemes = new Dictionary<string, DataScheme>() };

        static public Dictionary<string, DataScheme> KnownSchemes { get { return DefaultScheme.KnownSchemes; } }

        public override ResourceScheme Scheme 
        {
            get { return DefaultScheme; }
            set { DefaultScheme = (DataSchemeMap)value; }
        }

        public override ArcFile TryOpen(ArcView file)
        {
            var arc_name = Path.GetFileName(file.Name);
            var dir_name = VFS.GetDirectoryName(file.Name);
            var toc_arc_name = VFS.CombinePath(dir_name, "Data00.dat");
            if (!VFS.FileExists(toc_arc_name))
               return null;
            if ("Data00.dat".Equals(arc_name, StringComparison.OrdinalIgnoreCase)) 
                return null;
            if (!int.TryParse(arc_name.Substring(4, arc_name.IndexOf('.') - 4), out int arc_index))
                return null;
            var scheme = QueryScheme(arc_name);
            var dir = ScanDir(toc_arc_name, arc_index, scheme);
            if (null == dir || 0 == dir.Count)
                return null;

            return new ArcFile(file, this, dir);
            
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry) 
        {
            if (0 == entry.Size) 
                throw new FileSizeException(garStrings.MsgFileIsEmpty);     //in some cases,the size of entry maybe 0,like game Mujina(vndb.org/v48764).

            Stream input = arc.File.CreateStream(entry.Offset, entry.Size);
            var pent = entry as PackedEntry;
            if (null != pent && pent.IsPacked)
            {
                input = new LzssStream(input);
            }
            return input;
        }

        public override IImageDecoder OpenImage(ArcFile arc, Entry entry)
        {

            if(entry.Size < 0x1D)
               return base.OpenImage(arc, entry);
            var input = arc.OpenBinaryEntry(entry);
            try 
            {
                var reader = DecryptImage(input);
                var data_img_decoder = reader as DataImageDecoder;
                if (null != data_img_decoder 
                    && data_img_decoder.HasBaseImage)
                {
                    data_img_decoder.ReadBaseImage(arc);
                }
                return reader;
            }
            catch 
            {
                input.Dispose();
                throw;
            }
            
        }

        protected virtual IImageDecoder DecryptImage(IBinaryStream input) 
        {
            var type = input.ReadByte();
            switch (type) 
            {
                case 0x61:
                case 0x64: 
                {
                    return new DataImageDecoder(input,type);
                }
                case 0x62:
                case 0x63: 
                {
                    var img_size = Binary.BigEndian(input.ReadUInt32());
                    var start_pos = input.Length - img_size;
                    input = BinaryStream.FromStream(new StreamRegion(input.AsStream, start_pos, img_size),input.Name);
                    break;    
                }
            }
            input.Position = 0;
            return new ImageFormatDecoder(input);
        }

        List<Entry> ScanDir(string toc_arc_name, int arc_index, DataScheme scheme) 
        {
            var dir = new List<Entry>();
            var toc_offset = 0;

            using (var toc_file = VFS.OpenView(toc_arc_name))
            {
                var data_file_count = toc_file.View.ReadInt32(toc_offset);
                toc_offset += 4;

                if(arc_index >= data_file_count)
                   return null;

                for (var i = 0; i < data_file_count; ++i) 
                {
                    var data_entry_count = toc_file.View.ReadInt32(toc_offset);
                    toc_offset += 4;

                    for (var j = 0; j < data_entry_count; ++j) 
                    {
                        if (i == arc_index)
                        {
                            var entry = new PackedEntry { Name = string.Format("{0:D4}", j) };
                            entry.UnpackedSize  = toc_file.View.ReadUInt32(toc_offset);
                            entry.Size          = toc_file.View.ReadUInt32(toc_offset + 4);
                            entry.Offset        = toc_file.View.ReadUInt32(toc_offset + 8);
                            entry.IsPacked      = (0 != entry.UnpackedSize);
                            entry.Type          = "image";
                            dir.Add(entry);

                        }
                        toc_offset += (0xC + scheme.ExtraHeaderSize);
                    }

                    var unknow_chunkA_count = toc_file.View.ReadInt32(toc_offset);
                    toc_offset += 4;
                    for (var j = 0; j < unknow_chunkA_count; ++j) 
                    {
                        toc_offset += 0xC;
                        var chunkA_extra_count = toc_file.View.ReadInt32(toc_offset);
                        toc_offset += 4;
                        toc_offset += ((chunkA_extra_count-1) * 4);
                        toc_offset += (chunkA_extra_count * 0xC);
                    }

                    var unknow_chunkB_count = toc_file.View.ReadInt32(toc_offset);
                    toc_offset += 4;
                    if (0 != unknow_chunkB_count) 
                    {
                        toc_offset += 4;
                        var chunkB_extra_count = toc_file.View.ReadInt32(toc_offset);
                        toc_offset += 4;
                        toc_offset += (chunkB_extra_count*8);
                    }

                }

            }

            return dir;
        }

        DataScheme QueryScheme(string arc_name) 
        {
            var title = FormatCatalog.Instance.LookupGame(arc_name, @"..\*.exe");
            DataScheme scheme = new DataScheme();

            if (!string.IsNullOrEmpty(title) && KnownSchemes.TryGetValue(title, out scheme))
                return scheme;
            var options = Query<DataOptions>(arcStrings.ArcEncryptedNotice);
            if (null != options)
                KnownSchemes.TryGetValue(options.Scheme,out scheme);
            return scheme;

        }

        public override object GetAccessWidget()
        {
            return new GUI.WidgetBELLDATA();
        }

        public override ResourceOptions GetDefaultOptions() 
        {
            return new DataOptions { Scheme = Properties.Settings.Default.BELLDATATitle }; 
        }

    }
}
