//! \file       ArcASSET.cs
//! \date       2017 Nov 22
//! \brief      Unity assets archive.
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

using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using GameRes.Utility;

namespace GameRes.Formats.Unity
{
    [Export(typeof(ArchiveFormat))]
    public class UnityAssetOpener : ArchiveFormat
    {
        public override string         Tag { get { return "ASSETS/UNITY"; } }
        public override string Description { get { return "Unity game engine assets archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            uint header_size = Binary.BigEndian (file.View.ReadUInt32 (0));
            uint file_size = Binary.BigEndian (file.View.ReadUInt32 (4));
            if (file_size != file.MaxOffset || header_size > file_size || 0 == header_size)
                return null;
            using (var stream = file.CreateStream())
            using (var input = new AssetReader (stream))
            {
                var index = new ResourcesAssetsDeserializer (file);
                var dir = index.Parse (input);
                if (null == dir || 0 == dir.Count)
                    return null;
                var res_map = index.GenerateResourceMap (dir);
                return new UnityResourcesAsset (file, this, dir, res_map);
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var uarc = (UnityResourcesAsset)arc;
            var uent = (AssetEntry)entry;
            if (null == uent.Bundle || !uarc.ResourceMap.ContainsKey (uent.Bundle.Name))
                return arc.File.CreateStream (entry.Offset, entry.Size);
            var bundle = uarc.ResourceMap[uent.Bundle.Name];
            return bundle.CreateStream (entry.Offset, entry.Size);
        }

        public override IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            var aent = entry as AssetEntry;
            if (null == aent || aent.AssetObject.TypeId != 28)
                return base.OpenImage (arc, entry);

            var obj = aent.AssetObject;
            using (var stream = arc.File.CreateStream (obj.Offset, obj.Size))
            using (var reader = new AssetReader (stream))
            {
                reader.SetupReaders (obj.Asset);
                var tex = new Texture2D();
                tex.Load (reader);

                var input = OpenEntry (arc, entry);
                try
                {
                    tex.m_Data = new byte[entry.Size];
                    input.Read (tex.m_Data, 0, tex.m_Data.Length);
                    var bin_input = BinaryStream.FromStream (input, entry.Name);
                    var tex_reader = new AssetReader (bin_input);
                    tex_reader.SetupReaders (obj.Asset);
                    return new Texture2DDecoder (tex, tex_reader);
                }
                catch
                {
                    input.Dispose();
                    throw;
                }
            }
        }
    }

    internal class UnityResourcesAsset : ArcFile
    {
        public readonly IDictionary<string, ArcView> ResourceMap;

        public UnityResourcesAsset (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, IDictionary<string, ArcView> res_map)
            : base (arc, impl, dir)
        {
            ResourceMap = res_map;
        }

        #region IDisposable Members
        bool m_disposed = false;

        protected override void Dispose (bool disposing)
        {
            if (!m_disposed)
            {
                if (disposing)
                {
                    foreach (var res in ResourceMap.Values)
                    {
                        res.Dispose();
                    }
                }
                m_disposed = true;
            }
        }
        #endregion
    }

    internal class ResourcesAssetsDeserializer
    {
        string                          m_res_name;
        Dictionary<string, BundleEntry> m_bundles;

        public ResourcesAssetsDeserializer (ArcView file)
        {
            m_res_name = file.Name;
        }

        public List<Entry> Parse (AssetReader input)
        {
            var asset = new Asset();
            asset.Load (input);
            var dir = new List<Entry>();
            m_bundles = new Dictionary<string, BundleEntry>();
            var used_names = new HashSet<string>();

            foreach (var obj in asset.Objects.Where (o => o.TypeId > 0))
            {
                input.Position = obj.Offset;
                AssetEntry entry = null;
                switch (obj.TypeId)
                {
                default:
                    break;

                case 28: // Texture2D
                    {
                        var tex = new Texture2D();
                        tex.Load (input);
                        if (tex.m_StreamData != null && !string.IsNullOrEmpty (tex.m_StreamData.Path))
                        {
                            entry = new AssetEntry {
                                Name = tex.m_Name,
                                Type = "image",
                                Offset = tex.m_StreamData.Offset,
                                Size = tex.m_StreamData.Size,
                                Bundle = GetBundle (tex.m_StreamData.Path),
                                AssetObject = obj,
                            };
                        }
                        break;
                    }
                case 83: // AudioClip
                    {
                        var clip = new AudioClip();
                        clip.Load (input);
                        if (!string.IsNullOrEmpty (clip.m_Source))
                        {
                            entry = new AssetEntry {
                                Name = clip.m_Name,
                                Type = "audio",
                                Offset = clip.m_Offset,
                                Size = (uint)clip.m_Size,
                                Bundle = GetBundle (clip.m_Source),
                                AssetObject = obj,
                            };
                        }
                        break;
                    }
                case 49:  // TextAsset
                    {
                        var name = input.ReadString();
                        input.Align();
                        uint size = input.ReadUInt32();
                        entry = new AssetEntry {
                            Name =  name,
                            Offset = input.Position,
                            Size = size,
                            AssetObject = obj,
                        };
                        break;
                    }
                case 128: // Font
                    {
                        entry = new AssetEntry {
                            Offset = obj.Offset,
                            Size = obj.Size,
                            AssetObject = obj,
                        };
                        break;
                    }
                }
                if (entry != null)
                {
                    if (string.IsNullOrEmpty (entry.Name)
                        entry.Name = string.Format ("{0:D4} [{1}]", obj.PathId, obj.TypeId);
                    else if (!used_names.Add (entry.Name))
                        entry.Name = string.Format ("{0}-{1}", entry.Name, obj.PathId);
                    dir.Add (entry);
                }
            }
            return dir;
        }

        public Dictionary<string, ArcView> GenerateResourceMap (List<Entry> dir)
        {
            var res_map = new Dictionary<string, ArcView>();
            var asset_dir = VFS.GetDirectoryName (m_res_name);
            foreach (AssetEntry entry in dir)
            {
                if (null == entry.Bundle)
                    continue;
                if (res_map.ContainsKey (entry.Bundle.Name))
                    continue;
                var bundle_name = VFS.CombinePath (asset_dir, entry.Bundle.Name);
                if (!VFS.FileExists (bundle_name))
                {
                    entry.Bundle = null;
                    entry.Offset = entry.AssetObject.Offset;
                    entry.Size   = entry.AssetObject.Size;
                    continue;
                }
                res_map[entry.Bundle.Name] = VFS.OpenView (bundle_name);
            }
            return res_map;
        }

        BundleEntry GetBundle (string path)
        {
            BundleEntry bundle;
            if (!m_bundles.TryGetValue (path, out bundle))
            {
                bundle = new BundleEntry { Name = path };
                m_bundles[path] = bundle;
            }
            return bundle;
        }
    }
}
