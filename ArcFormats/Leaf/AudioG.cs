//! \file       AudioG.cs
//! \date       Thu Aug 18 15:50:27 2016
//! \brief      Leaf audio format.
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
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Utility;

namespace GameRes.Formats.Leaf
{
    [Export(typeof(AudioFormat))]
    public class GAudio : AudioFormat
    {
        public override string         Tag { get { return "G/Leaf"; } }
        public override string Description { get { return "Leaf audio format (Ogg/Vorbis)"; } }
        public override uint     Signature { get { return 0; } }

        public GAudio ()
        {
            Extensions = new string[] { "g" };
        }

        public override SoundInput TryOpen (Stream file)
        {
            var header = new byte[0x1C];
            if (header.Length != file.Read (header, 0, header.Length))
                return null;
            if (header[4] != 0 || header[5] != 2 || LittleEndian.ToInt64 (header, 6) != 0)
                return null;
            file.Position = 0;
            var input = new GStream (file);
            return new OggInput (new SeekableStream (input));
        }
    }

    internal class GStream : InputProxyStream
    {
        IEnumerator<int>    m_pages;
        bool                m_eof;

        byte[]              m_page = new byte[0x10000];
        int                 m_page_pos = 0;
        int                 m_page_length = 0;

        public GStream (Stream source) : base (source)
        {
            m_pages = EnumeratePages();
            m_eof = false;
        }

        #region IO.Stream methods
        public override bool CanSeek { get { return false; } }
        public override long  Length { get { throw new NotSupportedException(); } }
        public override long Position
        {
            get { throw new NotSupportedException(); }
            set { throw new NotSupportedException(); }
        }
        public override long Seek (long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }
        #endregion

        public override int Read (byte[] buffer, int offset, int count)
        {
            int total_read = 0;
            while (count > 0 && !m_eof)
            {
                if (m_page_pos == m_page_length)
                {
                    NextPage();
                }
                if (m_eof)
                    break;
                int available = Math.Min (m_page_length - m_page_pos, count);
                Buffer.BlockCopy (m_page, m_page_pos, buffer, offset, available);
                m_page_pos += available;
                offset += available;
                count -= available;
                total_read += available;
            }
            return total_read;
        }

        void NextPage ()
        {
            m_eof = !m_pages.MoveNext();
            m_page_pos = 0;
            m_page_length = !m_eof ? m_pages.Current : 0;
        }

        enum State
        {
            Header,
            Comment,
            Setup,
            Payload,
            Broken,
        };

        IEnumerator<int> EnumeratePages ()
        {
            var state = State.Header;
            for (;;)
            {
                int page_length = 0x1B;
                int read = BaseStream.Read (m_page, 0, page_length);
                if (read < page_length)
                {
                    if (read != 0)
                        yield return read;
                    yield break;
                }
                PutString (0, "OggS");
                int segment_count = m_page[0x1A];
                if (0 == segment_count)
                {
                    UpdateCrc (page_length);
                    yield return page_length;
                    continue;
                }
                read = BaseStream.Read (m_page, page_length, segment_count);
                page_length += read;
                if (read < segment_count)
                {
                    UpdateCrc (page_length);
                    yield return page_length;
                    yield break;
                }
                int segments_size = 0;
                for (int i = 0; i < segment_count; ++i)
                {
                    segments_size += m_page[0x1B+i];
                }
                int id, next;
                switch (state)
                {
                case State.Header:
                    id = BaseStream.ReadByte();
                    if (-1 == id)
                        break;

                    m_page[page_length++] = (byte)id;
                    next = BaseStream.ReadByte();
                    segments_size -= 2;
                    if (1 == id)
                    {
                        PutString (page_length, "vorbis");
                        page_length += 6;
                        m_page[0x1B] += 5;
                        state = State.Comment;
                    }
                    else
                    {
                        m_page[page_length++] = (byte)next;
                        state = State.Broken;
                    }
                    break;

                case State.Comment:
                    id = BaseStream.ReadByte();
                    if (-1 == id)
                        break;

                    m_page[page_length++] = (byte)id;
                    next = BaseStream.ReadByte();
                    segments_size -= 2;
                    if (3 == id)
                    {
                        PutString (page_length, "vorbis");
                        page_length += 6;
                        read = BaseStream.Read (m_page, page_length, m_page[0x1B]-2);
                        page_length += read;
                        segments_size -= read;
                        m_page[0x1B] += 5;
                        state = State.Setup;
                        if (segments_size > 0)
                            goto case State.Setup;
                    }
                    else
                    {
                        m_page[page_length++] = (byte)next;
                        state = State.Broken;
                    }
                    break;

                case State.Setup:
                    id = BaseStream.ReadByte();
                    if (-1 == id)
                        break;

                    m_page[page_length++] = (byte)id;
                    next = BaseStream.ReadByte();
                    segments_size -= 2;
                    if (5 == id)
                    {
                        PutString (page_length, "vorbis");
                        page_length += 6;
                        m_page[0x1B+segment_count-1] += 5;
                        state = State.Payload;
                    }
                    else
                    {
                        m_page[page_length++] = (byte)next;
                        state = State.Broken;
                    }
                    break;
                }
                read = BaseStream.Read (m_page, page_length, segments_size);
                page_length += read;
                UpdateCrc (page_length);
                yield return page_length;
            }
        }

        void PutString (int pos, string s)
        {
            for (int i = 0; i < s.Length; ++i)
                m_page[pos+i] = (byte)s[i];
        }

        void UpdateCrc (int page_length)
        {
            LittleEndian.Pack (0, m_page, 0x16);
            uint crc = Crc32Normal.UpdateCrc (0, m_page, 0, page_length);
            LittleEndian.Pack (crc, m_page, 0x16);
        }

        #region IDisposable Members
        bool _g_disposed = false;
        protected override void Dispose (bool disposing)
        {
            if (_g_disposed)
                return;

            if (disposing)
            {
                m_pages.Dispose();
            }
            _g_disposed = true;
            base.Dispose (disposing);
        }
        #endregion
    }
}
