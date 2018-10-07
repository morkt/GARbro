//! \file       ArcRPA.cs
//! \date       Sat Aug 16 05:26:13 2014
//! \brief      Ren'Py game engine archive implementation.
//
// Copyright (C) 2014 by morkt
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
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.IO;
using System.Text;
using GameRes.Compression;
using GameRes.Formats.Strings;

namespace GameRes.Formats.RenPy
{
    internal class RpaEntry : PackedEntry
    {
        public byte[] Header = null;
    }

    public class RpaOptions : ResourceOptions
    {
        public uint Key;
    }

    [Export(typeof(ArchiveFormat))]
    public class RpaOpener : ArchiveFormat
    {
        public override string         Tag { get { return "RPA"; } }
        public override string Description { get { return Strings.arcStrings.RPADescription; } }
        public override uint     Signature { get { return 0x2d415052; } } // "RPA-"
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return true; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (0x20302e33 != file.View.ReadUInt32 (4))
                return null;
            string index_offset_str = file.View.ReadString (8, 16, Encoding.ASCII);
            long index_offset;
            if (!long.TryParse (index_offset_str, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out index_offset))
                return null;
            if (index_offset >= file.MaxOffset)
                return null;
            uint key;
            string key_str = file.View.ReadString (0x19, 8, Encoding.ASCII);
            if (!uint.TryParse (key_str, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out key))
                return null;

            IDictionary dict = null;
            using (var index = new ZLibStream (file.CreateStream (index_offset), CompressionMode.Decompress))
            {
                var pickle = new Pickle (index);
                dict = pickle.Load() as IDictionary;
            }
            if (null == dict)
                return null;
            var dir = new List<Entry> (dict.Count);
            foreach (DictionaryEntry item in dict)
            {
                var name_raw = item.Key as byte[];
                var values = item.Value as IList;
                if (null == name_raw || null == values || values.Count < 1)
                {
                    Trace.WriteLine ("invalid index entry", "RpaOpener.TryOpen");
                    return null;
                }
                string name = Encoding.UTF8.GetString (name_raw);
                if (string.IsNullOrEmpty (name))
                    return null;
                var tuple = values[0] as IList;
                if (null == tuple || tuple.Count < 2)
                {
                    Trace.WriteLine ("invalid index tuple", "RpaOpener.TryOpen");
                    return null;
                }
                var entry = FormatCatalog.Instance.Create<RpaEntry> (name);
                entry.Offset       = (long)(Convert.ToInt64 (tuple[0]) ^ key);
                entry.UnpackedSize = (uint)(Convert.ToInt64 (tuple[1]) ^ key);
                entry.Size         = entry.UnpackedSize;
                if (tuple.Count > 2)
                {
                    entry.Header = tuple[2] as byte[];
                    if (null != entry.Header && entry.Header.Length > 0)
                    {
                        entry.Size -= (uint)entry.Header.Length;
                        entry.IsPacked = true;
                    }
                }
                dir.Add (entry);
            }
            if (dir.Count > 0)
                Trace.TraceInformation ("[{0}] [{1:X8}] [{2}]", dir[0].Name, dir[0].Offset, dir[0].Size);
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            Stream input;
            if (0 != entry.Size)
                input = arc.File.CreateStream (entry.Offset, entry.Size);
            else
                input = Stream.Null;
            var rpa_entry = entry as RpaEntry;
            if (null == rpa_entry || null == rpa_entry.Header || 0 == rpa_entry.Header.Length)
                return input;
            return new PrefixStream (rpa_entry.Header, input);
        }

        public override ResourceOptions GetDefaultOptions ()
        {
            return new RpaOptions { Key = Properties.Settings.Default.RPAKey };
        }

        public override object GetCreationWidget ()
        {
            return new GUI.CreateRPAWidget();
        }

        public override void Create (Stream output, IEnumerable<Entry> list, ResourceOptions options,
                                     EntryCallback callback)
        {
            var rpa_options = GetOptions<RpaOptions> (options);
            int callback_count = 0;
            var file_table = new Dictionary<PyString, ArrayList>();
            long data_offset = 0x22;
            output.Position = data_offset;
            foreach (var entry in list)
            {
                if (null != callback)
                    callback (callback_count++, entry, arcStrings.MsgAddingFile);

                string name = entry.Name.Replace (@"\", "/");
                var rpa_entry = new RpaEntry { Name = name };
                using (var file = File.OpenRead (entry.Name))
                {
                    var size = file.Length;
                    if (size > uint.MaxValue)
                        throw new FileSizeException();
                    int header_size = (int)Math.Min (size, 0x10);
                    rpa_entry.Offset        = output.Position ^ rpa_options.Key;
                    rpa_entry.Header        = new byte[header_size];
                    rpa_entry.UnpackedSize  = (uint)size ^ rpa_options.Key;
                    rpa_entry.Size          = (uint)(size - header_size);
                    file.Read (rpa_entry.Header, 0, header_size);
                    file.CopyTo (output);
                }
                var py_name = new PyString (name);
                if (file_table.ContainsKey (py_name))
                    file_table[py_name].Add (rpa_entry);
                else
                    file_table[py_name] = new ArrayList { rpa_entry };
            }
            long index_pos = output.Position;
            string signature = string.Format (CultureInfo.InvariantCulture, "RPA-3.0 {0:x16} {1:x8}\n",
                                              index_pos, rpa_options.Key);
            var header = Encoding.ASCII.GetBytes (signature);
            if (header.Length > data_offset)
                throw new ApplicationException ("Signature serialization failed.");

            if (null != callback)
                callback (callback_count++, null, arcStrings.MsgWritingIndex);

            using (var index = new ZLibStream (output, CompressionMode.Compress, CompressionLevel.Level9, true))
            {
                var pickle = new Pickle (index);
                if (!pickle.Dump (file_table))
                    throw new ApplicationException ("Archive index serialization failed.");
            }
            output.Position = 0;
            output.Write (header, 0, header.Length);
        }
    }

    public class Pickle
    {
        Stream          m_stream;

        ArrayList       m_stack = new ArrayList();
        Stack<int>      m_marks = new Stack<int>();

        const int HIGHEST_PROTOCOL  = 2;
        const int BATCHSIZE         = 1000;
        const byte PROTO            = 0x80; /* identify pickle protocol */
        const byte TUPLE2           = 0x86; /* build 2-tuple from two topmost stack items */
        const byte TUPLE3           = 0x87; /* build 3-tuple from three topmost stack items */
        const byte LONG1            = 0x8A; /* push long from < 256 bytes */
        const byte LONG4            = 0x8B; /* push really big long */
        const byte MARK             = (byte)'(';
        const byte STOP             = (byte)'.';
        const byte INT              = (byte)'I';
        const byte BININT           = (byte)'J';
        const byte BININT1          = (byte)'K';
        const byte BININT2          = (byte)'M';
        const byte BINSTRING        = (byte)'T';
        const byte SHORT_BINSTRING  = (byte)'U';
        const byte BINUNICODE       = (byte)'X';
        const byte EMPTY_LIST       = (byte)']';
        const byte APPEND           = (byte)'a';
        const byte APPENDS          = (byte)'e';
        const byte BINPUT           = (byte)'q';
        const byte LONG_BINPUT      = (byte)'r';
        const byte SETITEM          = (byte)'s';
        const byte TUPLE            = (byte)'t';
        const byte SETITEMS         = (byte)'u';
        const byte EMPTY_DICT       = (byte)'}';

        public Pickle (Stream stream)
        {
            m_stream = stream;
        }

        public bool Dump (object obj)
        {
            m_stream.WriteByte (PROTO);
            m_stream.WriteByte ((byte)HIGHEST_PROTOCOL);
            if (!Save (obj))
                return false;
            m_stream.WriteByte (STOP);
            return true;
        }

        bool Save (object obj)
        {
            if (null == obj)
            {
                Trace.WriteLine ("Null reference not serialized", "Pickle.Save");
                return false;
            }
            switch (Type.GetTypeCode (obj.GetType()))
            {
            case TypeCode.Byte:     return SaveInt ((uint)(byte)obj);
            case TypeCode.SByte:    return SaveInt ((uint)(sbyte)obj);
            case TypeCode.UInt16:   return SaveInt ((uint)(ushort)obj);
            case TypeCode.Int16:    return SaveInt ((uint)(short)obj);
            case TypeCode.Int32:    return SaveInt ((uint)(int)obj);
            case TypeCode.UInt32:   return SaveInt ((uint)obj);
            case TypeCode.Int64:    return SaveLong ((long)obj);
            case TypeCode.UInt64:   return SaveLong ((long)(ulong)obj);
            case TypeCode.Object:   break;
            default:
                Trace.WriteLine (obj, "Object could not be serialized");
                return false;
            }
            if (obj is RpaEntry)
                return SaveEntry (obj as RpaEntry);
            if (obj is PyString)
                return SaveString (obj as PyString);
            if (obj is byte[])
                return SaveString (obj as byte[]);
            if (obj is IDictionary)
                return SaveDict (obj as IDictionary);
            if (obj is IList)
                return SaveList (obj as IList);

            Trace.WriteLine (obj, "Object could not be serialized");
            return false;
        }

        bool SaveString (byte[] str)
        {
            int size = str.Length;
            if (size < 256)
            {
                m_stream.WriteByte (SHORT_BINSTRING);
                m_stream.WriteByte ((byte)size);
            }
            else
            {
                m_stream.WriteByte (BINSTRING);
                PutInt (size);
            }
            m_stream.Write (str, 0, size);
            return true;
        }

        bool SaveString (PyString str)
        {
            if (str.IsAscii)
                return SaveString (str.Bytes);
            m_stream.WriteByte (BINUNICODE);
            PutInt (str.Length);
            m_stream.Write (str.Bytes, 0, str.Length);
            return true;
        }

        bool SaveEntry (RpaEntry entry)
        {
            byte opcode = null == entry.Header ? TUPLE2 : TUPLE3;
            SaveLong (entry.Offset);
            SaveInt (entry.UnpackedSize);
            if (null != entry.Header)
                SaveString (entry.Header);
            m_stream.WriteByte (opcode);
            return true;
        }

        bool SaveList (IList list)
        {
            m_stream.WriteByte (EMPTY_LIST);
            if (0 == list.Count)
                return true;
            return BatchList (list.GetEnumerator());
        }

        bool BatchList (IEnumerator iterator)
        {
            int n = 0;
            do
            {
                if (!iterator.MoveNext())
                    break;
                var first_item = iterator.Current;
                if (!iterator.MoveNext())
                {
                    if (!Save (first_item))
                        return false;
                    m_stream.WriteByte (APPEND);
                    break;
                }
                m_stream.WriteByte (MARK);
                if (!Save (first_item))
                    return false;
                n = 1;
                do
                {
                    if (!Save (iterator.Current))
                        return false;
                    if (++n == BATCHSIZE)
                        break;
                }
                while (iterator.MoveNext());
                m_stream.WriteByte (APPENDS);
            }
            while (n == BATCHSIZE);
            return true;
        }

        bool SaveInt (uint i)
        {
            byte[] buf = new byte[5];
            buf[1] = (byte)( i        & 0xff);
            buf[2] = (byte)((i >> 8)  & 0xff);
            buf[3] = (byte)((i >> 16) & 0xff);
            buf[4] = (byte)((i >> 24) & 0xff);
            int length;
            if (0 == buf[4] && 0 == buf[3])
            {
                if (0 == buf[2])
                {
                    buf[0] = BININT1;
                    length = 2;
                }
                else
                {
                    buf[0] = BININT2;
                    length = 3;
                }
            }
            else
            {
                buf[0] = BININT;
                length = 5;
            }
            m_stream.Write (buf, 0, length);
            return true;
        }

        bool SaveLong (long l)
        {
            if (0 == ((l >> 32) & 0xffffffff))
                return SaveInt ((uint)l);
            m_stream.WriteByte (INT);
            string num = l.ToString (CultureInfo.InvariantCulture);
            var num_data = Encoding.ASCII.GetBytes (num);
            m_stream.Write (num_data, 0, num_data.Length);
            m_stream.WriteByte (0x0a);
            return true;
        }

        bool SaveDict (IDictionary dict)
        {
            m_stream.WriteByte (EMPTY_DICT);
            if (0 == dict.Count)
                return true;
            return BatchDict (dict);
        }

        bool BatchDict (IDictionary dict)
        {
            int dict_size = dict.Count;
            var iterator = dict.GetEnumerator();
            if (1 == dict_size)
            {
                if (!iterator.MoveNext())
                    return false;
                if (!Save (iterator.Key))
                    return false;
                if (!Save (iterator.Value))
                    return false;
                m_stream.WriteByte (SETITEM);
                return true;
            }
            int i;
            do
            {
                i = 0;
                m_stream.WriteByte (MARK);
                while (iterator.MoveNext())
                {
                    if (!Save (iterator.Key))
                        return false;
                    if (!Save (iterator.Value))
                        return false;
                    if (++i == BATCHSIZE)
                        break;
                }
                m_stream.WriteByte (SETITEMS);
            }
            while (i == BATCHSIZE);
            return true;
        }

        bool PutInt (int i)
        {
            m_stream.WriteByte ((byte)(i & 0xff));
            m_stream.WriteByte ((byte)((i >> 8) & 0xff));
            m_stream.WriteByte ((byte)((i >> 16) & 0xff));
            m_stream.WriteByte ((byte)((i >> 24) & 0xff));
            return true;
        }

        public object Load ()
        {
            for (;;)
            {
                int sym = m_stream.ReadByte();
                switch (sym)
                {
                case PROTO:
                    if (!LoadProto())
                        break;
                    continue;

                case EMPTY_DICT:
                    if (!LoadEmptyDict())
                        break;
                    continue;

                case BINPUT:
                    if (!LoadBinPut())
                        break;
                    continue;

                case LONG_BINPUT:
                    if (!LoadLongBinPut())
                        break;
                    continue;

                case MARK:
                    if (!LoadMark())
                        break;
                    continue;

                case SHORT_BINSTRING:
                    if (!LoadShortBinstring())
                        break;
                    continue;

                case BINSTRING:
                case BINUNICODE:
                    if (!LoadBinUnicode())
                        break;
                    continue;

                case EMPTY_LIST:
                    if (!LoadEmptyList())
                        break;
                    continue;

                case BININT:
                    if (!LoadBinInt (4))
                        break;
                    continue;

                case BININT1:
                    if (!LoadBinInt (1))
                        break;
                    continue;

                case BININT2:
                    if (!LoadBinInt (2))
                        break;
                    continue;

                case INT:
                    if (!LoadInt())
                        break;
                    continue;

                case TUPLE2:
                    if (!LoadCountedTuple (2))
                        break;
                    continue;

                case TUPLE3:
                    if (!LoadCountedTuple (3))
                        break;
                    continue;

                case LONG1:
                    if (!LoadLong())
                        break;
                    continue;

                case LONG4:
                    if (!LoadLong4())
                        break;
                    continue;

                case APPEND:
                    if (!LoadAppend())
                        break;
                    continue;

                case SETITEM:
                    if (!LoadSetItem())
                        break;
                    continue;

                case SETITEMS:
                    if (!LoadSetItems())
                        break;
                    continue;

                case STOP:
                    break;

                case -1: // EOF
                case 0:
                    Trace.WriteLine ("Unexpected end of file", "Pickle.Load");
                    return null;

                default:
                    Trace.TraceError ("Unknown Pickle serialization opcode 0x{0:X2}", sym);
                    return null;
                }
                break;
            }
            if (0 == m_stack.Count)
            {
                Trace.WriteLine ("Invalid pickle data", "Pickle.Load");
                return null;
            }
            return m_stack.Pop();
        }

        bool LoadProto ()
        {
            int i = m_stream.ReadByte();
            if (-1 == i)
                return false;
            if (i > HIGHEST_PROTOCOL)
                return false;
            return true;
        }

        bool LoadEmptyDict ()
        {
            m_stack.Push (new Hashtable());
            return true;
        }

        bool LoadBinPut ()
        {
            int key = m_stream.ReadByte();
            if (-1 == key || 0 == m_stack.Count)
                return false;
            return true;
        }

        bool LoadLongBinPut ()
        {
            int key;
            if (!ReadInt (4, out key) || 0 == m_stack.Count || key < 0)
                return false;
            return true;
        }

        bool LoadMark ()
        {
            m_marks.Push (m_stack.Count);
            return true;
        }

        int GetMarker ()
        {
            if (0 == m_marks.Count)
            {
                Trace.TraceError ("MARK list is empty");
                return -1;
            }
            return m_marks.Pop();
        }

        bool LoadShortBinstring ()
        {
            int length = m_stream.ReadByte();
            if (-1 == length)
                return false;
            return LoadBinString (length);
        }

        bool LoadBinUnicode ()
        {
            int length;
            if (!ReadInt (4, out length))
                return false;
            return LoadBinString (length);
        }

        bool LoadBinString (int length)
        {
            var bytes = new byte[length];
            if (length != m_stream.Read (bytes, 0, length))
                return false;
            m_stack.Push (bytes);
            return true;
        }

        bool LoadEmptyList ()
        {
            m_stack.Push (new ArrayList());
            return true;
        }

        bool ReadInt (int size, out int value)
        {
            value = 0;
            for (int i = 0; i < size; ++i)
            {
                int b = m_stream.ReadByte();
                if (-1 == b)
                    return false;
                value |= b << (i * 8);
            }
            return true;
        }

        bool LoadBinInt (int size)
        {
            int x = 0;
            if (!ReadInt (size, out x))
                return false;
            m_stack.Push (x);
            return true;
        }

        bool LoadInt ()
        {
            var num = m_stream.ReadStringUntil (0x0a, Encoding.ASCII);
            long n;
            if (!long.TryParse (num, NumberStyles.Integer, CultureInfo.InvariantCulture, out n))
                return false;
            m_stack.Push (n);
            return true;
        }

        bool LoadLong ()
        {
            int count = m_stream.ReadByte();
            if (-1 == count)
                return false;
            m_stack.Push (DecodeLong (count));
            return true;
        }

        bool LoadLong4 ()
        {
            int count = 0;
            if (!ReadInt (4, out count) || count < 0)
                return false;
            m_stack.Push (DecodeLong (count));
            return true;
        }

        object DecodeLong (int count)
        {
            if (count <= 0)
                return 0L;
            else if (count > 8)
            {
                var bytes = new byte[count];
                m_stream.Read (bytes, 0, count);
                return new BigInteger (bytes);
            }
            else
            {
                var bytes = new byte[8];
                m_stream.Read (bytes, 0, count);
                if (0 != (bytes[count-1] & 0x80)) // sign bit is set
                {
                    for (int i = count; i < bytes.Length; ++i)
                        bytes[i] = 0xFF;
                }
                return bytes.ToInt64 (0);
            }
        }

        bool LoadCountedTuple (int count)
        {
            if (m_stack.Count < count)
                return false;
            var tuple = new ArrayList (count);
            while (--count >= 0)
            {
                var item = m_stack.Pop();
                tuple.Add (item);
            }
            tuple.Reverse();
            m_stack.Push (tuple);
            return true;
        }

        bool LoadAppend ()
        {
            int x = m_stack.Count - 1;
            if (x <= 0)
            {
                Trace.WriteLine ("Stack underflow", "LoadAppend");
                return false;
            }
            var list = m_stack[x-1] as ArrayList;
            if (null == list)
            {
                Trace.WriteLine ("Object is not a list", "LoadAppend");
                return false;
            }
            var slice = PdataPopList (x);
            if (null == slice)
                return false;
            list.AddRange (slice);
            return true;
        }

        ArrayList PdataPopList (int start)
        {
            int count = m_stack.Count - start;
            var list = new ArrayList (count);
            for (int i = start; i < m_stack.Count; ++i)
                list.Add (m_stack[i]);
            m_stack.RemoveRange (start, count);
            return list;
        }

        bool LoadSetItem ()
        {
            return DoSetItems (m_stack.Count-2);
        }

        bool LoadSetItems ()
        {
            return DoSetItems (GetMarker());
        }

        bool DoSetItems (int mark)
        {
            if (!(m_stack.Count >= mark && mark > 0))
            {
                Trace.WriteLine ("Stack underflow", "LoadSetItems");
                return false;
            }
            var dict = m_stack[mark-1] as Hashtable;
            if (null == dict)
            {
                Trace.WriteLine ("Marked object is not a dictionary", "LoadSetItems");
                return false;
            }
            for (int i = mark+1; i < m_stack.Count; i += 2)
            {
                var key   = m_stack[i-1];
                var value = m_stack[i];
                dict[key] = value;
            }
            return PdataClear (mark);
        }

        bool PdataClear (int clearto)
        {
            if (clearto < 0)
                return false;
            if (clearto < m_stack.Count)
                m_stack.RemoveRange (clearto, m_stack.Count-clearto);
            return true;
        }
    }

    static public class ArrayListEx
    {
        static public object Peek (this ArrayList array)
        {
            return array[array.Count-1];
        }

        static public void Push (this ArrayList array, object item)
        {
            array.Add (item);
        }

        static public object Pop (this ArrayList array)
        {
            var item = array[array.Count-1];
            array.RemoveAt (array.Count-1);
            return item;
        }
    }

    internal class PyString : IEquatable<PyString>
    {
        int         m_hash;
        byte[]      m_bytes;
        Lazy<bool>  m_is_ascii;

        public PyString (string s)
        {
            m_hash = s.GetHashCode();
            m_bytes = Encoding.UTF8.GetBytes (s);
            m_is_ascii = new Lazy<bool> (() => -1 == Array.FindIndex (m_bytes, x => x > 0x7f));
        }

        public PyString () : this ("")
        {
        }

        public bool IsAscii { get { return m_is_ascii.Value; } }

        public byte[] Bytes { get { return m_bytes; } }

        public int   Length { get { return m_bytes.Length; } }

        public bool Equals (PyString other)
        {
            if (null == other)
                return false;
            if (this.m_hash != other.m_hash)
                return false;
            if (this.Length != other.Length)
                return false;
            for (var i = 0; i < m_bytes.Length; ++i)
                if (m_bytes[i] != other.m_bytes[i])
                    return false;
            return true;
        }

        public override bool Equals (object other)
        {
            return this.Equals (other as PyString);
        }

        public override int GetHashCode ()
        {
            return m_hash;
        }

        public override string ToString ()
        {
            return Encoding.UTF8.GetString (m_bytes);
        }
    }
}
