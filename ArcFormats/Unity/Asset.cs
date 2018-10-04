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
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using GameRes.Utility;

namespace GameRes.Formats.Unity
{
    internal class Asset
    {
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
            input.ReadInt32();  // header_size
            input.ReadUInt32(); // file_size
            m_format = input.ReadInt32();
            m_data_offset  = input.ReadUInt32();
            if (m_format >= 9)
                m_is_little_endian = 0 == input.ReadInt32();
            input.SetupReaders (this);
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
                    Trace.WriteLine (string.Format ("Unknown type id {0}", obj.ClassId.ToString()), "[Unity.Asset]");
                    m_types[obj.TypeId] = null;
                }
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
            reader.SetupReaders (Asset);
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

        public string TypeName {
            get {
                var type = this.Type;
                if (type != null)
                    return type.Type;
                return string.Format ("[TypeId:{0}]", TypeId);
            }
        }

        public TypeTree Type {
            get {
                TypeTree type;
                Asset.Tree.TypeTrees.TryGetValue (TypeId, out type);
                return type;
            }
        }

        public override string ToString ()
        {
            return string.Format ("<{0} {1}>", Type, ClassId);
        }

        public IDictionary Deserialize (AssetReader input)
        {
            var type_tree = Asset.Tree.TypeTrees;
            if (!type_tree.ContainsKey (TypeId))
                return null;
            var type_map = new Hashtable();
            var type = type_tree[TypeId];
            foreach (var node in type.Children)
            {
                type_map[node.Name] = DeserializeType (input, node);
            }
            return type_map;
        }

        object DeserializeType (AssetReader input, TypeTree node)
        {
            object obj = null;
            if (node.IsArray)
            {
                int size = input.ReadInt32();
                var data_field = node.Children.FirstOrDefault (n => n.Name == "data");
                if (data_field != null)
                {
                    if ("TypelessData" == node.Type)
                        obj = input.ReadBytes (size * data_field.Size);
                    else
                        obj = DeserializeArray (input, size, data_field);
                }
            }
            else if (node.Size < 0)
            {
                if (node.Type == "string")
                {
                    obj = input.ReadString();
                    if (node.Children[0].IsAligned)
                        input.Align();
                }
                else if (node.Type == "StreamingInfo")
                {
                    var info = new StreamingInfo();
                    info.Load (input);
                    obj = info;
                }
                else
                    throw new NotImplementedException ("Unknown class encountered in asset deserialzation.");
            }
            else if ("int" == node.Type)
                obj = input.ReadInt32();
            else if ("unsigned int" == node.Type)
                obj = input.ReadUInt32();
            else if ("bool" == node.Type)
                obj = input.ReadBool();
            else
                input.Position += node.Size;
            if (node.IsAligned)
                input.Align();
            return obj;
        }

        object[] DeserializeArray (AssetReader input, int length, TypeTree elem)
        {
            var array = new object[length];
            for (int i = 0; i < length; ++i)
                array[i] = DeserializeType (input, elem);
            return array;
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

        public bool           IsAligned { get { return (Flags & 0x4000) != 0; } }

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
            Type = reader.ReadCString();
            Name = reader.ReadCString();
            Size = reader.ReadInt32();
            Index = reader.ReadUInt32();
            IsArray = reader.ReadInt32() != 0;
            Version = reader.ReadInt32();
            Flags = reader.ReadInt32();
            int count = reader.ReadInt32();
            for (int i = 0; i < count; ++i)
            {
                var child = new TypeTree (m_format);
                child.Load (reader);
                Children.Add (child);
            }
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

        internal static byte[] LoadResource (string name)
        {
            var res = EmbeddedResource.Load (name, typeof(TypeTree));
            if (null == res)
                throw new FileNotFoundException ("Resource not found.", name);
            return res;
        }
    }

    internal class UnityTypeData
    {
        string                      m_version;
        List<int>                   m_class_ids = new List<int> ();
        Dictionary<int, byte[]>     m_hashes = new Dictionary<int, byte[]> ();
        Dictionary<int, TypeTree>   m_type_trees = new Dictionary<int, TypeTree> ();

        public string                       Version { get { return m_version; } }
        public IList<int>                  ClassIds { get { return m_class_ids; } }
        public IDictionary<int, byte[]>      Hashes { get { return m_hashes; } }
        public IDictionary<int, TypeTree> TypeTrees { get { return m_type_trees; } }

        public void Load (AssetReader reader)
        {
            int format = reader.Format;
            m_version = reader.ReadCString();
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
}
