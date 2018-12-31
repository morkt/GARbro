//! \file       ArcPSB.cs
//! \date       Thu Mar 24 01:40:57 2016
//! \brief      E-mote engine image container.
//
// Copyright (C) 2016 by morkt
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
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Text;
using System.Windows.Media;
using GameRes.Utility;

namespace GameRes.Formats.Emote
{
    internal class TexEntry : Entry
    {
        public string   TexType;
        public int      Width;
        public int      Height;
        public int      TruncatedWidth;
        public int      TruncatedHeight;
        public int      OffsetX;
        public int      OffsetY;
    }

    [Serializable]
    public class PsbScheme : ResourceScheme
    {
        public uint[] KnownKeys;
    }

    [Export(typeof(ArchiveFormat))]
    public class PsbOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PSB/EMOTE"; } }
        public override string Description { get { return "E-mote engine texture container"; } }
        public override uint     Signature { get { return 0x425350; } } // 'PSB'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        static uint[] KnownKeys = new uint[] { 970396437u };

        public PsbOpener ()
        {
            Extensions = new string[] { "psb", "pimg", "dpak", "psbz" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            using (var input = file.CreateStream())
            using (var reader = new PsbReader (input))
            {
                foreach (var key in KnownKeys)
                {
                    try
                    {
                        if (reader.Parse (key))
                            return OpenArcFile (reader, file);
                        if (!reader.IsEncrypted)
                            break;
                    }
                    catch { /* ignore parse errors caused by invalid key */ }
                }
                if (reader.ParseNonEncrypted())
                    return OpenArcFile (reader, file);
                return null;
            }
        }

        ArcFile OpenArcFile (PsbReader reader, ArcView file)
        {
            var dir = reader.GetTextures();
            if (null == dir)
                dir = reader.GetLayers();
            if (null == dir)
                dir = reader.GetChunks();
            if (null == dir || 0 == dir.Count)
                return null;
            return new ArcFile (file, this, dir);
        }

        public override IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            var tex = entry as TexEntry;
            if (null == tex)
                return base.OpenImage (arc, entry);
            if ("TLG" == tex.TexType)
                return OpenTlg (arc, tex);
            var info = new PsbTexMetaData
            {
                FullWidth   = tex.Width,
                FullHeight  = tex.Height,
                Width       = (uint)tex.TruncatedWidth,
                Height      = (uint)tex.TruncatedHeight,
                TexType     = tex.TexType,
                BPP = 32
            };
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            return new PsbTextureDecoder (input, info);
        }

        IImageDecoder OpenTlg (ArcFile arc, TexEntry entry)
        {
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            try
            {
                var info = TlgFormat.ReadMetaData (input);
                if (null == info)
                    throw new InvalidFormatException();
                info.OffsetX = entry.OffsetX;
                info.OffsetY = entry.OffsetY;
                return new ImageFormatDecoder (input, TlgFormat, info);
            }
            catch
            {
                input.Dispose();
                throw;
            }
        }

        public override ResourceScheme Scheme
        {
            get { return new PsbScheme { KnownKeys = KnownKeys }; }
            set { KnownKeys = ((PsbScheme)value).KnownKeys; }
        }

        ImageFormat TlgFormat { get { return s_TlgFormat.Value; } }

        static Lazy<ImageFormat> s_TlgFormat = new Lazy<ImageFormat> (() => ImageFormat.FindByTag ("TLG"));
    }

    /// <summary>
    /// PSB container deserialization.
    /// </summary>
    internal sealed class PsbReader : IDisposable
    {
        IBinaryStream       m_input;

        public PsbReader (IBinaryStream input)
        {
            m_input = input;
        }

        public int      Version { get { return m_version; } }
        public bool IsEncrypted { get { return 0 != (m_flags & 3); } }
        public int   DataOffset { get { return m_chunk_data; } }

        public T GetRootKey<T> (string key)
        {
            int obj_offset;
            if (!GetKey (key, m_root, out obj_offset))
                return default(T);
            return (T)GetObject (obj_offset);
        }

        int m_version;
        int m_flags;

        uint[] m_key = new uint[6];
        Dictionary<int, string> m_name_map;

        public bool ParseNonEncrypted ()
        {
            return Parse (false);
        }

        public bool Parse (uint key)
        {
            m_key[0] = 0x075BCD15;
            m_key[1] = 0x159A55E5;
            m_key[2] = 0x1F123BB5;
            m_key[3] = key;
            m_key[4] = 0;
            m_key[5] = 0;

            return Parse (true);
        }

        bool Parse (bool encrypted)
        {
            if (!ReadHeader (encrypted))
                return false;
            if (Version < 2)
                throw new NotSupportedException ("Not supported PSB version");
            m_name_map = ReadNames();
#if DEBUG
            var dict = GetDict (m_root); // returns all metadata in a single dictionary
#endif
            return true;
        }

        public List<Entry> GetLayers ()
        {
            var layers = GetRootKey<IList> ("layers");
            if (null == layers || 0 == layers.Count)
                return null;
            var dir = new List<Entry> (layers.Count);
            foreach (IDictionary layer in layers)
            {
                var name = layer["layer_id"].ToString() + ".tlg";
                var layer_data = GetRootKey<EmChunk> (name);
                if (null == layer_data)
                    continue;
                var entry = new TexEntry {
                    Name        = name,
                    Type        = "image",
                    Offset      = DataOffset + layer_data.Offset,
                    Size        = (uint)layer_data.Length,
                    TexType     = "TLG",
                    OffsetX     = Convert.ToInt32 (layer["left"]),
                    OffsetY     = Convert.ToInt32 (layer["top"]),
                    Width       = Convert.ToInt32 (layer["width"]),
                    Height      = Convert.ToInt32 (layer["height"]),
                };
                dir.Add (entry);
            }
            if (0 == dir.Count)
                return null;
            return dir;
        }

        public List<Entry> GetTextures ()
        {
            var source = GetRootKey<IDictionary> ("source");
            if (null == source || 0 == source.Count)
                return null;
            var dir = new List<Entry> (source.Count);
            foreach (DictionaryEntry item in source)
            {
                var item_value = item.Value as IDictionary;
                if (null == item_value)
                    continue;
                if (item_value.Contains ("texture"))
                {
                    AddTextureEntry (dir, item.Key, item_value["texture"] as IDictionary);
                }
                else if (item_value.Contains ("icon"))
                {
                    AddIconEntry (dir, item.Key, item_value["icon"] as IDictionary);
                }
            }
            return dir;
        }

        public List<Entry> GetChunks ()
        {
            var dict = GetDict (m_root);
            if (0 == dict.Count)
                return null;
            var dir = new List<Entry> (dict.Count);
            foreach (DictionaryEntry item in dict)
            {
                var name = item.Key.ToString();
                var data = item.Value as EmChunk;
                if (string.IsNullOrEmpty (name) || null == data)
                    continue;
                var entry = new Entry {
                    Name   = name,
                    Type   = FormatCatalog.Instance.GetTypeFromName (name),
                    Offset = DataOffset + data.Offset,
                    Size   = (uint)data.Length,
                };
                dir.Add (entry);
            }
            if (0 == dir.Count)
                return null;
            return dir;
        }

        void AddTextureEntry (List<Entry> dir, object name, IDictionary texture)
        {
            if (null == texture)
                return;
            var pixel = texture["pixel"] as EmChunk;
            if (null == pixel)
                return;
            var entry = new TexEntry {
                Name            = name.ToString(),
                Type            = "image",
                Offset          = DataOffset + pixel.Offset,
                Size            = (uint)pixel.Length,
                TexType         = texture["type"].ToString(),
                Width           = Convert.ToInt32 (texture["width"]),
                Height          = Convert.ToInt32 (texture["height"]),
                TruncatedWidth  = Convert.ToInt32 (texture["truncated_width"]),
                TruncatedHeight = Convert.ToInt32 (texture["truncated_height"]),
            };
            dir.Add (entry);
        }

        void AddIconEntry (List<Entry> dir, object name, IDictionary icon_list)
        {
            if (null == icon_list)
                return;
            foreach (DictionaryEntry icon in icon_list)
            {
                var layer = icon.Value as IDictionary;
                var pixel = layer["pixel"] as EmChunk;
                if (null == pixel)
                    continue;
                var entry = new TexEntry {
                    Name        = name.ToString()+'#'+icon.Key.ToString(),
                    Type        = "image",
                    Offset      = DataOffset + pixel.Offset,
                    Size        = (uint)pixel.Length,
                    Width       = Convert.ToInt32 (layer["width"]),
                    Height      = Convert.ToInt32 (layer["height"]),
                    OffsetX     = Convert.ToInt32 (layer["originX"]),
                    OffsetY     = Convert.ToInt32 (layer["originY"]),
                    TexType     = layer.Contains ("compress") ? layer["compress"].ToString() : "RGBA8",
                };
                entry.TruncatedWidth = entry.Width;
                entry.TruncatedHeight = entry.Height;
                dir.Add (entry);
            }
        }

        int m_names;
        int m_strings;
        int m_strings_data;
        int m_chunk_offsets;
        int m_chunk_lengths;
        int m_chunk_data;
        int m_extra_offsets;
        int m_extra_lengths;
        int m_extra_data;
        int m_root;
        byte[] m_data;

        bool ReadHeader (bool encrypted)
        {
            m_input.Position = 4;
            
            m_version = m_input.ReadUInt16();
            m_flags = m_input.ReadUInt16();
            if (encrypted && m_version < 3)
                m_flags = 2;

            int header_size = m_version > 3 ? 0x30 : 0x20;
            var header = m_input.ReadBytes (header_size);
            if (encrypted && 0 != (m_flags & 1))
            {
                if (m_version > 3)
                {
                    Decrypt (header, 0, 0x24);
                    Decrypt (header, 0x24, 0xC);
                }
                else
                    Decrypt (header, 0, 0x20);
            }

            m_names         = LittleEndian.ToInt32 (header, 0x04); // 0x08
            m_strings       = LittleEndian.ToInt32 (header, 0x08); // 0x0C
            m_strings_data  = LittleEndian.ToInt32 (header, 0x0C); // 0x10
            m_chunk_offsets = LittleEndian.ToInt32 (header, 0x10); // 0x14
            m_chunk_lengths = LittleEndian.ToInt32 (header, 0x14); // 0x18
            m_chunk_data    = LittleEndian.ToInt32 (header, 0x18); // 0x1C
            m_root          = LittleEndian.ToInt32 (header, 0x1C); // 0x20

            if (m_version > 3)
            {
                m_extra_offsets = LittleEndian.ToInt32 (header, 0x24);
                m_extra_lengths = LittleEndian.ToInt32 (header, 0x28);
                m_extra_data    = LittleEndian.ToInt32 (header, 0x2C);
            }

            int buffer_length = (int)m_input.Length;
            if (!(m_names           >= 0x28 && m_names < m_chunk_data
                  && m_strings      >= 0x28 && m_strings < m_chunk_data
                  && m_strings_data >= 0x28 && m_strings_data < m_chunk_data
                  && m_chunk_offsets >= 0x28 && m_chunk_offsets < m_chunk_data
                  && m_chunk_lengths >= 0x28 && m_chunk_lengths < m_chunk_data
                  && m_chunk_data   >= 0x28 && m_chunk_data <= buffer_length
                  && m_root         >= 0x28 && m_root < m_chunk_data))
                return false;

            if (null == m_data || m_data.Length < m_chunk_data)
                m_data = new byte[m_chunk_data];
            int data_pos = (int)m_input.Position;
            m_input.Read (m_data, data_pos, m_chunk_data-data_pos);
            if (encrypted && 0 != (m_flags & 2))
                Decrypt (m_data, m_names, m_chunk_offsets-m_names);
            // root object is a dictionary
            return 0x21 == m_data[m_root];
        }

        bool GetKey (string name, int dict_offset, out int value_offset)
        {
            value_offset = 0;
            int offset;
            if (!GetOffset (name, out offset))
                return false;
            var keys = GetArray (++dict_offset);
            if (0 == keys.Count)
                return false;

            int upper_bound = keys.Count;
            int lower_bound = 0;
            int key_index = 0;
            while (lower_bound < upper_bound)
            {
                key_index = (upper_bound + lower_bound) >> 1;
                int key = GetArrayElem (keys, key_index);
                if (key == offset)
                    break;
                if (key >= offset)
                    upper_bound = (upper_bound + lower_bound) >> 1;
                else
                    lower_bound = key_index + 1;
            }
            if (lower_bound >= upper_bound)
                return false;

            var values = GetArray (dict_offset + keys.ArraySize);
            int data_offset = GetArrayElem (values, key_index);
            value_offset = dict_offset + keys.ArraySize + values.ArraySize + data_offset;
            return true;
        }

        bool GetOffset (string name, out int offset)
        {
            // FIXME works for ASCII names only.
            var nm1 = GetArray (m_names);
            var nm2 = GetArray (m_names + nm1.ArraySize);
            int i = 0;
            for (int name_idx = 0; ; ++name_idx)
            {
                char symbol = name_idx < name.Length ? name[name_idx] : '\0';
                int prev_i = i;
                i = symbol + GetArrayElem (nm1, i);
                if (i >= nm1.Count || GetArrayElem (nm2, i) != prev_i)
                    break;

                if (name_idx >= name.Length)
                {
                    offset = GetArrayElem (nm1, i);
                    return true;
                }
            }
            offset = 0;
            return false;
        }

        Dictionary<int, string> ReadNames ()
        {
            // this implementation is utterly inefficient. FIXME
            var lookup = new Dictionary<int, byte[]>();
            var next_lookup = new Dictionary<int, byte[]>();
            var dict = new Dictionary<int, string>();
            var nm1 = GetArray (m_names);
            var nm2 = GetArray (m_names + nm1.ArraySize);
            lookup[0] = new byte[0];
            while (lookup.Count > 0)
            {
                foreach (var item in lookup)
                {
                    int first = GetArrayElem (nm1, item.Key);
                    for (int i = 0; i < 256 && i + first < nm2.Count; ++i)
                    {
                        if (GetArrayElem (nm2, i + first) == item.Key)
                        {
                            if (0 == i)
                                dict[GetArrayElem (nm1, i + first)] = Encoding.UTF8.GetString (item.Value);
                            else
                                next_lookup[i+first] = ArrayAppend (item.Value, (byte)i);
                        }
                    }
                }
                var tmp = lookup;
                lookup = next_lookup;
                next_lookup = tmp;
                next_lookup.Clear();
            }
            return dict;
        }

        static byte[] ArrayAppend (byte[] array, byte n)
        {
            var new_array = new byte[array.Length+1];
            Buffer.BlockCopy (array, 0, new_array, 0, array.Length);
            new_array[array.Length] = n;
            return new_array;
        }

        EmArray GetArray (int offset)
        {
            int data_offset = m_data[offset] - 10;
            var array = new EmArray {
                Count = GetInteger (offset, 0xC),
                ElemSize = m_data[offset + data_offset - 1] - 12,
                DataOffset = offset + data_offset,
            };
            array.ArraySize = array.Count * array.ElemSize + data_offset;
            return array;
        }

        int GetArrayElem (EmArray a1, int index)
        {
            int offset = index * a1.ElemSize;
            switch (a1.ElemSize)
            {
            case 1:
                return m_data[a1.DataOffset + offset];
            case 2:
                return LittleEndian.ToUInt16 (m_data, a1.DataOffset + offset);
            case 3:
                return LittleEndian.ToUInt16 (m_data, a1.DataOffset + offset) | m_data[a1.DataOffset + offset + 2] << 16;
            case 4:
                return LittleEndian.ToInt32 (m_data, a1.DataOffset + offset);
            default:
                throw new InvalidFormatException ("Invalid PSB array structure");
            }
        }

        object GetObject (int offset)
        {
            switch (m_data[offset])
            {
            case 1: return null;
            case 2: return true;
            case 3: return false;

            case 4:
            case 5:
            case 6:
            case 7:
            case 8: return GetInteger (offset, 4);

            case 9:
            case 0x0A:
            case 0x0B:
            case 0x0C: return GetLong (offset);

            case 0x15:
            case 0x16:
            case 0x17:
            case 0x18: return GetString (offset);

            case 0x19:
            case 0x1A:
            case 0x1B:
            case 0x1C: return GetChunk (offset);

            case 0x1D:
            case 0x1E: return GetFloat (offset);
            case 0x1F: return GetDouble (offset);
            case 0x20: return GetList (offset);
            case 0x21: return GetDict (offset);

            case 0x22:
            case 0x23:
            case 0x24:
            case 0x25: return GetExtraChunk (offset);
            default:
                throw new InvalidFormatException (string.Format ("Unknown serialized object type 0x{0:X2}", m_data[offset]));
            }
        }

        int GetInteger (int offset, int base_type)
        {
            switch (m_data[offset] - base_type)
            {
            case 1: return m_data[offset+1];
            case 2: return LittleEndian.ToUInt16 (m_data, offset+1);
            case 3: return LittleEndian.ToUInt16 (m_data, offset+1) | m_data[offset+3] << 16;
            case 4: return LittleEndian.ToInt32 (m_data, offset+1);
            default: return 0;
            }
        }

        float GetFloat (int offset)
        {
            if (0x1E == m_data[offset])
                return BitConverter.ToSingle (m_data, offset+1); // FIXME endianness
            else
                return 0.0f;
        }

        double GetDouble (int offset)
        {
            if (0x1F == m_data[offset])
                return BitConverter.ToDouble (m_data, offset+1); // FIXME endianness
            else
                return 0.0;
        }

        long GetLong (int offset)
        {
            switch (m_data[offset])
            {
            case 0x09:  return LittleEndian.ToUInt32 (m_data, offset+1) | (long)(sbyte)m_data[offset+5] << 32;
            case 0x0A:  return LittleEndian.ToUInt32 (m_data, offset+1)
                               | (long)LittleEndian.ToInt16 (m_data, offset+5) << 32;
            case 0x0B:  return LittleEndian.ToUInt32 (m_data, offset+1)
                               | (long)LittleEndian.ToUInt16 (m_data, offset+5) << 32
                               | (long)(sbyte)m_data[offset+6] << 48;
            case 0x0C:  return LittleEndian.ToInt64 (m_data, offset+1);
            default:    return 0L;
            }
        }

        string GetString (int obj_offset)
        {
            int index = GetInteger (obj_offset, 0x14);
            var array = GetArray (m_strings);
            int data_offset = m_strings_data + GetArrayElem (array, index);
            return Binary.GetCString (m_data, data_offset, m_data.Length-data_offset, Encoding.UTF8);
        }

        IList GetList (int offset)
        {
            var array = GetArray (++offset);
            var list = new ArrayList (array.Count);
            for (int i = 0; i < array.Count; ++i)
            {
                int item_offset = offset + array.ArraySize + GetArrayElem (array, i);
                var item = GetObject (item_offset);
                list.Add (item);
            }
            return list;
        }

        IDictionary GetDict (int offset)
        {
            var keys = GetArray (++offset);
            if (0 == keys.Count)
                return new Dictionary<string, object>();
            var values = GetArray (offset + keys.ArraySize);
            var dict = new Dictionary<string, object> (keys.Count);
            for (int i = 0; i < keys.Count; ++i)
            {
                int key = GetArrayElem (keys, i);
                var value_offset = GetArrayElem (values, i);
                string key_name = m_name_map[key];
                dict[key_name] = GetObject (offset + value_offset + keys.ArraySize + values.ArraySize);
            }
            return dict;
        }

        EmChunk GetChunk (int offset)
        {
            var chunk_index = GetInteger (offset, 0x18);
            var chunks = GetArray (m_chunk_offsets);
            if (chunk_index >= chunks.Count)
                throw new InvalidFormatException ("Invalid chunk index");
            var lengths = GetArray (m_chunk_lengths);
            return new EmChunk {
                Offset = GetArrayElem (chunks, chunk_index),
                Length = GetArrayElem (lengths, chunk_index),
            };
        }

        EmChunk GetExtraChunk (int offset)
        {
            var chunk_index = GetInteger (offset, 0x21);
            var chunks = GetArray (m_extra_offsets);
            if (chunk_index >= chunks.Count)
                throw new InvalidFormatException ("Invalid chunk index");
            var lengths = GetArray (m_extra_lengths);
            return new EmChunk {
                Offset = GetArrayElem (chunks, chunk_index),
                Length = GetArrayElem (lengths, chunk_index),
            };
        }

        void Decrypt (byte[] data, int offset, int length)
        {
            for (int i = 0; i < length; ++i)
            {
                if (0 == m_key[4])
                {
                    var v5 = m_key[3];
                    var v6 = m_key[0] ^ (m_key[0] << 11);
                    m_key[0] = m_key[1];
                    m_key[1] = m_key[2];
                    var eax = v6 ^ v5 ^ ((v6 ^ (v5 >> 11)) >> 8);
                    m_key[2] = v5;
                    m_key[3] = eax;
                    m_key[4] = eax;
                }
                data[offset+i] ^= (byte)m_key[4];
                m_key[4] >>= 8;
            }
        }

        internal class EmArray
        {
            public int  ArraySize;
            public int  Count;
            public int  ElemSize;
            public int  DataOffset;
        }

        internal class EmChunk
        {
            public int  Offset;
            public int  Length;
        }

        #region IDisposable Members
        public void Dispose ()
        {
        }
        #endregion
    }

    internal class PsbTexMetaData : ImageMetaData
    {
        public string   TexType;
        public int      FullWidth;
        public int      FullHeight;
    }

    /// <summary>
    /// Artificial format representing PSB texture.
    /// </summary>
    internal sealed class PsbTextureDecoder : BinaryImageDecoder
    {
        PsbTexMetaData      m_info;

        public PsbTextureDecoder (IBinaryStream input, PsbTexMetaData info) : base (input, info)
        {
            m_info = info;
        }

        protected override ImageData GetImageData ()
        {
            int stride = (int)m_info.Width * 4;
            var pixels = new byte[stride * (int)m_info.Height];
            if ("RGBA8" == m_info.TexType)
                ReadRgba8 (pixels, stride);
            else if ("L8" == m_info.TexType)
                ReadL8 (pixels, stride);
            else if ("A8L8" == m_info.TexType)
                ReadA8L8 (pixels, stride);
            else if ("RGBA4444" == m_info.TexType)
                ReadRgba4444 (pixels, stride);
            else if ("RL" == m_info.TexType)
                ReadRle (pixels, stride);
            else if ("DXT5" == m_info.TexType)
                pixels = ReadDxt5();
            else
                throw new NotImplementedException (string.Format ("PSB texture format '{0}' not implemented", m_info.TexType));
            return ImageData.Create (m_info, PixelFormats.Bgra32, null, pixels, stride);
        }

        void ReadRgba8 (byte[] output, int dst_stride)
        {
            long next_row = 0;
            int src_stride = m_info.FullWidth * 4;
            int dst = 0;
            for (uint i = 0; i < m_info.Height; ++i)
            {
                m_input.Position = next_row;
                m_input.Read (output, dst, dst_stride);
                dst += dst_stride;
                next_row += src_stride;
            }
        }

        void ReadL8 (byte[] output, int dst_stride)
        {
            int src_stride = m_info.FullWidth;
            int dst = 0;
            var row = new byte[src_stride];
            m_input.Position = 0;
            for (uint i = 0; i < m_info.Height; ++i)
            {
                m_input.Read (row, 0, src_stride);
                int src = 0;
                for (int x = 0; x < dst_stride; x += 4)
                {
                    byte c = row[src++];
                    output[dst++] = c;
                    output[dst++] = c;
                    output[dst++] = c;
                    output[dst++] = 0xFF;
                }
            }
        }

        void ReadA8L8 (byte[] output, int dst_stride)
        {
            int src_stride = m_info.FullWidth * 2;
            int dst = 0;
            var row = new byte[src_stride];
            m_input.Position = 0;
            for (uint i = 0; i < m_info.Height; ++i)
            {
                m_input.Read (row, 0, src_stride);
                int src = 0;
                for (int x = 0; x < dst_stride; x += 4)
                {
                    byte c = row[src++];
                    byte a = row[src++];
                    output[dst++] = c;
                    output[dst++] = c;
                    output[dst++] = c;
                    output[dst++] = a;
                }
            }
        }

        void ReadRgba4444 (byte[] output, int dst_stride)
        {
            int src_stride = m_info.FullWidth * 2;
            int dst = 0;
            var row = new byte[src_stride];
            m_input.Position = 0;
            for (uint i = 0; i < m_info.Height; ++i)
            {
                m_input.Read (row, 0, src_stride);
                int src = 0;
                for (int x = 0; x < dst_stride; x += 4)
                {
                    uint p = LittleEndian.ToUInt16 (row, src);
                    src += 2;
                    output[dst++] = (byte)((p & 0x000Fu) * 0xFFu / 0x000Fu);
                    output[dst++] = (byte)((p & 0x00F0u) * 0xFFu / 0x00F0u);
                    output[dst++] = (byte)((p & 0x0F00u) * 0xFFu / 0x0F00u);
                    output[dst++] = (byte)((p & 0xF000u) * 0xFFu / 0xF000u);
                }
            }
        }

        void ReadRle (byte[] output, int dst_stride)
        {
            const int pixel_size = 4;
            m_input.Position = 0;
            int dst = 0;
            while (dst < output.Length)
            {
                int count = m_input.ReadUInt8();
                if (0 == (count & 0x80))
                {
                    count = pixel_size * (count + 1);
                    dst += m_input.Read (output, dst, count);
                }
                else
                {
                    count = pixel_size * ((count & 0x7F) + 3);
                    m_input.Read (output, dst, pixel_size);
                    Binary.CopyOverlapped (output, dst, dst+pixel_size, count-pixel_size);
                    dst += count;
                }
            }
        }

        byte[] ReadDxt5 ()
        {
            var packed = m_input.ReadBytes ((int)m_input.Length);
            var dxt = new DirectDraw.DxtDecoder (packed, m_info);
            return dxt.UnpackDXT5();
        }
    }
}
