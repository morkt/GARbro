//! \file       ArcUnityFS.cs
//! \date       Tue Apr 04 22:27:22 2017
//! \brief      Unity asset archive.
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
using GameRes.Compression;
using GameRes.Utility;

namespace GameRes.Formats.Unity
{
    [Export(typeof(ArchiveFormat))]
    public class UnityFSOpener : ArchiveFormat
    {
        public override string         Tag { get { return "UNITY/FS"; } }
        public override string Description { get { return "Unity game engine asset archive"; } }
        public override uint     Signature { get { return 0x74696E55; } } // 'UnityFS'
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public UnityFSOpener ()
        {
            Extensions = new string[] { "", "unity3d", "asset" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (0, "UnityFS\0"))
                return null;
            int arc_version = Binary.BigEndian (file.View.ReadInt32 (8));
            if (arc_version != 6)
                return null;
            long data_offset;
            byte[] index_data;
            using (var input = file.CreateStream())
            {
                input.Position = 0xC;
                input.ReadCString (Encoding.UTF8);
                input.ReadCString (Encoding.UTF8);
                long file_size = Binary.BigEndian (input.ReadInt64());
                int packed_index_size = Binary.BigEndian (input.ReadInt32());
                int index_size = Binary.BigEndian (input.ReadInt32());
                int flags = Binary.BigEndian (input.ReadInt32());
                long index_offset;
                if (0 == (flags & 0x80))
                {
                    index_offset = input.Position;
                    data_offset = index_offset + packed_index_size;
                }
                else
                {
                    index_offset = file_size - packed_index_size;
                    data_offset = input.Position;
                }
                input.Position = index_offset;
                var packed = input.ReadBytes (packed_index_size);
                switch (flags & 0x3F)
                {
                case 0:
                    index_data = packed;
                    break;
                case 1:
                    index_data = UnpackLzma (packed, index_size);
                    break;
                case 3:
                    index_data = new byte[index_size];
                    Lz4Compressor.DecompressBlock (packed, packed.Length, index_data, index_data.Length);
                    break;
                default:
                    return null;
                }
            }
            var index = new AssetDeserializer (file, data_offset);
            using (var input = new BinMemoryStream (index_data))
                index.Parse (input);
            var dir = index.LoadObjects();
            return new UnityBundle (file, this, dir, index.Segments, index.Bundles);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var uarc = (UnityBundle)arc;
            Stream input = new BundleStream (uarc.File, uarc.Segments);
            input = new StreamRegion (input, entry.Offset, entry.Size);
            var aent = entry as AssetEntry;
            if (null == aent || !aent.IsEncrypted)
                return input;
            using (input)
            {
                var data = new byte[entry.Size];
                input.Read (data, 0, data.Length);
                DecryptAsset (data);
                return new BinMemoryStream (data);
            }
        }

        public override IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            var aent = entry as AssetEntry;
            if (null == aent || aent.AssetObject.TypeName != "Texture2D")
                return base.OpenImage (arc, entry);
            var uarc = (UnityBundle)arc;
            var obj = aent.AssetObject;
            var bundles = new BundleStream (uarc.File, uarc.Segments);
            var input = new StreamRegion (bundles, obj.Offset, obj.Size);
            var reader = new AssetReader (input, entry.Name);
            reader.SetupReaders (obj.Asset);
            Texture2D tex = null;
            var type = obj.Type;
            if (type != null && type.Children.Any (t => t.Type == "StreamingInfo"))
            {
                var fields = obj.Deserialize (reader);
                tex = new Texture2D();
                tex.Import (fields);
                var info = fields["m_StreamData"] as StreamingInfo;
                if (info != null)
                {
                    var bundle = uarc.Bundles.FirstOrDefault (b => VFS.IsPathEqualsToFileName (info.Path, b.Name));
                    if (bundle != null)
                    {
                        tex.m_DataLength = (int)info.Size;
                        input = new StreamRegion (bundles, bundle.Offset+info.Offset, info.Size);
                        reader = new AssetReader (input, entry.Name);
                    }
                }
            }
            if (null == tex)
            {
                tex = new Texture2D();
                tex.Load (reader);
            }
            return new Texture2DDecoder (tex, reader);
        }

        internal static byte[] UnpackLzma (byte[] input, int unpacked_size)
        {
            throw new NotImplementedException();
        }

        internal void DecryptAsset (byte[] data)
        {
            uint key = 0xBF8766F5u;
            for (int i = 0; i < data.Length; ++i)
            {
                key = ((0x343FD * key + 0x269EC3) >> 16) & 0x7FFF; // MSVC rand()
                data[i] ^= (byte)key;
            }
        }
    }

    internal class BundleEntry : Entry
    {
        public uint Flags;
    }

    internal class AssetEntry : Entry
    {
        public BundleEntry  Bundle;
        public UnityObject  AssetObject;
        public bool         IsEncrypted;
    }

    internal class UnityBundle : ArcFile
    {
        public readonly List<BundleSegment> Segments;
        public readonly List<BundleEntry>   Bundles;

        public UnityBundle (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, List<BundleSegment> segments, List<BundleEntry> bundles)
            : base (arc, impl, dir)
        {
            Segments = segments;
            Bundles = bundles;
        }
    }

    internal class BundleSegment
    {
        public long Offset;
        public uint PackedSize;
        public long UnpackedOffset;
        public uint UnpackedSize;
        public int  Compression;

        public bool IsCompressed { get { return (Compression & 0x3F) != 0; } }
    }

    internal class AssetDeserializer
    {
        readonly ArcView    m_file;
        readonly long       m_data_offset;
        List<BundleSegment> m_segments;
        List<BundleEntry>   m_bundles;

        public List<BundleSegment> Segments { get { return m_segments; } }
        public List<BundleEntry>    Bundles { get { return m_bundles; } }

        public AssetDeserializer (ArcView file, long data_offset)
        {
            m_file = file;
            m_data_offset = data_offset;
        }

        public void Parse (IBinaryStream index)
        {
            index.Position = 16;
            int segment_count = Binary.BigEndian (index.ReadInt32());
            m_segments = new List<BundleSegment> (segment_count);
            long packed_offset = m_data_offset;
            long unpacked_offset = 0;
            for (int i = 0; i < segment_count; ++i)
            {
                var segment = new BundleSegment();
                segment.Offset = packed_offset;
                segment.UnpackedOffset = unpacked_offset;
                segment.UnpackedSize = Binary.BigEndian (index.ReadUInt32());
                segment.PackedSize = Binary.BigEndian (index.ReadUInt32());
                segment.Compression = Binary.BigEndian (index.ReadUInt16());
                m_segments.Add (segment);
                packed_offset += segment.PackedSize;
                unpacked_offset += segment.UnpackedSize;
            }
            int count = Binary.BigEndian (index.ReadInt32());
            m_bundles = new List<BundleEntry> (count);
            for (int i = 0; i < count; ++i)
            {
                var entry = new BundleEntry();
                entry.Offset = Binary.BigEndian (index.ReadInt64());
                entry.Size = (uint)Binary.BigEndian (index.ReadInt64());
                entry.Flags = Binary.BigEndian (index.ReadUInt32());
                entry.Name = index.ReadCString (Encoding.UTF8);
                m_bundles.Add (entry);
            }
        }

        public List<Entry> LoadObjects ()
        {
            var dir = new List<Entry>();
            using (var stream = new BundleStream (m_file, m_segments))
            {
                foreach (BundleEntry bundle in m_bundles)
                {
                    if (bundle.Name.HasAnyOfExtensions (".resource", ".resS"))
                        continue;
                    using (var asset_stream = new StreamRegion (stream, bundle.Offset, bundle.Size, true))
                    using (var reader = new AssetReader (asset_stream, bundle.Name))
                    {
                        var asset = new Asset();
                        asset.Load (reader);
                        var object_dir = ParseAsset (stream, bundle, asset);
                        dir.AddRange (object_dir);
                    }
                }
                if (0 == dir.Count)
                    dir.AddRange (m_bundles);
            }
            return dir;
        }

        IEnumerable<Entry> ParseAsset (Stream file, BundleEntry bundle, Asset asset)
        {
            Dictionary<long, string> id_map = null;
            var bundle_types = asset.Tree.TypeTrees.Where (t => t.Value.Type == "AssetBundle").Select (t => t.Key);
            if (bundle_types.Any())
            {
                // try to read entry names from AssetBundle object
                int bundle_type_id = bundle_types.First();
                var asset_bundle = asset.Objects.FirstOrDefault (x => x.TypeId == bundle_type_id);
                if (asset_bundle != null)
                {
                    id_map = ReadAssetBundle (file, asset_bundle);
                }
            }
            if (null == id_map)
                id_map = new Dictionary<long, string>();
            foreach (var obj in asset.Objects)
            {
                var entry = ReadAsset (file, obj);
                if (null == entry)
                    continue;
                if (null == entry.Bundle)
                    entry.Bundle = bundle;
                string name;
                if (!id_map.TryGetValue (obj.PathId, out name))
                    name = GetObjectName (file, obj);
                else
                    name = ShortenPath (name);
                entry.Name = name;
                yield return entry;
            }
        }

        AssetEntry ReadAsset (Stream file, UnityObject obj)
        {
            string type = obj.TypeName;
            if ("AudioClip" == type)
                return ReadAudioClip (file, obj);
            else if ("TextAsset" == type)
                return ReadTextAsset (file, obj);
            else if ("Texture2D" == type)
                type = "image";
            else if ("AssetBundle" == type)
                return null;

            return new AssetEntry {
                Type = type,
                AssetObject = obj,
                Offset = obj.Offset,
                Size = obj.Size,
            };
        }

        Dictionary<long, string> ReadAssetBundle (Stream input, UnityObject bundle)
        {
            using (var reader = bundle.Open (input))
            {
                var name = reader.ReadString(); // m_Name
                reader.Align();
                int count = reader.ReadInt32(); // m_PreloadTable
                for (int i = 0; i < count; ++i)
                {
                    reader.ReadInt32(); // m_FileID
                    reader.ReadInt64(); // m_PathID
                }
                count = reader.ReadInt32(); // m_Container
                var id_map = new Dictionary<long, string> (count+1);
                id_map[bundle.PathId] = name;
                for (int i = 0; i < count; ++i)
                {
                    name = reader.ReadString();
                    reader.Align();
                    reader.ReadInt32(); // preloadIndex
                    reader.ReadInt32(); // preloadSize
                    reader.ReadInt32(); // m_FileID
                    long id = reader.ReadInt64();
                    id_map[id] = name;
                }
                return id_map;
            }
        }

        string GetObjectName (Stream input, UnityObject obj)
        {
            var type = obj.Type;
            if (type != null && type.Children.Count > 0)
            {
                var first_field = type.Children[0];
                if ("m_Name" == first_field.Name && "string" == first_field.Type)
                {
                    using (var reader = obj.Open (input))
                    {
                        var name = reader.ReadString();
                        if (!string.IsNullOrEmpty (name))
                            return name;
                    }
                }
            }
            return obj.PathId.ToString ("X16");
        }

        AssetEntry ReadTextAsset (Stream input, UnityObject obj)
        {
            var script = obj.Type.Children.FirstOrDefault (f => f.Name == "m_Script");
            if (null == script)
                return null;
            using (var reader = obj.Open (input))
            {
                var name = reader.ReadString();
                reader.Align();
                uint size = reader.ReadUInt32();
                var entry = new AssetEntry {
                    AssetObject = obj,
                    Offset = obj.Offset + reader.Position,
                    Size = size,
                    IsEncrypted = 0 != (script.Flags & 0x04000000),
                };
                if (entry.IsEncrypted)
                {
                    uint signature = reader.ReadUInt32();
                    if (0x0D15F641 == signature)
                        entry.Type = "image";
                    else if (0x474E5089 == signature)
                    {
                        entry.Type = "image";
                        entry.IsEncrypted = false;
                    }
                }
                return entry;
            }
        }

        AssetEntry ReadAudioClip (Stream input, UnityObject obj)
        {
            using (var reader = obj.Open (input))
            {
                var clip = new AudioClip();
                clip.Load (reader);
                var bundle_name = Path.GetFileName (clip.m_Source);
                var bundle = m_bundles.FirstOrDefault (b => b.Name == bundle_name);
                if (null == bundle)
                    return null;
                return new AssetEntry {
                    Type = "audio",
                    Bundle = bundle,
                    AssetObject = obj,
                    Offset = bundle.Offset + clip.m_Offset,
                    Size = (uint)clip.m_Size,
                };
            }
        }

        /// <summary>
        /// Shorten asset path to contain only the bottom directory component.
        /// </summary>
        static string ShortenPath (string name)
        {
            int slash_pos = name.LastIndexOf ('/');
            if (-1 == slash_pos)
                return name;
            slash_pos = name.LastIndexOf ('/', slash_pos-1);
            if (-1 == slash_pos)
                return name;
            return name.Substring (slash_pos+1);
        }
    }
}
