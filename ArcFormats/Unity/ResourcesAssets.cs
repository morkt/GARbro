//! \file       ResourcesAssets.cs
//! \date       2018 Aug 31
//! \brief      Unity resources assets deserializer.
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

using System.Collections.Generic;
using System.Linq;

namespace GameRes.Formats.Unity
{
    internal class ResourcesAssetsDeserializer
    {
        string                          m_res_name;
        Dictionary<string, BundleEntry> m_bundles;

        public ResourcesAssetsDeserializer (string arc_name)
        {
            m_res_name = arc_name;
        }

        public List<Entry> Parse (AssetReader input, long base_offset = 0)
        {
            var asset = new Asset();
            asset.Load (input);
            var dir = new List<Entry>();
            m_bundles = new Dictionary<string, BundleEntry>();
            var used_names = new HashSet<string>();

            foreach (var obj in asset.Objects.Where (o => o.TypeId > 0))
            {
                input.Position = obj.Offset + base_offset;
                AssetEntry entry = null;
                int id = obj.TypeId > 0 ? obj.TypeId : obj.ClassId;
                switch (id)
                {
                case 48: // Shader
                case 114: // MonoBehaviour
                default:
                    break;

                case 28: // Texture2D
                    {
                        var tex = new Texture2D();
                        tex.Load (input, asset.Tree.Version);
                        if (0 == tex.m_DataLength)
                        {
                            var stream_data = new StreamingInfo();
                            stream_data.Load (input);
                            if (!string.IsNullOrEmpty (stream_data.Path))
                            {
                                entry = new AssetEntry {
                                    Name = tex.m_Name,
                                    Type = "image",
                                    Offset = stream_data.Offset,
                                    Size = stream_data.Size,
                                    Bundle = GetBundle (stream_data.Path),
                                };
                            }
                        }
                        else
                        {
                            entry = new AssetEntry {
                                Name = tex.m_Name,
                                Type = "image",
                                Offset = obj.Offset,
                                Size = obj.Size,
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
                            };
                        }
                        else if (clip.m_Size != 0)
                        {
                            entry = new AssetEntry {
                                Name = clip.m_Name,
                                Type = "audio",
                                Offset = input.Position,
                                Size = (uint)clip.m_Size,
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
                        };
                        if (name.HasAnyOfExtensions ("jpg", "png"))
                            entry.Type = "image";
                        break;
                    }
                case 128: // Font
                    {
                        entry = new AssetEntry {
                            Offset = obj.Offset,
                            Size = obj.Size,
                        };
                        break;
                    }
                }
                if (entry != null)
                {
                    entry.AssetObject = obj;
                    if (string.IsNullOrEmpty (entry.Name))
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
