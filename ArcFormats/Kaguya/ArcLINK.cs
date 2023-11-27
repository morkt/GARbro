﻿//! \file       ArcLINK.cs
//! \date       Fri Jan 22 18:44:56 2016
//! \brief      KaGuYa archive format.
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
using System.ComponentModel.Composition;
using System.IO;
using System.Text;

namespace GameRes.Formats.Kaguya
{
    internal class LinkEntry : PackedEntry
    {
        public bool IsEncrypted;
    }

    internal class LinkArchive : ArcFile
    {
        public readonly LinkEncryption Encryption;

        public LinkArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, LinkEncryption enc)
            : base (arc, impl, dir)
        {
            Encryption = enc;
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class LinkOpener : ArchiveFormat
    {
        public override string         Tag { get { return "ARC/LINK"; } }
        public override string Description { get { return "KaGuYa script engine resource archive"; } }
        public override uint     Signature { get { return 0x4B4E494C; } } // 'LINK'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public LinkOpener ()
        {
            Extensions = new string[] { "arc" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int version = file.View.ReadByte (4) - '0';
            if (version < 3 || version > 6)
                return ReadOldIndex (file);

            using (var reader = LinkReader.Create (file, version))
            {
                var dir = reader.ReadIndex();
                if (null == dir)
                    return null;

                if (reader.HasEncrypted)
                {
                    var enc = reader.GetEncryption();
                    if (enc != null)
                        return new LinkArchive (file, this, dir, enc);
                }
                return new ArcFile (file, this, dir);
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var lent = entry as LinkEntry;
            if (null == lent || (!lent.IsPacked && !lent.IsEncrypted))
            {
                if (entry.Size > 8)
                {
                    uint unpacked_size = arc.File.View.ReadUInt32 (entry.Offset);
                    int id = arc.File.View.ReadUInt16 (entry.Offset+5);
                    if (id == 0x4D42) // 'BM'
                    {
                        using (var input = arc.File.CreateStream (entry.Offset+4, entry.Size-4, entry.Name))
                        {
                            var data = Lin2Opener.UnpackLzss (input, unpacked_size);
                            return new BinMemoryStream (data, entry.Name);
                        }
                    }
                }
                return base.OpenEntry (arc, entry);
            }
            if (lent.IsEncrypted)
            {
                var larc = arc as LinkArchive;
                if (null == larc)
                    return base.OpenEntry (arc, entry);
                return larc.Encryption.DecryptEntry (larc, lent);
            }
            using (var input = arc.File.CreateStream (entry.Offset, entry.Size))
            using (var bmr = new BmrDecoder (input))
            {
                bmr.Unpack();
                return new BinMemoryStream (bmr.Data, entry.Name);
            }
        }

        internal ArcFile ReadOldIndex (ArcView file)
        {
            int count = file.View.ReadInt32 (4);
            if (!IsSaneCount (count))
                return null;

            var dir = new List<Entry> (count);
            using (var index = file.CreateStream())
            {
                index.Position = 8;
                uint names_size = index.ReadUInt32();
                for (int i = 0; i < count; ++i)
                {
                    var name = index.ReadCString();
                    var entry = FormatCatalog.Instance.Create<Entry> (name);
                    dir.Add (entry);
                }
                index.Position = 12 + names_size;
                foreach (var entry in dir)
                {
                    entry.Offset = index.ReadUInt32();
                    entry.Size   = index.ReadUInt32();
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                }
            }
            return new ArcFile (file, this, dir);
        }
    }

    internal class LinkReader : IDisposable
    {
        IBinaryStream   m_input;
        readonly long   m_max_offset;
        bool            m_has_encrypted;

        protected LinkReader (ArcView file)
        {
            m_input = file.CreateStream();
            m_max_offset = file.MaxOffset;
            m_has_encrypted = false;
        }

        public IBinaryStream Input { get { return m_input; } }
        public bool   HasEncrypted { get { return m_has_encrypted; } }

        public static LinkReader Create (ArcView file, int version)
        {
            if (version < 4)
                return new LinkReader (file);
            else if (version < 6)
                return new Link4Reader (file);
            else
                return new Link6Reader (file);
        }

        protected virtual long GetDataOffset ()
        {
            return 8;
        }

        protected virtual string ReadName ()
        {
            int name_length = m_input.ReadUInt8();
            Skip (2);
            return m_input.ReadCString (name_length);
        }

        public List<Entry> ReadIndex ()
        {
            m_input.Position = GetDataOffset();

            var header = new byte[4];
            var dir = new List<Entry>();
            while (m_input.Position + 4 < m_max_offset)
            {
                long base_offset = m_input.Position;
                uint size = m_input.ReadUInt32();
                if (0 == size)
                    break;
                if (size < 0x10)
                    return null;
                int flags = m_input.ReadUInt16 ();
                bool is_compressed = (flags & 3) != 0;
                Skip (7);
                var name = ReadName();
                if (string.IsNullOrEmpty (name))
                    return null;
                var entry = FormatCatalog.Instance.Create<LinkEntry> (name);
                entry.Offset = m_input.Position;
                entry.Size   = size - (uint)(entry.Offset - base_offset);
                if (is_compressed)
                {
                    m_input.Read (header, 0, 4);
                    if (header.AsciiEqual ("BMR"))
                    {
                        entry.IsPacked = true;
                        entry.UnpackedSize = m_input.ReadUInt32();
                    }
                }
                entry.IsEncrypted = (flags & 4) != 0;
                m_has_encrypted = m_has_encrypted || entry.IsEncrypted;
                dir.Add (entry);
                m_input.Position = entry.Offset + entry.Size;
            }
            return dir;
        }

        public virtual LinkEncryption GetEncryption ()
        {
            var params_dat = VFS.ChangeFileName (m_input.Name, "params.dat");
            if (!VFS.FileExists (params_dat))
                return null;

            using (var input = VFS.OpenBinaryStream (params_dat))
            {
                var param = ParamsDeserializer.Create (input);
                return param.GetEncryption();
            }
        }

        protected void Skip (int amount)
        {
            m_input.Seek (amount, SeekOrigin.Current);
        }

        #region IDisposable Members
        public void Dispose ()
        {
            Dispose (true);
            GC.SuppressFinalize (this);
        }

        bool _disposed = false;
        protected virtual void Dispose (bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                    m_input.Dispose();
                _disposed = true;
            }
        }
        #endregion
    }

    internal class Link4Reader : LinkReader
    {
        public Link4Reader (ArcView file) : base (file)
        {
        }

        protected override long GetDataOffset ()
        {
            return 0xA;
        }
    }

    internal class Link6Reader : LinkReader
    {
        public Link6Reader (ArcView file) : base (file)
        {
        }

        protected override long GetDataOffset ()
        {
            Input.Position = 7;
            return 8 + Input.ReadUInt8();
        }

        byte[] name_buffer = new byte[0x100];

        protected override string ReadName ()
        {
            int name_length = Input.ReadUInt16();
            if (name_length > 0x400)
                throw new InvalidFormatException();
            if (name_length > name_buffer.Length)
                name_buffer = new byte[name_length];
            Input.Read (name_buffer, 0, name_length);
            return Encoding.Unicode.GetString (name_buffer, 0, name_length);
        }
    }

    internal class BmrDecoder : IDisposable
    {
        byte[]          m_output;
        MsbBitStream    m_input;
        int             m_final_size;
        int             m_step;
        int             m_key;

        public byte[] Data { get { return m_output; } }

        public BmrDecoder (IBinaryStream input)
        {
            input.Position = 3;
            m_step = input.ReadUInt8();
            m_final_size = input.ReadInt32();
            m_key = input.ReadInt32();
            int unpacked_size = input.ReadInt32();
            m_output = new byte[unpacked_size];
            m_input = new MsbBitStream (input.AsStream, true);
        }

        public void Unpack ()
        {
            m_input.Input.Position = 0x14;
            UnpackHuffman();
            UndoMoveToFront();
            m_output = Decode (m_output, m_key);
            if (m_step != 0)
                m_output = DecompressRLE (m_output);
        }

        byte[] DecompressRLE (byte[] input)
        {
            var result = new byte[m_final_size];
            int src = 0;
            for (int i = 0; i < m_step; ++i)
            {
                byte v1 = input[src++];
                result[i] = v1;
                int dst = i + m_step;
                while (dst < result.Length)
                {
                    byte v2 = input[src++];
                    result[dst] = v2;
                    dst += m_step;
                    if (v2 == v1)
                    {
                        int count = input[src++];
                        if (0 != (count & 0x80))
                            count = input[src++] + ((count & 0x7F) << 8) + 128;
                        while (count --> 0 && dst < result.Length)
                        {
                            result[dst] = v2;
                            dst += m_step;
                        }
                        if (dst < result.Length)
                        {
                            v2 = input[src++];
                            result[dst] = v2;
                            dst += m_step;
                        }
                    }
                    v1 = v2;
                }
            }
            return result;
        }


        void UndoMoveToFront ()
        {
            var dict = new byte[256];
            for (int i = 0; i < 256; ++i)
                dict[i] = (byte)i;
            for (int i = 0; i < m_output.Length; ++i)
            {
                byte v = m_output[i];
                m_output[i] = dict[v];
                for (int j = v; j > 0; --j)
                {
                    dict[j] = dict[j-1];
                }
                dict[0] = m_output[i];
            }
        }

        byte[] Decode (byte[] input, int key)
        {
            var freq_table = new int[256];
            for (int i = 0; i < input.Length; ++i)
            {
                ++freq_table[input[i]];
            }
            for (int i = 1; i < 256; ++i)
            {
                freq_table[i] += freq_table[i-1];
            }
            var distrib_table = new int[input.Length];
            for (int i = input.Length-1; i >= 0; --i)
            {
                int v = input[i];
                int freq = --freq_table[v];
                distrib_table[freq] = i;
            }
            int pos = key;
            var copy_out = new byte[input.Length];
            for (int i = 0; i < copy_out.Length; ++i)
            {
                pos = distrib_table[pos];
                copy_out[i] = input[pos];
            }
            return copy_out;
        }

        ushort      m_token;
        ushort[,]   m_tree = new ushort[2,256];

        void UnpackHuffman ()
        {
            m_token = 256;
            ushort root = CreateHuffmanTree();
            int dst = 0;
            while (dst < m_output.Length)
            {
                ushort symbol = root;
                while (symbol >= 0x100)
                {
                    int bit = m_input.GetNextBit();
                    if (-1 == bit)
                        throw new EndOfStreamException();
                    symbol = m_tree[bit,symbol-256];
                }
                m_output[dst++] = (byte)symbol;
            }
        }

        ushort CreateHuffmanTree ()
        {
            if (0 != m_input.GetNextBit())
            {
                ushort v = m_token++;
                m_tree[0,v-256] = CreateHuffmanTree();
                m_tree[1,v-256] = CreateHuffmanTree();
                return v;
            }
            else
            {
                return (ushort)m_input.GetBits (8);
            }
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

    internal abstract class ParamsDeserializer
    {
        protected IBinaryStream     m_input;
        protected string            m_title;
        protected Version           m_version;

        protected ParamsDeserializer (IBinaryStream input, Version version)
        {
            m_input = input;
            m_version = version;
        }

        public static ParamsDeserializer Create (IBinaryStream input)
        {
            var header = input.ReadHeader (0x11);
            if (header.AsciiEqual ("[SCR-PARAMS]v0"))
            {
                Version version;
                if ('.' == header[15])
                    version = Version.Parse (header.GetCString (13, 4));
                else
                    version = new Version (header[14] - '0', 0);
                if (2 == version.Major)
                    return new ParamsV2Deserializer (input, version);
                else if (version.Major < 5)
                    return new ParamsV4Deserializer (input, version);
                else if (5 == version.Major && (version.Minor >= 4 && version.Minor <= 7))
                    return new ParamsV5Deserializer (input, version);
            }
            throw new UnknownEncryptionScheme();
        }

        public virtual LinkEncryption GetEncryption ()
        {
            return new LinkEncryption (GetKey());
        }

        public abstract byte[] GetKey ();

        protected byte[] ReadKey ()
        {
            int key_length = m_input.ReadInt32();
            return m_input.ReadBytes (key_length);
        }

        protected virtual string ReadString ()
        {
            int length = m_input.ReadUInt8();
            return m_input.ReadCString (length);
        }

        protected virtual void SkipString ()
        {
            SkipChunk();
        }

        protected void Skip (int amount)
        {
            m_input.Seek (amount, SeekOrigin.Current);
        }

        protected void SkipChunk ()
        {
            Skip (m_input.ReadUInt8());
        }

        protected void SkipArray ()
        {
            int count = m_input.ReadUInt8();
            for (int i = 0; i < count; ++i)
                SkipChunk();
        }

        protected void SkipDict ()
        {
            int count = m_input.ReadUInt8();
            for (int i = 0; i < count; ++i)
            {
                SkipString();
                SkipString();
            }
        }

        protected void ReadHeader (int start)
        {
            m_input.Position = start;
            SkipChunk();
            m_title = ReadString();
            if (m_version.Major < 2)
                m_input.ReadCString();
//            else
//                SkipString();
            SkipString();
            SkipString();
            m_input.ReadByte();
            SkipString();
            SkipString();
            SkipDict();
            m_input.ReadByte();
        }
    }

    internal class ParamsV2Deserializer : ParamsDeserializer
    {
        public ParamsV2Deserializer (IBinaryStream input, Version version) : base (input, version)
        {
        }

        public override LinkEncryption GetEncryption ()
        {
            var key = GetKey();
            return new LinkEncryption (key, m_title != "幼なじみと甘～くエッチに過ごす方法");
        }

        public override byte[] GetKey ()
        {
            ReadHeader (0x17);

            if ("幼なじみと甘～くエッチに過ごす方法" == m_title || "艶女医" == m_title)
            {
                int count = m_input.ReadUInt8();
                for (int i = 0; i < count; ++i)
                {
                    m_input.ReadByte();
                    SkipChunk();
                    SkipArray();
                    SkipChunk();
                }
                SkipArray();
                SkipArray();
                if ("幼なじみと甘～くエッチに過ごす方法" == m_title)
                {
                    m_input.ReadInt32();
                    return m_input.ReadBytes (240000);
                }
                else
                {
                    return ReadKey();
                }
            }
            else // 毎日がＭ！
            {
                int count = m_input.ReadUInt8();
                for (int i = 0; i < count; ++i)
                {
                    m_input.ReadByte();
                    SkipChunk();
                    SkipArray();
                    SkipArray();
                }
                SkipDict();
                count = m_input.ReadUInt8();
                for (int i = 0; i < count; ++i)
                {
                    SkipChunk();
                    SkipArray();
                    SkipArray();
                }
                return ReadKey();
            }
        }
    }

    internal class ParamsV4Deserializer : ParamsDeserializer
    {
        public ParamsV4Deserializer (IBinaryStream input, Version version) : base (input, version)
        {
        }

        public override byte[] GetKey ()
        {
            ReadHeader (0x19);

            Skip (m_version.Major < 5 ? 12 : 11);
            int count = m_input.ReadUInt8();
            for (int i = 0; i < count; ++i)
            {
                m_input.ReadByte();
                SkipChunk();
                SkipArray();
                SkipArray();
            }
            SkipDict();
            count = m_input.ReadUInt8();
            for (int i = 0; i < count; ++i)
            {
                SkipChunk();
                SkipArray();
                SkipArray();
            }
            return ReadKey();
        }
    }

    internal class ParamsV5Deserializer : ParamsDeserializer
    {
        public ParamsV5Deserializer (IBinaryStream input, Version version) : base (input, version)
        {
        }

        public override byte[] GetKey ()
        {
            ReadHeader (0x1B);

            Skip (m_version.Minor <= 4 ? 15 : 16);
            for (int i = 0; i < 3; ++i)
            {
                if (0 != m_input.ReadUInt8())
                    SkipTree();
            }
            Skip (m_input.ReadInt32() * 0xC);
            return ReadKey();
        }

        protected override void SkipString ()
        {
            int length = m_input.ReadUInt16();
            Skip (length);
        }

        byte[] name_buffer = new byte[0x100];

        protected override string ReadString ()
        {
            int length = m_input.ReadUInt16();
            if (length > name_buffer.Length)
                name_buffer = new byte[length];
            m_input.Read (name_buffer, 0, length);
            return Encoding.Unicode.GetString (name_buffer, 0, length);
        }

        protected void SkipTree ()
        {
            SkipString();
            int count = m_input.ReadInt32();
            while (count --> 0)
            {
                SkipString();
                SkipString();
            }
            count = m_input.ReadInt32();
            while (count --> 0)
                SkipTree();
        }
    }

    internal class LinkEncryption
    {
        byte[]   m_key;
        Tuple<string, Decryptor>[] m_type_table;

        delegate Stream Decryptor (LinkArchive arc, LinkEntry entry);

        static readonly ResourceInstance<AnmOpener>  An00 = new ResourceInstance<AnmOpener> ("ANM/KAGUYA");
        static readonly ResourceInstance<An10Opener> An10 = new ResourceInstance<An10Opener> ("AN10/KAGUYA");
        static readonly ResourceInstance<An20Opener> An20 = new ResourceInstance<An20Opener> ("AN20/KAGUYA");
        static readonly ResourceInstance<Pl00Opener> Pl00 = new ResourceInstance<Pl00Opener> ("PLT/KAGUYA");

        public LinkEncryption (byte[] key, bool anm_encrypted = true)
        {
            if (null == key || 0 == key.Length)
                throw new ArgumentException ("Invalid encryption key");
            m_key = key;
            var table = new List<Tuple<string, Decryptor>>
            {
                new Tuple<string, Decryptor> ("BM",     (a, e) => DecryptImage (a, e, 0x36)),
                new Tuple<string, Decryptor> ("AP-2",   (a, e) => DecryptImage (a, e, 0x18)),
                new Tuple<string, Decryptor> ("AP-3",   (a, e) => DecryptImage (a, e, 0x18)),
                new Tuple<string, Decryptor> ("AP",     (a, e) => DecryptImage (a, e, 0xC)),
            };
            if (anm_encrypted)
            {
                table.Add (new Tuple<string, Decryptor> ("AN00", (a, e) => DecryptAnm (a, e, An00.Value)));
                table.Add (new Tuple<string, Decryptor> ("AN10", (a, e) => DecryptAnm (a, e, An10.Value)));
                table.Add (new Tuple<string, Decryptor> ("AN20", (a, e) => DecryptAnm (a, e, An20.Value)));
                table.Add (new Tuple<string, Decryptor> ("AN21", (a, e) => DecryptAn21 (a, e)));
                table.Add (new Tuple<string, Decryptor> ("PL00", (a, e) => DecryptAnm (a, e, Pl00.Value)));
                table.Add (new Tuple<string, Decryptor> ("PL10", (a, e) => DecryptPl10 (a, e)));
            }
            m_type_table = table.ToArray();
        }

        public Stream DecryptEntry (LinkArchive arc, LinkEntry entry)
        {
            var header = arc.File.View.ReadBytes (entry.Offset, 4);
            foreach (var type in m_type_table)
            {
                if (header.AsciiEqual (type.Item1))
                    return type.Item2 (arc, entry);
            }
            return arc.File.CreateStream (entry.Offset, entry.Size);
        }

        Stream DecryptImage (LinkArchive arc, LinkEntry entry, uint data_offset)
        {
            var header = arc.File.View.ReadBytes (entry.Offset, data_offset);
            Stream body = arc.File.CreateStream (entry.Offset+data_offset, entry.Size-data_offset);
            body = new ByteStringEncryptedStream (body, m_key);
            return new PrefixStream (header, body);
        }

        Stream DecryptAnm (LinkArchive arc, LinkEntry entry, IAnmReader reader)
        {
            var data = arc.File.View.ReadBytes (entry.Offset, entry.Size);
            var input = new BinMemoryStream (data, entry.Name);
            var dir = reader.GetFramesList (input);
            if (dir != null)
            {
                foreach (AnmEntry frame in dir)
                {
                    DecryptData (data, (int)frame.ImageDataOffset, (int)frame.ImageDataSize);
                }
            }
            input.Position = 0;
            return input;
        }

        Stream DecryptAn21 (LinkArchive arc, LinkEntry entry)
        {
            var data = arc.File.View.ReadBytes (entry.Offset, entry.Size);
            int count = data.ToUInt16 (4);
            int offset = 8;
            for (int i = 0; i < count; ++i)
            {
                switch (data[offset++])
                {
                case 0: break;
                case 1: offset += 8; break;
                case 2:
                case 3:
                case 4:
                case 5: offset += 4; break;
                default: return new BinMemoryStream (data, entry.Name);
                }
            }
            count = data.ToUInt16 (offset);
            offset += 2 + count * 8 + 0x21;
            int w = data.ToInt32 (offset);
            int h = data.ToInt32 (offset+4);
            int channels = data.ToInt32 (offset+8);
            offset += 12;
            DecryptData (data, offset, channels * w * h);
            return new BinMemoryStream (data, entry.Name);
        }

        Stream DecryptPl10 (LinkArchive arc, LinkEntry entry)
        {
            var data = arc.File.View.ReadBytes (entry.Offset, entry.Size);
            int offset = 30;
            int w = data.ToInt32 (offset);
            int h = data.ToInt32 (offset+4);
            int channels = data.ToInt32 (offset+8);
            offset += 12;
            DecryptData (data, offset, channels * w * h);
            return new BinMemoryStream (data, entry.Name);
        }

        void DecryptData (byte[] data, int index, int length)
        {
            while (length > 0)
            {
                int count = Math.Min (length, m_key.Length);
                for (int i = 0; i < count; ++i)
                {
                    data[index++] ^= m_key[i];
                }
                length -= count;
            }
        }
    }
}
