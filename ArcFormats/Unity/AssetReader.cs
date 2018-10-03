//! \file       AssetReader.cs
//! \date       Wed Apr 05 13:28:33 2017
//! \brief      Unity asset reader class.
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

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using GameRes.Utility;

namespace GameRes.Formats.Unity
{
    /// <summary>
    /// AssetReader provides access to a serialized stream of Unity assets.
    /// </summary>
    internal sealed class AssetReader : IDisposable
    {
        IBinaryStream   m_input;
        int             m_format;

        const int MaxStringLength = 0x100000;

        public Stream Source { get { return m_input.AsStream; } }
        public int    Format { get { return m_format; } }
        public long Position {
            get { return m_input.Position; }
            set { m_input.Position = value; }
        }

        public AssetReader (Stream input, string name) : this (BinaryStream.FromStream (input, name))
        {
        }

        public AssetReader (IBinaryStream input)
        {
            m_input = input;
            SetupReaders (0, false);
        }

        public Action       Align;
        public Func<ushort> ReadUInt16;
        public Func<short>  ReadInt16;
        public Func<uint>   ReadUInt32;
        public Func<int>    ReadInt32;
        public Func<long>   ReadInt64;
        public Func<long>   ReadId;

        public void SetupReaders (Asset asset)
        {
            SetupReaders (asset.Format, asset.IsLittleEndian);
        }

        /// <summary>
        /// Setup reader endianness accordingly.
        /// </summary>
        public void SetupReaders (int format, bool is_little_endian)
        {
            m_format = format;
            if (is_little_endian)
            {
                ReadUInt16 = () => m_input.ReadUInt16();
                ReadUInt32 = () => m_input.ReadUInt32();
                ReadInt16 = () => m_input.ReadInt16();
                ReadInt32 = () => m_input.ReadInt32();
                ReadInt64 = () => m_input.ReadInt64();
            }
            else
            {
                ReadUInt16 = () => Binary.BigEndian (m_input.ReadUInt16());
                ReadUInt32 = () => Binary.BigEndian (m_input.ReadUInt32());
                ReadInt16 = () => Binary.BigEndian (m_input.ReadInt16());
                ReadInt32 = () => Binary.BigEndian (m_input.ReadInt32());
                ReadInt64 = () => Binary.BigEndian (m_input.ReadInt64());
            }
            if (m_format >= 14 || m_format == 9)
            {
                Align = () => {
                    long pos = m_input.Position;
                    if (0 != (pos & 3))
                        m_input.Position = (pos + 3) & ~3L;
                };
            }
            else
            {
                Align = () => {};
            }
            if (m_format >= 14)
                ReadId = ReadInt64;
            else
                ReadId = () => ReadInt32();
        }

        /// <summary>
        /// Set asset ID length.  If <paramref name="long_id"/> is <c>true</c> IDs are 64-bit, otherwise 32-bit.
        /// </summary>
        public void SetupReadId (bool long_ids)
        {
            if (long_ids)
                ReadId = ReadInt64;
            else
                ReadId = () => ReadInt32();
        }

        /// <summary>
        /// Read bytes into specified buffer.
        /// </summary>
        public int Read (byte[] buffer, int offset, int count)
        {
            return m_input.Read (buffer, offset, count);
        }

        /// <summary>
        /// Read null-terminated UTF8 string.
        /// </summary>
        public string ReadCString ()
        {
            return m_input.ReadCString (Encoding.UTF8);
        }

        /// <summary>
        /// Read UTF8 string prefixed with length.
        /// </summary>
        public string ReadString ()
        {
            int length = ReadInt32();
            if (0 == length)
                return string.Empty;
            if (length < 0 || length > MaxStringLength)
                throw new InvalidFormatException();
            var bytes = ReadBytes (length);
            return Encoding.UTF8.GetString (bytes);
        }

        /// <summary>
        /// Read <paramref name="length"/> bytes from stream and return them in a byte array.
        /// May return less than <paramref name="length"/> bytes if end of file was encountered.
        /// </summary>
        public byte[] ReadBytes (int length)
        {
            return m_input.ReadBytes (length);
        }

        /// <summary>
        /// Read unsigned 8-bits byte from a stream.
        /// </summary>
        public byte ReadByte ()
        {
            return m_input.ReadUInt8();
        }

        /// <summary>
        /// Read byte and interpret is as a bool value, non-zero resulting in <c>true</c>.
        /// </summary>
        public bool ReadBool ()
        {
            return ReadByte() != 0;
        }

        [StructLayout(LayoutKind.Explicit)]
        struct Union
        {
            [FieldOffset (0)]
            public uint u;
            [FieldOffset(0)]
            public float f;
        }

        /// <summary>
        /// Read float value from a stream.
        /// </summary>
        public float ReadFloat ()
        {
            var buf = new Union();
            buf.u = ReadUInt32();
            return buf.f;
        }

        bool _disposed = false;
        public void Dispose ()
        {
            if (!_disposed)
            {
                m_input.Dispose();
                _disposed = true;
            }
            GC.SuppressFinalize (this);
        }
    }
}
