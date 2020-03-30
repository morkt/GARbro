//! \file       CryptAlgorithms.cs
//! \date       Thu Feb 04 12:08:40 2016
//! \brief      KiriKiri engine encryption algorithms.
//
// Copyright (C) 2016-2017 by morkt
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
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using GameRes.Compression;
using GameRes.Utility;

namespace GameRes.Formats.KiriKiri
{
    [Serializable]
    public abstract class ICrypt
    {
        /// <summary>
        /// whether Adler32 checksum should be calculated after contents have been encrypted.
        /// </summary>
        public virtual bool HashAfterCrypt { get { return false; } }

        /// <summary>
        /// sometimes startup.tjs file is not encrypted.
        /// </summary>
        public bool StartupTjsNotEncrypted { get; set; }

        /// <summary>
        /// whether XP3 index is obfuscated:
        ///  - duplicate entries
        ///  - entries have additional dummy segments
        /// </summary>
        public bool ObfuscatedIndex { get; set; }

        public override string ToString ()
        {
            return this.GetType().Name;
        }

        public virtual byte Decrypt (Xp3Entry entry, long offset, byte value)
        {
            byte[] buffer = new byte[1] { value };
            Decrypt (entry, offset, buffer, 0, 1);
            return buffer[0];
        }

        public abstract void Decrypt (Xp3Entry entry, long offset, byte[] values, int pos, int count);

        public virtual void Encrypt (Xp3Entry entry, long offset, byte[] values, int pos, int count)
        {
            throw new NotImplementedException (Strings.arcStrings.MsgEncNotImplemented);
        }

        /// <summary>
        /// Perform necessary initialization specific to an archive being opened.
        /// </summary>
        public virtual void Init (ArcFile arc)
        {
        }

        /// <summary>
        /// Read entry name from archive index.
        /// </summary>
        public virtual string ReadName (BinaryReader header)
        {
            int name_size = header.ReadInt16();
            if (name_size > 0 && name_size <= 0x100)
                return new string (header.ReadChars (name_size));
            else
                return null;
        }

        /// <summary>
        /// Post-process entry stream.
        /// </summary>
        public virtual Stream EntryReadFilter (Xp3Entry entry, Stream input)
        {
            if (entry.UnpackedSize <= 5 || "audio" == entry.Type)
                return input;

            var header = new byte[5];
            input.Read (header, 0, 5);
            uint signature = header.ToUInt32 (0);
            if (0x184D2204 == signature) // LZ4 magic
            {
                // assume no scripts are compressed using LZ4, return decompressed stream right away
                return DecompressLz4 (entry, header, input);
            }
            if (0x66646D == signature) // 'mdf'
            {
                return DecompressMdf (entry, header, input);
            }
            if ((signature & 0xFF00FFFFu) == 0xFF00FEFEu && header[2] < 3 && 0xFE == header[4])
                return DecryptScript (header[2], input, entry.UnpackedSize);

            if (!input.CanSeek)
                return new PrefixStream (header, input);
            input.Position = 0;
            return input;
        }

        internal Stream DecompressMdf (Xp3Entry entry, byte[] header, Stream input)
        {
            if (header.Length != 5)
                throw new ArgumentException ("Invalid header length for DecompressMdf", "header");
            var mdf_header = new byte[4] { header[4], 0, 0, 0 };
            input.Read (mdf_header, 1, 3);
            entry.UnpackedSize = mdf_header.ToUInt32 (0);
            entry.IsPacked = true;
            return new ZLibStream (input, CompressionMode.Decompress);
        }

        internal Stream DecompressLz4 (Xp3Entry entry, byte[] header, Stream input)
        {
            if (header.Length != 5)
                throw new ArgumentException ("Invalid header length for DecompressLz4", "header");
            var info = new Lz4FrameInfo (header[4]);
            info.SetBlockSize (input.ReadByte());
            if (info.HasContentLength)
            {
                input.Read (header, 0, 4);
                long length = header.ToUInt32 (0);
                input.Read (header, 0, 4);
                length |= (long)header.ToUInt32 (0) << 32;
                info.OriginalLength = length;
                entry.UnpackedSize = (uint)length;
                entry.IsPacked = true;
            }
            if (info.HasDictionary)
            {
                input.Read (header, 0, 4);
                info.DictionaryId = header.ToInt32 (0);
            }
            input.ReadByte(); // skip descriptor checksum
            return new Lz4Stream (input, info);
        }

        internal Stream DecryptScript (int enc_type, Stream input, uint unpacked_size)
        {
            using (var reader = new BinaryReader (input, Encoding.Unicode, true))
            {
                if (2 == enc_type)
                {
                    reader.ReadInt64(); // packed_size
                    reader.ReadInt64(); // unpacked_size
                    return new ZLibStream (input, CompressionMode.Decompress);
                }
                var output = new MemoryStream ((int)unpacked_size+2);
                using (var writer = new BinaryWriter (output, Encoding.Unicode, true))
                {
                    writer.Write ('\xFEFF'); // BOM
                    int c;
                    if (1 == enc_type)
                    {
                        while ((c = reader.Read()) != -1)
                        {
                            c = (c & 0xAAAA) >> 1 | (c & 0x5555) << 1;
                            writer.Write ((char)c);
                        }
                    }
                    else
                    {
                        while ((c = reader.Read()) != -1)
                        {
                            if (c >= 0x20)
                            {
                                c = c ^ (((c & 0xFE) << 8) ^ 1);
                                writer.Write ((char)c);
                            }
                        }
                    }
                }
                output.Position = 0;
                input.Dispose();
                return output;
            }
        }
    }

    [Serializable]
    public class NoCrypt : ICrypt
    {
        public override byte Decrypt (Xp3Entry entry, long offset, byte value)
        {
            return value;
        }
        public override void Decrypt (Xp3Entry entry, long offset, byte[] values, int pos, int count)
        {
            return;
        }
        public override void Encrypt (Xp3Entry entry, long offset, byte[] values, int pos, int count)
        {
            return;
        }
    }

    [Serializable]
    public class FateCrypt : ICrypt
    {
        public override bool HashAfterCrypt { get { return true; } }

        public override byte Decrypt (Xp3Entry entry, long offset, byte value)
        {
            byte result = (byte)(value ^ 0x36);
            if (0x13 == offset)
                result ^= 1;
            else if (0x2ea29 == offset)
                result ^= 3;
            return result;
        }

        public override void Decrypt (Xp3Entry entry, long offset, byte[] values, int pos, int count)
        {
            for (int i = 0; i < count; ++i)
            {
                values[pos+i] ^= 0x36;
            }
            if (offset > 0x2ea29)
                return;
            if (offset + count > 0x2ea29)
                values[pos+0x2ea29-offset] ^= 3;
            if (offset > 0x13)
                return;
            if (offset + count > 0x13)
                values[pos+0x13-offset] ^= 1;
        }

        public override void Encrypt (Xp3Entry entry, long offset, byte[] values, int pos, int count)
        {
            Decrypt (entry, offset, values, pos, count);
        }
    }

    [Serializable]
    public class MizukakeCrypt : ICrypt
    {
        public override bool HashAfterCrypt { get { return true; } }

        public override void Decrypt (Xp3Entry entry, long offset, byte[] values, int pos, int count)
        {
            if (offset <= 0x103 && offset + count > 0x103)
                values[pos+0x103-offset]--;
            for (int i = 0; i < count; ++i)
            {
                values[pos+i] ^= 0xB6;
            }
            if (offset > 0x3F82)
                return;
            if (offset + count > 0x3F82)
                values[pos+0x3F82-offset] ^= 1;
            if (offset > 0x83)
                return;
            if (offset + count > 0x83)
                values[pos+0x83-offset] ^= 3;
        }

        public override void Encrypt (Xp3Entry entry, long offset, byte[] values, int pos, int count)
        {
            for (int i = 0; i < count; ++i)
            {
                values[pos+i] ^= 0xB6;
            }
            if (offset <= 0x3F82 && offset + count > 0x3F82)
                values[pos+0x3F82-offset] ^= 1;
            if (offset <= 0x83 && offset + count > 0x83)
                values[pos+0x83-offset] ^= 3;
            if (offset <= 0x103 && offset + count > 0x103)
                values[pos+0x103-offset]++;
        }
    }

    [Serializable]
    public class HashCrypt : ICrypt
    {
        public override byte Decrypt (Xp3Entry entry, long offset, byte value)
        {
            return (byte)(value ^ entry.Hash);
        }

        public override void Decrypt (Xp3Entry entry, long offset, byte[] values, int pos, int count)
        {
            byte key = (byte)entry.Hash;
            for (int i = 0; i < count; ++i)
            {
                values[pos+i] ^= key;
            }
        }

        public override void Encrypt (Xp3Entry entry, long offset, byte[] values, int pos, int count)
        {
            Decrypt (entry, offset, values, pos, count);
        }
    }

    [Serializable]
    public class XorCrypt : ICrypt
    {
        private byte m_key;

        public byte Key
        {
            get { return m_key; }
            set { m_key = value; }
        }

        public XorCrypt (uint key)
        {
            m_key = (byte)key;
        }

        public override string ToString ()
        {
            return string.Format ("{0}(0x{1:X02})", base.ToString(), m_key);
        }

        public override byte Decrypt (Xp3Entry entry, long offset, byte value)
        {
            return (byte)(value ^ m_key);
        }

        public override void Decrypt (Xp3Entry entry, long offset, byte[] values, int pos, int count)
        {
            for (int i = 0; i < count; ++i)
            {
                values[pos+i] ^= m_key;
            }
        }

        public override void Encrypt (Xp3Entry entry, long offset, byte[] values, int pos, int count)
        {
            Decrypt (entry, offset, values, pos, count);
        }
    }

    [Serializable]
    public class FlyingShineCrypt : ICrypt
    {
        static private byte Adjust (uint hash, out int shift)
        {
            shift = (int)(hash & 0xff);
            if (0 == shift) shift = 0x0f;
            byte key = (byte)(hash >> 8);
            if (0 == key) key = 0xf0;
            return key;
        }

        public override byte Decrypt (Xp3Entry entry, long offset, byte value)
        {
            int shift;
            byte xor = Adjust (entry.Hash, out shift);
            return Binary.RotByteR ((byte)(value ^ xor), shift);
        }

        public override void Decrypt (Xp3Entry entry, long offset, byte[] values, int pos, int count)
        {
            int shift;
            byte xor = Adjust (entry.Hash, out shift);
            for (int i = 0; i < count; ++i)
            {
                byte data = (byte)(values[pos+i] ^ xor);
                values[pos+i] = Binary.RotByteR (data, shift);
            }
        }

        public override void Encrypt (Xp3Entry entry, long offset, byte[] values, int pos, int count)
        {
            int shift;
            byte xor = Adjust (entry.Hash, out shift);
            for (int i = 0; i < count; ++i)
            {
                byte data = Binary.RotByteL (values[pos+i], shift);
                values[pos+i] = (byte)(data ^ xor);
            }
        }
    }

    [Serializable]
    public class SeitenCrypt : ICrypt
    {
        public override byte Decrypt (Xp3Entry entry, long offset, byte value)
        {
            uint key = entry.Hash ^ (uint)offset;
            if (0 != (key & 2))
            {
                int ecx = (int)key & 0x18;
                value ^= (byte)((key >> ecx) | (key >> (ecx & 8)));
            }
            if (0 != (key & 4))
            {
                value += (byte)key;
            }
            if (0 != (key & 8))
            {
                value -= (byte)(key >> (int)(key & 0x10));
            }
            return value;
        }

        public override void Decrypt (Xp3Entry entry, long offset, byte[] buffer, int pos, int count)
        {
            for (int i = 0; i < count; ++i)
            {
                int shift;
                uint key = entry.Hash ^ (uint)offset;
                byte v = buffer[pos+i];
                if (0 != (key & 2))
                {
                    shift = (int)key & 0x18;
                    uint ebx = key >> shift;
                    shift &= 8;
                    v ^= (byte)(ebx | (key >> shift));
                }
                if (0 != (key & 4))
                {
                    v += (byte)key;
                }
                if (0 != (key & 8))
                {
                    shift = (int)key & 0x10;
                    v -= (byte)(key >> shift);
                }
                buffer[pos+i] = v;
                ++offset;
            }
        }

        public override void Encrypt (Xp3Entry entry, long offset, byte[] values, int pos, int count)
        {
            for (int i = 0; i < count; ++i)
            {
                uint key = entry.Hash ^ (uint)offset;
                if (0 != (key & 8))
                {
                    values[pos+i] += (byte)(key >> (int)(key & 0x10));
                }
                if (0 != (key & 4))
                {
                    values[pos+i] -= (byte)key;
                }
                if (0 != (key & 2))
                {
                    int ecx = (int)key & 0x18;
                    values[pos+i] ^= (byte)((key >> ecx) | (key >> (ecx & 8)));
                }
            }
        }
    }

    [Serializable]
    public class OkibaCrypt : ICrypt
    {
        public override byte Decrypt (Xp3Entry entry, long offset, byte value)
        {
            if (offset < 0x65)
                return (byte)(value ^ (byte)(entry.Hash >> 4));
            uint key = entry.Hash;
            // 0,1,2,3 -> 1,0,3,2
            key = ((key & 0xff0000) << 8) | ((key & 0xff000000) >> 8)
                | ((key & 0xff00) >> 8)   | ((key & 0xff) << 8);
            key >>= 8 * ((int)(offset - 0x65) & 3);
            return (byte)(value ^ (byte)key);
        }

        public override void Decrypt (Xp3Entry entry, long offset, byte[] values, int pos, int count)
        {
            int i = 0;
            if (offset < 0x65)
            {
                uint key = entry.Hash >> 4;
                int limit = Math.Min (count, (int)(0x65 - offset));
                for (; i < limit; ++i)
                {
                    values[pos+i] ^= (byte)key;
                    ++offset;
                }
            }
            if (i < count)
            {
                offset -= 0x65;
                uint key = entry.Hash;
                key = ((key & 0xff0000) << 8) | ((key & 0xff000000) >> 8)
                    | ((key & 0xff00) >> 8)   | ((key & 0xff) << 8);
                do
                {
                    values[pos+i] ^= (byte)(key >> (8 * ((int)offset & 3)));
                    ++offset;
                }
                while (++i < count);
            }
        }

        public override void Encrypt (Xp3Entry entry, long offset, byte[] values, int pos, int count)
        {
            Decrypt (entry, offset, values, pos, count);
        }
    }

    [Serializable]
    public class DieselmineCrypt : ICrypt
    {
        public override byte Decrypt (Xp3Entry entry, long offset, byte value)
        {
            byte key = (byte)entry.Hash;
            if (offset < 123)
                value ^= (byte)(21 * key);
            else if (offset < 246)
                value += (byte)(-32 * key);
            else if (offset < 369)
                value ^= (byte)(43 * key);
            else if (offset <= 0xffffffffL)
                value += (byte)(-54 * key);
            return value;
        }

        public override void Decrypt (Xp3Entry entry, long offset, byte[] values, int pos, int count)
        {
            byte key = (byte)entry.Hash;
            for (int i = 0; i < count && offset <= 0xffffffffL; ++i, ++offset)
            {
                if (offset < 123)
                    values[pos+i] ^= (byte)(21 * key);
                else if (offset < 246)
                    values[pos+i] += (byte)(-32 * key);
                else if (offset < 369)
                    values[pos+i] ^= (byte)(43 * key);
                else
                    values[pos+i] += (byte)(-54 * key);
            }
        }

        public override void Encrypt (Xp3Entry entry, long offset, byte[] values, int pos, int count)
        {
            byte key = (byte)entry.Hash;
            for (int i = 0; i < count && offset <= 0xffffffffL; ++i, ++offset)
            {
                if (offset < 123)
                    values[pos+i] ^= (byte)(21 * key);
                else if (offset < 246)
                    values[pos+i] -= (byte)(-32 * key);
                else if (offset < 369)
                    values[pos+i] ^= (byte)(43 * key);
                else
                    values[pos+i] -= (byte)(-54 * key);
            }
        }
    }

    [Serializable]
    public class DameganeCrypt : ICrypt
    {
        public override byte Decrypt (Xp3Entry entry, long offset, byte value)
        {
            if (0 != (offset & 1))
                return (byte)(value ^ entry.Hash);
            else
                return (byte)(value ^ offset);
        }

        public override void Decrypt (Xp3Entry entry, long offset, byte[] values, int pos, int count)
        {
            for (int i = 0; i < count; ++i, ++offset)
            {
                if (0 != (offset & 1))
                    values[pos+i] ^= (byte)entry.Hash;
                else
                    values[pos+i] ^= (byte)offset;
            }
        }

        public override void Encrypt (Xp3Entry entry, long offset, byte[] values, int pos, int count)
        {
            Decrypt (entry, offset, values, pos, count);
        }
    }

    [Serializable]
    public class NephriteCrypt : ICrypt
    {
        public override byte Decrypt (Xp3Entry entry, long offset, byte value)
        {
            if (0 != (offset & 1))
                return (byte)(value ^ offset);
            else
                return (byte)(value ^ entry.Hash);
        }

        public override void Decrypt (Xp3Entry entry, long offset, byte[] values, int pos, int count)
        {
            for (int i = 0; i < count; ++i, ++offset)
            {
                if (0 != (offset & 1))
                    values[pos+i] ^= (byte)offset;
                else
                    values[pos+i] ^= (byte)entry.Hash;
            }
        }

        public override void Encrypt (Xp3Entry entry, long offset, byte[] values, int pos, int count)
        {
            Decrypt (entry, offset, values, pos, count);
        }
    }

    [Serializable]
    public class AlteredPinkCrypt : ICrypt
    {
        static readonly byte[] KeyTable = {
            0x43, 0xF8, 0xAD, 0x08, 0xDF, 0xB7, 0x26, 0x44, 0xF0, 0xD9, 0xE9, 0x24, 0x1A, 0xC1, 0xEE, 0xB4,
            0x11, 0x4B, 0xE4, 0xAF, 0x01, 0x5B, 0xF0, 0xAB, 0x6A, 0x70, 0x78, 0x84, 0xB0, 0x78, 0x4F, 0xED,
            0x39, 0x52, 0x69, 0xAF, 0xC4, 0x92, 0x2A, 0x21, 0xDE, 0xDC, 0x6E, 0x63, 0x9D, 0x9B, 0x63, 0xE1,
            0xB1, 0x94, 0x40, 0x6E, 0x3A, 0x52, 0x5A, 0x28, 0x08, 0x4D, 0xFB, 0x22, 0x18, 0xEB, 0xBA, 0x98,
            0x49, 0x77, 0xBF, 0xAA, 0x43, 0x75, 0xF5, 0xD3, 0x83, 0x71, 0x58, 0xA4, 0xAF, 0x1B, 0x53, 0x99,
            0x8A, 0x27, 0x5B, 0xC2, 0x7F, 0x7A, 0xCD, 0x8D, 0x33, 0x59, 0xEB, 0xA6, 0xFA, 0x7C, 0x00, 0x19,
            0xC4, 0xAA, 0x24, 0xF8, 0x84, 0xCD, 0xF7, 0x20, 0x4B, 0xAB, 0xF1, 0xD5, 0x01, 0x6F, 0x7C, 0x91,
            0x08, 0x7D, 0x8D, 0x89, 0x7C, 0x71, 0x65, 0x99, 0x9B, 0x6F, 0x3A, 0x1C, 0x49, 0xE3, 0xAF, 0x1F,
            0xC6, 0xA5, 0x79, 0xFE, 0xAE, 0xA1, 0xCA, 0x59, 0x3C, 0xEE, 0xC1, 0x02, 0xBD, 0x2B, 0x8E, 0xC5,
            0x7D, 0x38, 0x80, 0x8F, 0x72, 0xF3, 0x86, 0x5D, 0xF4, 0x20, 0x0A, 0x5B, 0xA0, 0xE3, 0x85, 0xB5,
            0x67, 0x43, 0x96, 0xBB, 0x75, 0x86, 0x8D, 0x7E, 0x7E, 0xE6, 0xAA, 0x18, 0x57, 0xC4, 0xAA, 0x87,
            0xDC, 0x74, 0x05, 0xAA, 0xBD, 0x5E, 0x4F, 0xA9, 0xB5, 0x5E, 0xC5, 0xE8, 0x11, 0x6D, 0x68, 0x89,
            0x17, 0x7C, 0x10, 0x05, 0xA2, 0xBA, 0x43, 0x01, 0xD6, 0xFD, 0x26, 0x19, 0x57, 0xFA, 0x4D, 0x01,
            0xB0, 0xED, 0x3A, 0x55, 0xEB, 0x65, 0x8E, 0xD1, 0x58, 0x27, 0xAD, 0xA1, 0x5E, 0x57, 0x3F, 0xA0,
            0xEF, 0x59, 0x3E, 0xA4, 0xEB, 0x12, 0x15, 0x60, 0xBE, 0x95, 0x61, 0x0B, 0x98, 0xF5, 0xF4, 0x12,
            0x1C, 0xD8, 0x62, 0x3F, 0xFD, 0xCF, 0x01, 0x3A, 0xE7, 0xC2, 0x19, 0x38, 0x6C, 0xC3, 0x90, 0x3E,
        };

        public override byte Decrypt (Xp3Entry entry, long offset, byte value)
        {
            return (byte)(value ^ KeyTable[offset & 0xFF]);
        }

        public override void Decrypt (Xp3Entry entry, long offset, byte[] values, int pos, int count)
        {
            for (int i = 0; i < count; ++i)
            {
                values[pos+i] ^= KeyTable[(offset+i) & 0xFF];
            }
        }

        public override void Encrypt (Xp3Entry entry, long offset, byte[] values, int pos, int count)
        {
            Decrypt (entry, offset, values, pos, count);
        }
    }

    [Serializable]
    public class NatsupochiCrypt : ICrypt
    {
        public override byte Decrypt (Xp3Entry entry, long offset, byte value)
        {
            return (byte)(value ^ (entry.Hash >> 3));
        }

        public override void Decrypt (Xp3Entry entry, long offset, byte[] values, int pos, int count)
        {
            byte key = (byte)(entry.Hash >> 3);
            for (int i = 0; i < count; ++i)
            {
                values[pos+i] ^= key;
            }
        }

        public override void Encrypt (Xp3Entry entry, long offset, byte[] values, int pos, int count)
        {
            Decrypt (entry, offset, values, pos, count);
        }
    }

    [Serializable]
    public class PoringSoftCrypt : ICrypt
    {
        public override byte Decrypt (Xp3Entry entry, long offset, byte value)
        {
            return (byte)~(value ^ (entry.Hash + 1));
        }

        public override void Decrypt (Xp3Entry entry, long offset, byte[] values, int pos, int count)
        {
            byte key = (byte)~(entry.Hash + 1);
            for (int i = 0; i < count; ++i)
            {
                values[pos+i] ^= key;
            }
        }

        public override void Encrypt (Xp3Entry entry, long offset, byte[] values, int pos, int count)
        {
            Decrypt (entry, offset, values, pos, count);
        }
    }

    [Serializable]
    public class AppliqueCrypt : ICrypt
    {
        public override byte Decrypt (Xp3Entry entry, long offset, byte value)
        {
            return offset < 5 ? value : (byte)(value ^ (entry.Hash >> 12));
        }

        public override void Decrypt (Xp3Entry entry, long offset, byte[] values, int pos, int count)
        {
            if (offset < 5)
            {
                int skip = Math.Min (5 - (int)offset, count);
                offset += skip;
                pos += skip;
                count -= skip;
            }
            byte key = (byte)(entry.Hash >> 12);
            for (int i = 0; i < count; ++i)
            {
                values[pos+i] ^= key;
            }
        }

        public override void Encrypt (Xp3Entry entry, long offset, byte[] values, int pos, int count)
        {
            Decrypt (entry, offset, values, pos, count);
        }
    }

    [Serializable]
    public class TokidokiCrypt : ICrypt
    {
        public override bool HashAfterCrypt { get { return true; } }

        public override void Decrypt (Xp3Entry entry, long offset, byte[] values, int pos, int count)
        {
            uint key;
            uint limit = GetParameters (entry, out key);
            for (int i = 0; i < count && offset < limit; ++i, ++offset)
            {
                values[pos+i] ^= (byte)(key >> (((int)offset & 3) << 3));
            }
        }

        public override void Encrypt (Xp3Entry entry, long offset, byte[] values, int pos, int count)
        {
            Decrypt (entry, offset, values, pos, count);
        }

        uint GetParameters (Xp3Entry entry, out uint key)
        {
            var ext = System.IO.Path.GetExtension (entry.Name);
            if (!string.IsNullOrEmpty (ext))
            {
                ext = ext.ToLowerInvariant();
                var ext_bin = new byte[16];
                Encodings.cp932.GetBytes (ext, 0, Math.Min (4, ext.Length), ext_bin, 0);
                key = ~LittleEndian.ToUInt32 (ext_bin, 0);
                if (".asd.tjs.ks".Contains (ext))
                    return entry.UnpackedSize;
            }
            else
                key = uint.MaxValue;
            return Math.Min (entry.UnpackedSize, 0x100u);
        }
    }

    [Serializable]
    public class SourireCrypt : ICrypt
    {
        public override byte Decrypt (Xp3Entry entry, long offset, byte value)
        {
            return (byte)(value ^ entry.Hash ^ 0xCD);
        }

        public override void Decrypt (Xp3Entry entry, long offset, byte[] values, int pos, int count)
        {
            byte key = (byte)(entry.Hash ^ 0xCD);
            for (int i = 0; i < count; ++i)
            {
                values[pos+i] ^= key;
            }
        }

        public override void Encrypt (Xp3Entry entry, long offset, byte[] values, int pos, int count)
        {
            Decrypt (entry, offset, values, pos, count);
        }
    }

    [Serializable]
    public class HibikiCrypt : ICrypt
    {
        public override byte Decrypt (Xp3Entry entry, long offset, byte value)
        {
            if (0 != (offset & 4) || offset <= 0x64)
                return (byte)(value ^ (entry.Hash >> 5));
            else
                return (byte)(value ^ (entry.Hash >> 8));
        }

        public override void Decrypt (Xp3Entry entry, long offset, byte[] buffer, int pos, int count)
        {
            byte key1 = (byte)(entry.Hash >> 5);
            byte key2 = (byte)(entry.Hash >> 8);
            for (int i = 0; i < count; ++i, ++offset)
            {
                if (0 != (offset & 4) || offset <= 0x64)
                    buffer[pos+i] ^= key1;
                else
                    buffer[pos+i] ^= key2;
            }
        }

        public override void Encrypt (Xp3Entry entry, long offset, byte[] buffer, int pos, int count)
        {
            Decrypt (entry, offset, buffer, pos, count);
        }
    }

    [Serializable]
    public class AkabeiCrypt : ICrypt
    {
        private readonly uint m_seed;

        public AkabeiCrypt (uint seed)
        {
            m_seed = seed;
        }

        public override string ToString ()
        {
            return string.Format ("{0}(0x{1:X08})", base.ToString(), m_seed);
        }

        public override byte Decrypt (Xp3Entry entry, long offset, byte value)
        {
            int key_pos = (int)offset & 0x1F;
            var key = GetKey (entry.Hash).ElementAt (key_pos);
            return (byte)(value ^ key);
        }

        public override void Decrypt (Xp3Entry entry, long offset, byte[] buffer, int pos, int count)
        {
            var key = GetKey (entry.Hash).ToArray();
            int key_pos = (int)offset;
            for (int i = 0; i < count; ++i)
            {
                buffer[pos+i] ^= key[key_pos++ & 0x1F];
            }
        }

        public override void Encrypt (Xp3Entry entry, long offset, byte[] buffer, int pos, int count)
        {
            Decrypt (entry, offset, buffer, pos, count);
        }

        internal IEnumerable<byte> GetKey (uint hash)
        {
            hash = (hash ^ m_seed) & 0x7FFFFFFF;
            hash = hash << 31 | hash;
            for (int i = 0; i < 0x20; ++i)
            {
                yield return (byte)hash;
                hash = (hash & 0xFFFFFFFE) << 23 | hash >> 8;
            }
        }
    }

    [Serializable]
    public class HaikuoCrypt : ICrypt
    {
        public override byte Decrypt (Xp3Entry entry, long offset, byte value)
        {
            return (byte)(value ^ entry.Hash ^ (entry.Hash >> 8));
        }

        public override void Decrypt (Xp3Entry entry, long offset, byte[] values, int pos, int count)
        {
            byte key = (byte)(entry.Hash ^ (entry.Hash >> 8));
            for (int i = 0; i < count; ++i)
            {
                values[pos+i] ^= key;
            }
        }

        public override void Encrypt (Xp3Entry entry, long offset, byte[] values, int pos, int count)
        {
            Decrypt (entry, offset, values, pos, count);
        }
    }

    [Serializable]
    public class StripeCrypt : ICrypt
    {
        readonly byte   m_key;

        public StripeCrypt (byte key)
        {
            m_key = key;
        }

        public override string ToString ()
        {
            return string.Format ("{0}(0x{1:X02})", base.ToString(), m_key);
        }

        public override byte Decrypt (Xp3Entry entry, long offset, byte value)
        {
            return (byte)((value ^ m_key) + 1);
        }

        public override void Decrypt (Xp3Entry entry, long offset, byte[] values, int pos, int count)
        {
            for (int i = 0; i < count; ++i)
            {
                values[pos+i] ^= m_key;
                values[pos+i] ++;
            }
        }

        public override void Encrypt (Xp3Entry entry, long offset, byte[] values, int pos, int count)
        {
            for (int i = 0; i < count; ++i)
            {
                values[pos+i] --;
                values[pos+i] ^= m_key;
            }
        }
    }

    [Serializable]
    public class ExaCrypt : ICrypt
    {
        public override byte Decrypt (Xp3Entry entry, long offset, byte value)
        {
            return (byte)(value ^ (entry.Hash >> (int)((uint)offset % 5)));
        }

        public override void Decrypt (Xp3Entry entry, long offset, byte[] values, int pos, int count)
        {
            int shift = (int)(offset % 5);
            for (int i = 0; i < count; ++i)
            {
                values[pos+i] ^= (byte)(entry.Hash >> ((shift+i) % 5));
            }
        }

        public override void Encrypt (Xp3Entry entry, long offset, byte[] values, int pos, int count)
        {
            Decrypt (entry, offset, values, pos, count);
        }
    }

    [Serializable]
    public class SmileCrypt : ICrypt
    {
        readonly uint   m_key_xor;
        readonly byte   m_first_xor;
        readonly byte   m_zero_xor;

        public SmileCrypt (uint key_xor, byte first_xor, byte zero_xor)
        {
            m_key_xor = key_xor;
            m_first_xor = first_xor;
            m_zero_xor = zero_xor;
        }

        public override void Decrypt (Xp3Entry entry, long offset, byte[] values, int pos, int count)
        {
            uint hash = entry.Hash ^ m_key_xor;
            byte key = (byte)(hash ^ (hash >> 8) ^ (hash >> 16) ^ (hash >> 24));
            if (0 == key)
                key = m_zero_xor;
            if (0 == offset && count > 0)
            {
                if (0 == (hash & 0xFF))
                    hash = m_first_xor;
                values[pos] ^= (byte)hash;
            }
            for (int i = 0; i < count; ++i)
            {
                values[pos+i] ^= key;
            }
        }

        public override void Encrypt (Xp3Entry entry, long offset, byte[] values, int pos, int count)
        {
            Decrypt (entry, offset, values, pos, count);
        }
    }

    // reminder:
    // locate virtual table, then find constructor where it's referenced.
    [Serializable]
    public class YuzuCrypt : ICrypt
    {
        public override void Decrypt (Xp3Entry entry, long offset, byte[] data, int pos, int count)
        {
            uint hash = entry.Hash ^ 0x1DDB6E7A;
            byte key = (byte)(hash ^ (hash >> 8) ^ (hash >> 16) ^ (hash >> 24));
            if (0 == key)
                key = 0xD0;
            for (int i = 0; i < count; ++i)
                data[pos+i] ^= key;
        }

        public override void Encrypt (Xp3Entry entry, long offset, byte[] data, int pos, int count)
        {
            Decrypt (entry, offset, data, pos, count);
        }
    }

    [Serializable]
    public class HighRunningCrypt : ICrypt
    {
        public override void Decrypt (Xp3Entry entry, long offset, byte[] data, int pos, int count)
        {
            byte key = (byte)entry.Hash;
            if (0 == key)
                return;
            for (int i = 0; i < count; ++i)
            {
                if ((offset + i) % key != 0)
                    data[pos+i] ^= key;
            }
        }

        public override byte Decrypt (Xp3Entry entry, long offset, byte value)
        {
            byte key = (byte)entry.Hash;
            return key != 0 && offset % key != 0 ? (byte)(value ^ key) : value;
        }

        public override void Encrypt (Xp3Entry entry, long offset, byte[] data, int pos, int count)
        {
            Decrypt (entry, offset, data, pos, count);
        }
    }

    [Serializable]
    public class KissCrypt : CzCrypt
    {
        public override void Decrypt (Xp3Entry entry, long offset, byte[] data, int pos, int count)
        {
            uint key = entry.Hash ^ (entry.Hash >> 19) ^ 0x4A9EEFF0u;
            int i = 0;
            while (0 != ((offset + i) & 0xF))
                ++i;
            while (i < count)
            {
                data[pos+i] ^= (byte)(key ^ (offset + i));
                i += 0x10;
            }
        }

        public override void Encrypt (Xp3Entry entry, long offset, byte[] data, int pos, int count)
        {
            Decrypt (entry, offset, data, pos, count);
        }
    }

    [Serializable]
    public class PuCaCrypt : ICrypt
    {
        public uint[] HashTable;
        public byte[] KeyTable;

        public override void Decrypt (Xp3Entry entry, long offset, byte[] buffer, int pos, int count)
        {
            if (HashTable != null)
            {
                int i = Array.IndexOf (HashTable, entry.Hash);
                if (i != -1)
                {
                    for (int j = 0; j < count; ++j)
                        buffer[pos+j] ^= KeyTable[i];
                    return;
                }
            }
            var hash_table = new byte[32];
            uint hash = entry.Hash;
            for (int k = 0; k < 32; k += 4)
            {
                if (0 != (hash & 1))
                    hash |= 0x80000000;
                else
                    hash &= 0x7FFFFFFF;
                LittleEndian.Pack (hash, hash_table, k);
                hash >>= 1;
            }
            var key_table = new byte[0x400];
            for (int l = 0; l < 32; ++l)
            {
                for (int m = 0; m < 32; ++m)
                    key_table[32 * l + m] = (byte)(~hash_table[l] ^ hash_table[m]);
            }
            for (int n = 0; n < count; ++n)
                buffer[pos+n] ^= key_table[(offset + n) & 0x3FF];
        }

        public override void Encrypt (Xp3Entry entry, long offset, byte[] buffer, int pos, int count)
        {
            Decrypt (entry, offset, buffer, pos, count);
        }
    }

    [Serializable]
    public class RhapsodyCrypt : ICrypt
    {
        public string FileListName { get; set; }

        public override void Decrypt (Xp3Entry entry, long offset, byte[] buffer, int pos, int count)
        {
            var key = new byte[12];
            LittleEndian.Pack (entry.Hash, key, 0);
            LittleEndian.Pack (0x6E1DA9B2u, key, 4);
            LittleEndian.Pack (0x0040C800u, key, 8);
            int k = (int)(offset % 12);
            for (int i = 0; i < count; ++i)
            {
                buffer[pos+i] ^= key[k++];
                if (12 == k)
                    k = 0;
            }
        }

        public override void Encrypt (Xp3Entry entry, long offset, byte[] buffer, int pos, int count)
        {
            Decrypt (entry, offset, buffer, pos, count);
        }

        public override string ReadName (BinaryReader header)
        {
            if (null == KnownNames)
                ReadNames();
            uint key = header.ReadUInt32();
            uint name_hash = header.ReadUInt32() ^ key;
            string name;
            if (KnownNames.TryGetValue (name_hash, out name))
                return name;
            uint ext_hash = header.ReadUInt32() ^ key;
            name = name_hash.ToString ("X8");
            switch (ext_hash)
            {
            case 0x01854675: name += ".png"; break; // GetNameHash (".png")
            case 0x03D435DE: name += ".map"; break; // GetNameHash (".map")
            case 0x2D1F13E0: name += ".asd"; break; // GetNameHash (".asd")
            case 0x482F4319: name += ".tjs"; break; // GetNameHash (".tjs")
            case 0x58924012: name += ".txt"; break; // GetNameHash (".txt")
            case 0xB01C48CA: name += ".ks";  break; // GetNameHash (".ks")
            case 0xC0F7DFB2: name += ".wav"; break; // GetNameHash (".wav")
            case 0xE3A31D19: name += ".jpg"; break; // GetNameHash (".jpg")
            case 0xE7F3FEEB: name += ".ogg"; break; // GetNameHash (".ogg")
            default: name += ext_hash.ToString ("X8"); break;
            }
            return name;
        }

        static uint GetNameHash (string name)
        {
            uint hash = 0;
            for (int i = 0; i < name.Length; ++i)
            {
                int c = char.ToLowerInvariant (name[i]);
                hash = 0x1000193u * hash ^ (byte)c;
                hash = 0x1000193u * hash ^ (byte)(c >> 8);
            }
            return hash;
        }

        void ReadNames ()
        {
            var names = new Dictionary<uint, string>();
            try
            {
                FormatCatalog.Instance.ReadFileList (FileListName, name => {
                    names[GetNameHash (name)] = name;
                });
            }
            catch (Exception X)
            {
                System.Diagnostics.Trace.WriteLine (X.Message, "[RhapsodyCrypt]");
            }
            KnownNames = names;
        }

        [NonSerialized]
        Dictionary<uint, string> KnownNames = null;
    }

    [Serializable]
    public class MadoCrypt : AkabeiCrypt
    {
        public MadoCrypt (uint seed) : base (seed)
        {
        }

        public override byte Decrypt (Xp3Entry entry, long offset, byte value)
        {
            int key_pos = (int)offset % 0x1F;
            var key = GetKey (entry.Hash).ElementAt (key_pos);
            return (byte)(value ^ key);
        }

        public override void Decrypt (Xp3Entry entry, long offset, byte[] buffer, int pos, int count)
        {
            var key = GetKey (entry.Hash).ToArray();
            int key_pos = (int)offset;
            for (int i = 0; i < count; ++i)
            {
                buffer[pos+i] ^= key[key_pos++ % 0x1F];
            }
        }

        public override void Encrypt (Xp3Entry entry, long offset, byte[] buffer, int pos, int count)
        {
            Decrypt (entry, offset, buffer, pos, count);
        }
    }

    [Serializable]
    public class SmxCrypt : ICrypt
    {
        readonly int    Mask;
        readonly byte[] KeySeq;

        public SmxCrypt (int mask, byte[] key_seq)
        {
            if (key_seq.Length <= mask+1)
                throw new ArgumentException ("Not enough arguments for SmxCrypt.");
            KeySeq = key_seq;
            Mask = mask;
        }

        public SmxCrypt (params byte[] key_seq) : this (5, key_seq)
        {
        }

        public override void Decrypt (Xp3Entry entry, long offset, byte[] buffer, int pos, int count)
        {
            byte start_key = (byte)(entry.Hash >> KeySeq[0]);
            var key = GenerateKey (entry.Hash);
            for (int i = 0; i < count; ++i)
            {
                if ((offset + i) <= 100)
                    buffer[pos+i] ^= start_key;
                else
                    buffer[pos+i] ^= key[(int)(offset + i) & Mask];
            }
        }

        public override void Encrypt (Xp3Entry entry, long offset, byte[] buffer, int pos, int count)
        {
            Decrypt (entry, offset, buffer, pos, count);
        }

        public override string ToString ()
        {
            var key_seq = KeySeq != null ? string.Join (",", KeySeq.Select (x => x.ToString ("D"))) : "null";
            return string.Format ("{0}({1})", base.ToString(), key_seq);
        }

        protected byte[] GenerateKey (uint hash)
        {
            var key = new byte[KeySeq.Length - 1];
            for (int i = 1; i < KeySeq.Length; ++i)
                key[i - 1] = (byte)(hash >> KeySeq[i]);
            return key;
        }
    }

    [Serializable]
    public class FestivalCrypt : ICrypt
    {
        public override byte Decrypt (Xp3Entry entry, long offset, byte value)
        {
            return (byte)(value ^ (entry.Hash >> 7) ^ 0xFF);
        }

        public override void Decrypt (Xp3Entry entry, long offset, byte[] data, int pos, int count)
        {
            byte key = (byte)~(entry.Hash >> 7);
            for (int i = 0; i < count; ++i)
            {
                data[pos+i] ^= key;
            }
        }

        public override void Encrypt (Xp3Entry entry, long offset, byte[] data, int pos, int count)
        {
            Decrypt (entry, offset, data, pos, count);
        }
    }

    [Serializable]
    public class PinPointCrypt : ICrypt
    {
        public override void Decrypt (Xp3Entry entry, long offset, byte[] data, int pos, int count)
        {
            for (int i = 0; i < count; ++i)
            {
                byte val = data[pos+i];
                int bit_count = CountSetBits (val);
                if (bit_count > 0)
                {
                    val = Binary.RotByteL (val, bit_count);
                    data[pos+i] = val;
                }
            }
        }

        public override void Encrypt (Xp3Entry entry, long offset, byte[] data, int pos, int count)
        {
            for (int i = 0; i < count; ++i)
            {
                byte val = data[pos+i];
                int bit_count = CountSetBits (val);
                if (bit_count > 0)
                {
                    val = Binary.RotByteR (val, bit_count);
                    data[pos+i] = val;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static int CountSetBits (byte x)
        {
            int bit_count = (x & 0x55) + ((x >> 1) & 0x55);
            bit_count = (bit_count & 0x33) + ((bit_count >> 2) & 0x33);
            return ((bit_count & 0xF) + ((bit_count >> 4) & 0xF)) & 0xF;
        }
    }

    [Serializable]
    public class HybridCrypt : ICrypt
    {
        public override byte Decrypt (Xp3Entry entry, long offset, byte value)
        {
            return (byte)(value ^ (entry.Hash >> 5));
        }

        public override void Decrypt (Xp3Entry entry, long offset, byte[] buffer, int pos, int count)
        {
            byte key = (byte)(entry.Hash >> 5);
            for (int i = 0; i < count; ++i, ++offset)
            {
                buffer[pos+i] ^= key;
            }
        }

        public override void Encrypt (Xp3Entry entry, long offset, byte[] buffer, int pos, int count)
        {
            Decrypt (entry, offset, buffer, pos, count);
        }
    }

    [Serializable]
    public class NekoWorksCrypt : ICrypt
    {
        byte[]  DefaultKey;

        public NekoWorksCrypt (byte[] key)
        {
            DefaultKey = key;
        }

        public override void Decrypt (Xp3Entry entry, long offset, byte[] buffer, int pos, int count)
        {
            var key = InitKey (entry.Hash);
            for (int i = 0; i < count; ++i)
            {
                buffer[pos+i] ^= key[(offset + i) % 31];
            }
        }

        public override void Encrypt (Xp3Entry entry, long offset, byte[] buffer, int pos, int count)
        {
            Decrypt (entry, offset, buffer, pos, count);
        }

        byte[] InitKey (uint hash)
        {
            hash &= 0x7FFFFFFF;
            hash = hash << 31 | hash;
            var key = DefaultKey.Clone() as byte[];
            for (int i = 0; i < 31; ++i)
            {
                key[i] ^= (byte)hash;
                hash = (hash & 0xFFFFFFFE) << 23 | hash >> 8;
            }
            return key;
        }
    }
}
