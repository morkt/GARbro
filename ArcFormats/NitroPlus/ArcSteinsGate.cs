//! \file       ArcSteinsGate.cs
//! \date       Thu Jul 24 23:36:01 2014
//! \brief      Nitro+ Steins;Gate archive implementation.
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

using System.IO;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using GameRes.Formats.Strings;
using GameRes.Utility;

namespace GameRes.Formats.NitroPlus
{
    public class SteinsGateOptions : ResourceOptions
    {
        public Encoding FileNameEncoding { get; set; }
    }

    [Export(typeof(ArchiveFormat))]
    public class NpaSteinsGateOpener : ArchiveFormat
    {
        public override string         Tag { get { return "NPA-SG"; } }
        public override string Description { get { return arcStrings.NPASteinsGateDescription; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return true; } }

        public NpaSteinsGateOpener ()
        {
            Extensions = new string[] { "npa" };
        }

        internal static readonly byte[] KeyString = {
            'B'^0xff, 'U'^0xff, 'C'^0xff, 'K'^0xff,
            'T'^0xff, 'I'^0xff, 'C'^0xff, 'K'^0xff
        };

        public override ArcFile TryOpen (ArcView file)
        {
            int index_size = file.View.ReadInt32 (0);
            if (index_size < 0x14 || index_size >= file.MaxOffset || index_size > 0xffffff)
                return null;

            var stream = new SteinsGateEncryptedStream (file, 4, (uint)index_size);
            using (var header = new BinaryReader (stream))
            {
                int entry_count = header.ReadInt32();
                if (!IsSaneCount (entry_count))
                    return null;
                index_size -= 4;
                int average_entry_size = index_size / entry_count;
                if (average_entry_size < 0x11)
                    return null;

                var dir = new List<Entry> (entry_count);
                for (int i = 0; i < entry_count; ++i)
                {
                    int name_length = header.ReadInt32();
                    if (name_length+0x10 > index_size)
                        return null;
                    byte[] name_raw = header.ReadBytes (name_length);
                    Encoding enc = GuessEncoding (name_raw);
                    string filename = enc.GetString (name_raw);

                    var entry = FormatCatalog.Instance.Create<Entry> (filename);
                    entry.Size = header.ReadUInt32();
                    entry.Offset = header.ReadInt64();
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);

                    index_size -= name_length+0x10;
                }
                return new ArcFile (file, this, dir);
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            return new SteinsGateEncryptedStream (arc.File, entry.Offset, entry.Size);
        }

        internal void Encrypt (byte[] buffer, int offset, int count)
        {
            for (int i = 0; i < count; ++i)
            {
                buffer[offset+i] ^= KeyString[i & 7];
            }
        }

        Encoding GuessEncoding (byte[] text)
        {
            bool has_zero = false;
            bool has_non_ascii = false;
            foreach (var symbol in text)
            {
                if (0 == symbol)
                {
                    has_zero = true;
                    break;
                }
                else if (symbol > 0x7f)
                {
                    has_non_ascii = true;
                }
            }
            if (has_zero)
                return Encoding.Unicode;
            else if (has_non_ascii)
                return Encodings.cp932;
            else
                return Encoding.ASCII;
        }

        public override ResourceOptions GetDefaultOptions ()
        {
            return new SteinsGateOptions {
                FileNameEncoding = GetEncoding (Properties.Settings.Default.SGFileNameEncoding),
            };
        }

        public override object GetCreationWidget ()
        {
            return new GUI.CreateSGWidget();
        }

        Encoding GetEncoding (string name)
        {
            if ("shift-jis" == name)
                return Encodings.cp932;
            if ("utf-16" == name)
                return Encoding.Unicode;
            return Encoding.Default;
        }

        internal class RawEntry : Entry
        {
            public byte[]   IndexName;
        }

        public override void Create (Stream output, IEnumerable<Entry> list, ResourceOptions options,
                                     EntryCallback callback)
        {
            var sg_options = GetOptions<SteinsGateOptions> (options);
            Encoding encoding = sg_options.FileNameEncoding.WithFatalFallback();
            long start_pos = output.Position;
            int callback_count = 0;

            uint index_size = 4;
            var real_entry_list = new List<RawEntry> (list.Count());
            var used_names = new HashSet<string>();
            foreach (var entry in list)
            {
                string name = entry.Name.Replace (@"\", "/");
                if (!used_names.Add (name)) // duplicate name
                    continue;
                var header_entry = new RawEntry { Name = entry.Name };
                try
                {
                    header_entry.IndexName = encoding.GetBytes (name);
                }
                catch (EncoderFallbackException X)
                {
                    throw new InvalidFileName (entry.Name, arcStrings.MsgIllegalCharacters, X);
                }
                index_size += (uint)header_entry.IndexName.Length + 16;
                real_entry_list.Add (header_entry);
            }
            output.Seek (4+index_size, SeekOrigin.Current);
            foreach (var entry in real_entry_list)
            {
                using (var input = File.Open (entry.Name, FileMode.Open, FileAccess.Read))
                {
                    var file_size = input.Length;
                    if (file_size > uint.MaxValue)
                        throw new FileSizeException();
                    entry.Offset = output.Position;
                    entry.Size  = (uint)file_size;
                    if (null != callback)
                        callback (callback_count++, entry, arcStrings.MsgAddingFile);
                    using (var stream = new SteinsGateEncryptedStream (output))
                        input.CopyTo (stream);
                }
            }
            if (null != callback)
                callback (callback_count++, null, arcStrings.MsgWritingIndex);
            output.Position = start_pos;
            output.WriteByte ((byte)(index_size & 0xff));
            output.WriteByte ((byte)((index_size >> 8) & 0xff));
            output.WriteByte ((byte)((index_size >> 16) & 0xff));
            output.WriteByte ((byte)((index_size >> 24) & 0xff));
            var encrypted_stream = new SteinsGateEncryptedStream (output);
            using (var header = new BinaryWriter (encrypted_stream))
            {
                header.Write (real_entry_list.Count);
                foreach (var entry in real_entry_list)
                {
                    header.Write (entry.IndexName.Length);
                    header.Write (entry.IndexName);
                    header.Write ((uint)entry.Size);
                    header.Write ((long)entry.Offset);
                }
            }
        }
    }

    public class SteinsGateEncryptedStream : Stream
    {
        private Stream      m_stream;
        private long        m_base_pos;
        private bool        m_should_dispose;

        public Stream BaseStream { get { return m_stream; } }

        public override bool  CanRead { get { return m_stream.CanRead; } }
        public override bool  CanSeek { get { return m_stream.CanSeek; } }
        public override bool CanWrite { get { return m_stream.CanWrite; } }
        public override long   Length { get { return m_stream.Length - m_base_pos; } }
        public override long Position
        {
            get { return m_stream.Position - m_base_pos; }
            set { m_stream.Position = m_base_pos + value; }
        }

        public SteinsGateEncryptedStream (ArcView file, long offset, uint size)
        {
            m_stream = file.CreateStream (offset, size);
            m_should_dispose = true;
            m_base_pos = 0;
        }

        public SteinsGateEncryptedStream (Stream output)
        {
            m_stream = output;
            m_should_dispose = false;
            m_base_pos = m_stream.Position;
        }

        #region System.IO.Stream methods
        public override void Flush()
        {
            m_stream.Flush();
        }

        public override long Seek (long offset, SeekOrigin origin)
        {
            if (SeekOrigin.Begin == origin)
                offset += m_base_pos;
            offset = m_stream.Seek (offset, origin);
            return offset - m_base_pos;
        }

        public override void SetLength (long length)
        {
            throw new System.NotSupportedException ("SteinsGateEncryptedStream.SetLength method is not supported");
        }

        public override int Read (byte[] buffer, int offset, int count)
        {
            int position = (int)Position & 7;
            int read = m_stream.Read (buffer, offset, count);
            if (read > 0)
            {
                for (int i = 0; i < read; ++i)
                {
                    buffer[offset+i] ^= NpaSteinsGateOpener.KeyString[(position+i)&7];
                }
            }
            return read;
        }

        public override int ReadByte ()
        {
            int position = (int)Position & 7;
            int b = m_stream.ReadByte();
            if (-1 != b)
            {
                b ^= NpaSteinsGateOpener.KeyString[position];
            }
            return b;
        }

        public override void Write (byte[] buffer, int offset, int count)
        {
            int position = (int)Position & 7;
            byte[] write_buf = new byte[count];
            for (int i = 0; i < count; ++i)
            {
                write_buf[i] = (byte)(buffer[offset+i] ^ NpaSteinsGateOpener.KeyString[(position+i)&7]);
            }
            m_stream.Write (write_buf, 0, count);
        }

        public override void WriteByte (byte value)
        {
            int position = (int)Position & 7;
            m_stream.WriteByte ((byte)(value ^ NpaSteinsGateOpener.KeyString[position]));
        }
        #endregion

        #region IDisposable Members
        bool disposed = false;
        protected override void Dispose (bool disposing)
        {
            if (!disposed)
            {
                if (disposing && m_should_dispose)
                {
                    m_stream.Dispose();
                }
                disposed = true;
                base.Dispose (disposing);
            }
        }
        #endregion
    }
}
