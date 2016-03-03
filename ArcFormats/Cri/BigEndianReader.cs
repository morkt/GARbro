//! \file       BigEndianReader.cs
//! \date       Wed Mar 02 23:27:29 2016
//! \brief      Wrapper around BinaryReader that reads data in a big endian order.
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
using System.IO;
using System.Text;
using GameRes.Utility;

namespace GameRes.Formats.Cri
{
    public class BigEndianReader : IDisposable
    {
        BinaryReader    m_input;
        byte[]          m_buffer = new byte[8];

        public long Position
        {
            get { return m_input.BaseStream.Position; }
            set { m_input.BaseStream.Position = value; }
        }

        public BigEndianReader(Stream input)
        {
            m_input = new BinaryReader (input, Encoding.UTF8, false);
        }

        public BigEndianReader (Stream input, Encoding enc, bool leave_open = false)
        {
            m_input = new BinaryReader (input, enc, leave_open);
        }

        public int Read (byte[] buffer, int index, int count)
        {
            return m_input.Read (buffer, index, count);
        }

        public void Skip (int amount)
        {
            m_input.BaseStream.Seek (amount, SeekOrigin.Current);
        }

        public byte ReadByte ()
        {
            return m_input.ReadByte();
        }

        public sbyte ReadSByte ()
        {
            return m_input.ReadSByte();
        }

        public short ReadInt16 ()
        {
            return Binary.BigEndian (m_input.ReadInt16());
        }

        public ushort ReadUInt16 ()
        {
            return Binary.BigEndian (m_input.ReadUInt16());
        }

        public int ReadInt32 ()
        {
            return Binary.BigEndian (m_input.ReadInt32());
        }

        public uint ReadUInt32 ()
        {
            return Binary.BigEndian (m_input.ReadUInt32());
        }

        public long ReadInt64 ()
        {
            return Binary.BigEndian (m_input.ReadInt64());
        }

        public ulong ReadUInt64 ()
        {
            return Binary.BigEndian (m_input.ReadUInt64());
        }

        public float ReadSingle ()
        {
            if (4 != m_input.Read (m_buffer, 0, 4))
                throw new EndOfStreamException();
            if (BitConverter.IsLittleEndian)
                Array.Reverse (m_buffer, 0, 4);
            return BitConverter.ToSingle (m_buffer, 0);
        }

        #region IDisposable Members
        bool _disposed = false;
        public void Dispose ()
        {
            if (!_disposed)
            {
                m_input.Dispose();
                _disposed = true;
            }
        }
        #endregion
    }
}
