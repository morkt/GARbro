//! \file       Asset.cs
//! \date       Wed Apr 05 18:58:07 2017
//! \brief      Unity asset class.
//
// Based on the [UnityPack](https://github.com/HearthSim/UnityPack)
//
// Copyright (c) Jerome Leclanche
//
// C# port copyright (C) 2017 by morkt
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
//

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using GameRes.Utility;

namespace GameRes.Formats.Unity
{
    internal class Asset
    {
        int                     m_header_size;
        int                     m_format;
        uint                    m_data_offset;
        bool                    m_is_little_endian;
        UnityTypeData           m_tree = new UnityTypeData();
        Dictionary<long, int>   m_adds;
        List<AssetRef>          m_refs;
        Dictionary<int, TypeTree>       m_types = new Dictionary<int, TypeTree>();
        Dictionary<long, UnityObject>   m_objects = new Dictionary<long, UnityObject>();

        public int          Format { get { return m_format; } }
        public bool IsLittleEndian { get { return m_is_little_endian; } }
        public long     DataOffset { get { return m_data_offset; } }
        public UnityTypeData  Tree { get { return m_tree; } }
        public IEnumerable<UnityObject> Objects { get { return m_objects.Values; } }

        public void Load (AssetReader input)
        {
            m_header_size  = input.ReadInt32();
            input.ReadUInt32(); // file_size
            m_format = input.ReadInt32();
            m_data_offset  = input.ReadUInt32();
            if (m_format >= 9)
                m_is_little_endian = 0 == input.ReadInt32();
            input.SetupReaders (m_format, m_is_little_endian);
            m_tree.Load (input);

            bool long_ids = Format >= 14;
            if (Format >= 7 && Format < 14)
                long_ids = 0 != input.ReadInt32();
            input.SetupReadId (long_ids);

            int obj_count = input.ReadInt32();
            for (int i = 0; i < obj_count; ++i)
            {
                input.Align();
                var obj = new UnityObject (this);
                obj.Load (input);
                RegisterObject (obj);
            }
            if (Format >= 11)
            {
                int count = input.ReadInt32();
                m_adds = new Dictionary<long, int> (count);
                for (int i = 0; i < count; ++i)
                {
                    input.Align();
                    var id = input.ReadId();
                    m_adds[id] = input.ReadInt32();
                }
            }
            if (Format >= 6)
            {
                int count = input.ReadInt32();
                m_refs = new List<AssetRef> (count);
                for (int i = 0; i < count; ++i)
                {
                    var asset_ref = AssetRef.Load (input);
                    m_refs.Add (asset_ref);
                }
            }
            input.ReadCString();
        }

        void RegisterObject (UnityObject obj)
        {
            if (m_tree.TypeTrees.ContainsKey (obj.TypeId))
            {
                m_types[obj.TypeId] = m_tree.TypeTrees[obj.TypeId];
            }
            else if (!m_types.ContainsKey (obj.TypeId))
            {
                /*
                var trees = TypeTree.Default (this).TypeTrees;
                if (trees.ContainsKey (obj.ClassId))
                {
                    m_types[obj.TypeId] = trees[obj.ClassId];
                }
                else
                */
                {
                    Trace.WriteLine ("Unknown type id", obj.ClassId.ToString());
                    m_types[obj.TypeId] = null;
                }
                throw new ApplicationException (string.Format ("Unknwon type id {0}", obj.ClassId));
            }
            if (m_objects.ContainsKey (obj.PathId))
                throw new ApplicationException (string.Format ("Duplicate asset object {0} (PathId: {1})", obj, obj.PathId));
            m_objects[obj.PathId] = obj;
        }
    }

    internal class AssetRef
    {
        public string   AssetPath;
        public Guid     Guid;
        public int      Type;
        public string   FilePath;
        public object   Asset;

        public static AssetRef Load (AssetReader reader)
        {
            var r = new AssetRef();
            r.AssetPath = reader.ReadCString();
            r.Guid = new Guid (reader.ReadBytes (16));
            r.Type = reader.ReadInt32();
            r.FilePath = reader.ReadCString();
            r.Asset = null;
            return r;
        }
    }

    internal class UnityObject
    {
        public Asset    Asset;
        public long     PathId;
        public long     Offset;
        public uint     Size;
        public int      TypeId;
        public int      ClassId;
        public bool     IsDestroyed;

        public UnityObject (Asset owner)
        {
            Asset = owner;
        }

        public AssetReader Open (Stream input)
        {
            var stream = new StreamRegion (input, Offset, Size, true);
            var reader = new AssetReader (stream, "");
            reader.SetupReaders (Asset.Format, Asset.IsLittleEndian);
            return reader;
        }

        public void Load (AssetReader reader)
        {
            PathId = reader.ReadId();
            Offset = reader.ReadUInt32() + Asset.DataOffset;
            Size = reader.ReadUInt32();
            if (Asset.Format < 17)
            {
                TypeId = reader.ReadInt32();
                ClassId = reader.ReadInt16();
            }
            else
            {
                var type_id = reader.ReadInt32();
                var class_id = Asset.Tree.ClassIds[type_id];
                TypeId = class_id;
                ClassId = class_id;
            }
            if (Asset.Format <= 10)
                IsDestroyed = reader.ReadInt16() != 0;
            if (Asset.Format >= 11 && Asset.Format < 17)
                reader.ReadInt16();
            if (Asset.Format >= 15 && Asset.Format < 17)
                reader.ReadByte();
        }

        public string Type {
            get {
                var type_tree = Asset.Tree.TypeTrees;
                if (type_tree.ContainsKey (TypeId))
                    return type_tree[TypeId].Type;
                return string.Format ("[TypeId:{0}]", TypeId);
            }
        }

        public override string ToString ()
        {
            return string.Format ("<{0} {1}>", Type, ClassId);
        }
    }

    internal class TypeTree
    {
        int             m_format;
        List<TypeTree>  m_children = new List<TypeTree>();

        public int      Version;
        public bool     IsArray;
        public string   Type;
        public string   Name;
        public int      Size;
        public uint     Index;
        public int      Flags;

        public IList<TypeTree> Children { get { return m_children; } }

        static readonly string          Null = "(null)";
        static readonly Lazy<byte[]>    StringsDat = new Lazy<byte[]> (() => LoadResource ("strings.dat"));

        public TypeTree (int format)
        {
            m_format = format;
        }

        public void Load (AssetReader reader)
        {
            if (10 == m_format || m_format >= 12)
                LoadBlob (reader);
            else
                LoadRaw (reader);
        }

        void LoadRaw (AssetReader reader)
        {
            throw new NotImplementedException();
        }

        byte[] m_data;

        void LoadBlob (AssetReader reader)
        {
            int count = reader.ReadInt32();
            int buffer_bytes = reader.ReadInt32();
            var node_data = reader.ReadBytes (24 * count);
            m_data = reader.ReadBytes (buffer_bytes);

            var parents = new Stack<TypeTree>();
            parents.Push (this);
            using (var buf = new BinMemoryStream (node_data))
            {
                for (int i = 0; i < count; ++i)
                {
                    int version = buf.ReadInt16();
                    int depth = buf.ReadUInt8();
                    TypeTree current;
                    if (0 == depth)
                    {
                        current = this;
                    }
                    else
                    {
                        while (parents.Count > depth)
                            parents.Pop();
                        current = new TypeTree (m_format);
                        parents.Peek().Children.Add (current);
                        parents.Push (current);
                    }
                    current.Version = version;
                    current.IsArray = buf.ReadUInt8() != 0;
                    current.Type = GetString (buf.ReadInt32());
                    current.Name = GetString (buf.ReadInt32());
                    current.Size = buf.ReadInt32();
                    current.Index = buf.ReadUInt32();
                    current.Flags = buf.ReadInt32();
                }
            }
        }

        string GetString (int offset)
        {
            byte[] strings;
            if (offset < 0)
            {
                offset &= 0x7FFFFFFF;
                strings = StringsDat.Value;
            }
            else if (offset < m_data.Length)
                strings = m_data;
            else
                return Null;
            return Binary.GetCString (strings, offset, strings.Length-offset, Encoding.UTF8);
        }

        internal static Stream OpenResource (string name)
        {
            var qualified_name = ".Unity." + name;
            var assembly = Assembly.GetExecutingAssembly();
            var res_name = assembly.GetManifestResourceNames().Single (r => r.EndsWith (qualified_name));
            Stream stream = assembly.GetManifestResourceStream (res_name);
            if (null == stream)
                throw new FileNotFoundException ("Resource not found.", name);
            return stream;
        }

        internal static byte[] LoadResource (string name)
        {
            using (var stream = OpenResource (name))
            {
                var res = new byte[stream.Length];
                stream.Read (res, 0, res.Length);
                return res;
            }
        }
    }

    internal class UnityTypeData
    {
        List<int>                   m_class_ids = new List<int> ();
        Dictionary<int, byte[]>     m_hashes = new Dictionary<int, byte[]> ();
        Dictionary<int, TypeTree>   m_type_trees = new Dictionary<int, TypeTree> ();

        public IList<int>                  ClassIds { get { return m_class_ids; } }
        public IDictionary<int, byte[]>      Hashes { get { return m_hashes; } }
        public IDictionary<int, TypeTree> TypeTrees { get { return m_type_trees; } }

        public void Load (AssetReader reader)
        {
            int format = reader.Format;
            var version = reader.ReadCString();
            var platform = reader.ReadInt32 ();
            if (format >= 13)
            {
                bool has_type_trees = reader.ReadBool ();
                int count = reader.ReadInt32 ();
                for (int i = 0; i < count; ++i)
                {
                    int class_id = reader.ReadInt32 ();
                    if (format >= 17)
                    {
                        reader.ReadByte ();
                        int script_id = reader.ReadInt16 ();
                        if (114 == class_id)
                        {
                            if (script_id >= 0)
                                class_id = -2 - script_id;
                            else
                                class_id = -1;
                        }
                    }
                    m_class_ids.Add (class_id);
                    byte[] hash = reader.ReadBytes (class_id < 0 ? 0x20 : 0x10);
                    m_hashes[class_id] = hash;
                    if (has_type_trees)
                    {
                        var tree = new TypeTree (format);
                        tree.Load (reader);
                        m_type_trees[class_id] = tree;
                    }
                }
            }
            else
            {
                int count = reader.ReadInt32 ();
                for (int i = 0; i < count; ++i)
                {
                    int class_id = reader.ReadInt32 ();
                    var tree = new TypeTree (format);
                    tree.Load (reader);
                    m_type_trees[class_id] = tree;
                }
            }
        }
    }

    internal class AudioClip
    {
        public int      m_LoadType;
        public int      m_Channels;
        public int      m_Frequency;
        public int      m_BitsPerSample;
        public float    m_Length;
        public bool     m_IsTrackerFormat;
        public int      m_SubsoundIndex;
        public bool     m_PreloadAudioData;
        public bool     m_LoadInBackground;
        public bool     m_Legacy3D;
        public string   m_Source;
        public long     m_Offset;
        public long     m_Size;
        public int      m_CompressionFormat;

        public void Load (AssetReader reader)
        {
            var name = reader.ReadString();
            reader.Align();
            m_LoadType = reader.ReadInt32();
            m_Channels = reader.ReadInt32();
            m_Frequency = reader.ReadInt32();
            m_BitsPerSample = reader.ReadInt32();
            m_Length = reader.ReadFloat();
            m_IsTrackerFormat = reader.ReadBool();
            reader.Align();
            m_SubsoundIndex = reader.ReadInt32();
            m_PreloadAudioData = reader.ReadBool();
            m_LoadInBackground = reader.ReadBool();
            m_Legacy3D = reader.ReadBool();
            reader.Align();
            m_Source = reader.ReadString();
            reader.Align();
            m_Offset = reader.ReadInt64();
            m_Size = reader.ReadInt64();
            m_CompressionFormat = reader.ReadInt32();
        }
    }

    enum TextureFormat : int
    {
        Alpha8 = 1,
        ARGB4444 = 2,
        RGB24 = 3,
        RGBA32 = 4,
        ARGB32 = 5,
        R16 = 6, // A 16 bit color texture format that only has a red channel.
        RGB565 = 7,
        DXT1 = 10,
        DXT5 = 12,
        RGBA4444 = 13,
        BGRA32 = 14,
    }

    internal class Texture2D
    {
        public string   m_Name;
        public int      m_Width;
        public int      m_Height;
        public int      m_CompleteImageSize;
        public TextureFormat m_TextureFormat;
        public int      m_MipCount;
        public bool     m_IsReadable;
        public bool     m_ReadAllowed;
        public int      m_ImageCount;
        public int      m_TextureDimension;
        public int      m_FilterMode;
        public int      m_Aniso;
        public int      m_MipBias;
        public int      m_WrapMode;
        public int      m_LightFormat;
        public int      m_ColorSpace;
        // byte[] m_Data
        // StreamingInfo m_StreamData
        // uint offset
        // uint size
        // string path

        public void Load (AssetReader reader)
        {
            m_Name = reader.ReadString();
            reader.Align();
            m_Width = reader.ReadInt32();
            m_Height = reader.ReadInt32();
            m_CompleteImageSize = reader.ReadInt32();
            m_TextureFormat = (TextureFormat)reader.ReadInt32();
            m_MipCount = reader.ReadInt32();
            m_IsReadable = reader.ReadBool();
            m_ReadAllowed = reader.ReadBool();
            reader.Align();
            m_ImageCount = reader.ReadInt32();
            m_TextureDimension = reader.ReadInt32();
            m_FilterMode = reader.ReadInt32();
            m_Aniso = reader.ReadInt32();
            m_MipBias = reader.ReadInt32();
            m_WrapMode = reader.ReadInt32();
            m_LightFormat = reader.ReadInt32();
            m_ColorSpace = reader.ReadInt32();
        }
    }
}
