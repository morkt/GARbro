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
using System.IO;
using System.Text;
using ZLibNet;

namespace GameRes.Formats.RenPy
{
    internal class RpaEntry : Entry
    {
        public byte[] Header = null;
    }

    [Export(typeof(ArchiveFormat))]
    public class RpaOpener : ArchiveFormat
    {
        public override string Tag { get { return "RPA"; } }
        public override string Description { get { return Strings.arcStrings.RPADescription; } }
        public override uint Signature { get { return 0x2d415052; } } // "RPA-"
        public override bool IsHierarchic { get { return true; } }

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

            Hashtable dict = null;
            using (var index = new ZLibStream (file.CreateStream (index_offset), CompressionMode.Decompress))
            {
                var pickle = new Pickle (index);
                dict = pickle.Load() as Hashtable;
            }
            if (null == dict)
                return null;
            var dir = new List<Entry> (dict.Count);
            foreach (DictionaryEntry item in dict)
            {
                var name_raw = item.Key as byte[];
                var value = item.Value as ArrayList;
                if (null == name_raw || null == value || value.Count < 1)
                {
                    Trace.WriteLine ("invalid index entry", "RpaOpener.TryOpen");
                    return null;
                }
                string name = Encoding.UTF8.GetString (name_raw);
                if (string.IsNullOrEmpty (name))
                    return null;
                var tuple = value[0] as ArrayList;
                if (null == tuple || tuple.Count < 2)
                {
                    Trace.WriteLine ("invalid index tuple", "RpaOpener.TryOpen");
                    return null;
                }
                var entry = new RpaEntry
                {
                    Name   = name,
                    Type   = FormatCatalog.Instance.GetTypeFromName (name),
                    Offset = (uint)((int)tuple[0] ^ key),
                    Size   = (uint)((int)tuple[1] ^ key),
                };
                if (tuple.Count > 2)
                    entry.Header = tuple[2] as byte[];

                dir.Add (entry);
            }
            if (dir.Count > 0)
                Trace.TraceInformation ("[{0}] [{1:X8}] [{2}]", dir[0].Name, dir[0].Offset, dir[0].Size);
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            var rpa_entry = entry as RpaEntry;
            if (null == rpa_entry || null == rpa_entry.Header)
                return input;
            return new RpaStream (rpa_entry.Header, input);
        }
    }

    public class Pickle
    {
        Stream m_stream;

        ArrayList       m_stack = new ArrayList();
        Stack<int>      m_marks = new Stack<int>();

        const int HIGHEST_PROTOCOL  = 2;
        const int PROTO             = 0x80; /* identify pickle protocol */
        const int TUPLE2            = 0x86; /* build 2-tuple from two topmost stack items */
        const int TUPLE3            = 0x87; /* build 3-tuple from three topmost stack items */
        const int MARK              = '(';
        const int STOP              = '.';
        const int BININT            = 'J';
        const int BININT1           = 'K';
        const int BININT2           = 'M';
        const int SHORT_BINSTRING   = 'U';
        const int EMPTY_LIST        = ']';
        const int APPEND            = 'a';
        const int BINPUT            = 'q';
        const int LONG_BINPUT       = 'r';
        const int SETITEMS          = 'u';
        const int EMPTY_DICT        = '}';

        public Pickle (Stream stream)
        {
            m_stream = stream;
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

                case TUPLE2:
                    if (!LoadCountedTuple (2))
                        break;
                    continue;

                case TUPLE3:
                    if (!LoadCountedTuple (3))
                        break;
                    continue;

                case APPEND:
                    if (!LoadAppend())
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
                    Trace.TraceError ("Unknown Pickle serialization key {0:X2}", sym);
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
//            m_memo[key] = m_stack.Peek();
            return true;
        }

        bool LoadLongBinPut ()
        {
            int key;
            if (!ReadInt (4, out key) || 0 == m_stack.Count || key < 0)
                return false;
//            m_memo[key] = m_stack.Peek();
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
            if (m_stack.Count < x || 0 == x)
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

        bool LoadSetItems ()
        {
            int mark = GetMarker();
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
            if (clearto >= m_stack.Count)
                return true;
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

    public class RpaStream : Stream
    {
        byte[]  m_header;
        Stream  m_stream;
        long    m_position = 0;

        public RpaStream (byte[] header, Stream main)
        {
            m_header = header;
            m_stream = main;
        }

        public override bool CanRead  { get { return m_stream.CanRead; } }
        public override bool CanSeek  { get { return m_stream.CanSeek; } }
        public override bool CanWrite { get { return false; } }
        public override long Length   { get { return m_stream.Length + m_header.Length; } }
        public override long Position
        {
            get { return m_position; }
            set
            {
                m_position = Math.Max (value, 0);
                if (m_position > m_header.Length)
                {
                    long stream_pos = m_stream.Seek (m_position - m_header.Length, SeekOrigin.Begin);
                    m_position = m_header.Length + stream_pos;
                }
            }
        }

        public override void Flush()
        {
            m_stream.Flush();
        }

        public override long Seek (long offset, SeekOrigin origin)
        {
            if (SeekOrigin.Begin == origin)
                Position = offset;
            else if (SeekOrigin.Current == origin)
                Position = m_position + offset;
            else
                Position = Length + offset;

            return m_position;
        }

        public override int Read (byte[] buffer, int offset, int count)
        {
            int read = 0;
            if (m_position < m_header.Length)
            {
                int header_count = Math.Min (count, m_header.Length - (int)m_position);
                Array.Copy (m_header, (int)m_position, buffer, offset, header_count);
                m_position += header_count;
                read += header_count;
                offset += header_count;
                count -= header_count;
                if (count > 0)
                    m_stream.Position = 0;
            }
            if (count > 0)
            {
                int stream_read = m_stream.Read (buffer, offset, count);
                m_position += stream_read;
                read += stream_read;
            }
            return read;
        }

        public override int ReadByte ()
        {
            if (m_position < m_header.Length)
                return m_header[m_position++];
            if (m_position == m_header.Length)
                m_stream.Position = 0;
            int b = m_stream.ReadByte();
            if (-1 != b)
                m_position++;
            return b;
        }

        public override void SetLength (long length)
        {
            throw new NotSupportedException ("RpaStream.SetLength method is not supported");
        }

        public override void Write (byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException ("RpaStream.Write method is not supported");
        }

        public override void WriteByte (byte value)
        {
            throw new NotSupportedException ("RpaStream.WriteByte method is not supported");
        }

        bool disposed = false;
        protected override void Dispose (bool disposing)
        {
            if (!disposed)
            {
                m_stream.Dispose();
                disposed = true;
                base.Dispose (disposing);
            }
        }
    }
}
