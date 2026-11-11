//! \file       ArcDX8.cs
//! \date       2022 Jun 05
//! \brief      DxLib archive version 8.
//
// Copyright (C) 2022 by morkt
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

using GameRes.Formats.Strings;
using System;
using System.CodeDom;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Runtime.Remoting.Messaging;
using System.Text;
using System.Windows.Navigation;
using static GameRes.Formats.DxLib.Dx8Opener;




namespace GameRes.Formats.DxLib
{

    internal class DXA8PackedEntry : PackedEntry {
        public bool HuffmanCompressed { get; set; }
        public uint HuffmanSize { get; set; }

        public uint LZSize { get; set; }
    }

    internal class DxArchive8 : DxArchive
    {
        public byte huffmanMaxKB;

        public DxArchive8(ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, IDxKey enc, int version,byte huffmanKB) : base(arc, impl, dir, enc, version)
        {
            huffmanMaxKB = huffmanKB;
        }
    }

    internal class DxHeaderV8 : DxHeader
    {
        public DXA8Flags Flags;
        public byte HuffmanKB; // oddly used only in Compression process not in decompression.
                               //15 bytes of padding.
    }

    [Serializable]
    public class Dx8Scheme : ResourceScheme {
        public IDictionary<string, IDxKey> KnownKeys;
    }

    [Export(typeof(ArchiveFormat))]
    public class Dx8Opener : DxOpener
    {
        public override string         Tag { get { return "DXA8"; } }
        public override string Description { get { return "DxLib archive version 8"; } }
        public override uint     Signature { get { return 0x00085844; } }
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public Dx8Opener ()
        {
            Extensions = new string[] { "dxa", "hud", "usi", "med", "dat", "bin", "bcx", "wolf" };
            Signatures = new[] { 0x00085844u };
        }

        //static readonly byte[] DefaultKey = new byte[] { 0xBE, 0xC8, 0x8A, 0xF5, 0x28, 0x50, 0xC9 };


        Dx8Scheme DefaultScheme = new Dx8Scheme { KnownKeys = new Dictionary<string, IDxKey>() };

        public override ResourceScheme Scheme {
            get { return DefaultScheme; }
            set { DefaultScheme = (Dx8Scheme)value; }
        }


        internal enum DXA8Flags : UInt32
        {
            DXA_FLAG_NO_KEY=1, //file is not encrypted
            DXA_FLAG_NO_HEAD_PRESS=1<<1, //do not compress the header after compressing individual entries
        }

        [Serializable]
        public class DXAOpts : ResourceOptions
        {
            public byte[] Keyword;
        }

        public override ResourceOptions GetDefaultOptions()
        {
            return new DXAOpts
            {
                Keyword = Encoding.ASCII.GetBytes(Properties.Settings.Default.DXAPassword)
            };
        }

        
        

        public override ResourceOptions GetOptions(object widget)
        {
            if (widget is GUI.WidgetDXA)
            {
                var w = (GUI.WidgetDXA)widget;
                byte[] keyword;
                if (w.Title.SelectedIndex != -1)
                {
                    keyword = ((KeyValuePair<string, IDxKey>)(w.Title.SelectedItem)).Value.Password;
                }
                else
                {
                    keyword = Encoding.ASCII.GetBytes(w.Password.Text);
                }
                return new DXAOpts
                {
                    Keyword = keyword
                };
            }
            return GetDefaultOptions();
        }

        public override object GetAccessWidget()
        {
            return new GUI.WidgetDXA(DefaultScheme.KnownKeys);
        }

        public override ArcFile TryOpen (ArcView file)
        {
            var dx = new DxHeaderV8 {
                IndexSize  = file.View.ReadUInt32 (4),
                BaseOffset = file.View.ReadInt64 (8),
                IndexOffset = file.View.ReadInt64 (0x10),
                FileTable  = file.View.ReadInt64 (0x18),
                DirTable   = file.View.ReadInt64 (0x20),
                CodePage   = file.View.ReadInt32 (0x28),
                Flags      = (DXA8Flags)file.View.ReadUInt32(0x2C),
                HuffmanKB = file.View.ReadByte(0x30)
            };
            if (dx.DirTable >= dx.IndexSize || dx.FileTable >= dx.IndexSize)
                return null;
            IDxKey key = null;
            
            //FIXME: ReadBytes sets hard cap of filesize to 4GB.
            var headerBuffer = file.View.ReadBytes(dx.IndexOffset, (uint)(file.MaxOffset-dx.IndexOffset));
            bool isencrypted = (dx.Flags & DXA8Flags.DXA_FLAG_NO_KEY) == 0;
           
            if (isencrypted)
            {
                var keyStr = Query<DXAOpts>(arcStrings.ZIPEncryptedNotice).Keyword;
                key = new DxKey8(keyStr,dx.CodePage);

                
            }

            Decrypt(headerBuffer, 0, headerBuffer.Length, 0, key.Key);
            //Decrypted but might be compressed
            if ((dx.Flags & DXA8Flags.DXA_FLAG_NO_HEAD_PRESS) == 0)
            {
                byte[] huffmanBuffer = new byte[headerBuffer.Length]; 
                byte[] lzBuffer;
                headerBuffer.CopyTo(huffmanBuffer, 0);
                //huffmanBuffer = headerBuffer;
                HuffmanDecoder decoder = new HuffmanDecoder(huffmanBuffer, (ulong)huffmanBuffer.LongLength);
                lzBuffer = decoder.Unpack();
                MemoryStream lzStream = new MemoryStream(lzBuffer);
                headerBuffer = Unpack(lzStream);
                
            }

            
            List<Entry> entries;
            //There MAY be the case where the singular file is over 4GB, but it's very rare.
            using (var reader = IndexReader.Create(dx, 8, new MemoryStream(headerBuffer)))
            {
                entries = reader.Read();
            }
            return new DxArchive8(file, this,entries ,key, 8,dx.HuffmanKB);
            //retu rn null;
        }

        public override Stream OpenEntry(ArcFile arc, Entry entry)
        {
            Stream input = arc.File.CreateStream(entry.Offset, entry.Size);
            var dx_arc = arc as DxArchive8;
            if (null == dx_arc)
                return input;
            var dx_ent = (DXA8PackedEntry)entry;
            long dec_offset =  dx_ent.UnpackedSize; 
            var key = dx_arc.Encryption.GetEntryKey(dx_ent.Name);
            input = new EncryptedStream(input, dec_offset, key);

            byte[] tmpBuffer = new byte[dx_ent.Size]; 
            input.Read(tmpBuffer, 0, tmpBuffer.Length);
            if (dx_ent.HuffmanCompressed)
            {
                byte[] buffer = new byte[dx_ent.HuffmanSize];
                byte[] outBuffer = new byte[dx_ent.IsPacked ? dx_ent.LZSize : dx_ent.UnpackedSize];
                Array.Copy(tmpBuffer, buffer, dx_ent.HuffmanSize);
                HuffmanDecoder decoder = new HuffmanDecoder(buffer,dx_ent.HuffmanSize);
                byte[] partTmpBuffer = decoder.Unpack();
                //returned buffer might be partial. Check if this is the case.
                var outBufSize = dx_ent.IsPacked ? dx_ent.LZSize : dx_ent.UnpackedSize;
                if(dx_arc.huffmanMaxKB != 0xff && outBufSize > dx_arc.huffmanMaxKB * 1024 * 2)
                {
                    //What we have here is two huffmanMaxKB KB buffers, that constitute the beginning and end of file respectively.
                    Array.Copy(partTmpBuffer,0, outBuffer, 0,dx_arc.huffmanMaxKB*1024);
                    Array.Copy(partTmpBuffer,dx_arc.huffmanMaxKB*1024,outBuffer,outBuffer.Length-dx_arc.huffmanMaxKB*1024,dx_arc.huffmanMaxKB*1024);
                    //uncompressed part goes into middle.
                    Array.Copy(tmpBuffer, dx_ent.HuffmanSize, outBuffer, dx_arc.huffmanMaxKB * 1024, outBufSize - dx_arc.huffmanMaxKB * 1024 * 2);
                    tmpBuffer = outBuffer;
                } else
                {
                    //that is all that needs to be done.
                    tmpBuffer = partTmpBuffer;
                }
            }
            if(dx_ent.IsPacked)
            {
                byte[] buffer = new byte[dx_ent.LZSize];
                tmpBuffer.CopyTo(buffer, 0);
                var tmpMemStream = new MemoryStream(buffer);
                tmpBuffer = Unpack(tmpMemStream);

            }
            return new BinMemoryStream(tmpBuffer, entry.Name);

            /*
            if (!dx_ent.IsPacked)
                return input;
            using (input)
            {
                var data = Unpack(input);
                return new BinMemoryStream(data, entry.Name);
            }
            */
            //return null;
        }
    }

    internal sealed class IndexReaderV8 : IndexReader
    {
        readonly int m_entry_size;
        public IndexReaderV8(DxHeader header, int version, Stream input) : base(header, version, input)
        {
            m_entry_size = 0x48;
        }
        private class DxDirectory
        {
            public long DirOffset;
            public long ParentDirOffset;
            public int FileCount;
            public long FileTable;
        }

        DxDirectory ReadDirEntry()
        {
            var dir = new DxDirectory
            {
                DirOffset = m_input.ReadInt64(),
                ParentDirOffset = m_input.ReadInt64(),
                FileCount = (int)m_input.ReadInt64(),
                FileTable = m_input.ReadInt64()
            };
            return dir;
        }

        protected override void ReadFileTable(string root, long table_offset)
        {
            m_input.Position = m_header.DirTable + table_offset;
            var dir = ReadDirEntry();
            if (dir.DirOffset != -1 && dir.ParentDirOffset != -1)
            {
                m_input.Position = m_header.FileTable + dir.DirOffset;
                root = Path.Combine(root, ExtractFileName(m_input.ReadInt64()));
            }
            long current_pos = m_header.FileTable + dir.FileTable;
            for (int i = 0; i < dir.FileCount; ++i)
            {
                m_input.Position = current_pos;
                var name_offset = m_input.ReadInt64();
                uint attr = (uint)m_input.ReadInt64();
                m_input.Seek(0x18, SeekOrigin.Current);
                var offset = m_input.ReadInt64();
                if (0 != (attr & 0x10)) // FILE_ATTRIBUTE_DIRECTORY
                {
                    if (0 == offset || table_offset == offset)
                        throw new InvalidFormatException("Infinite recursion in DXA directory index");
                    ReadFileTable(root, offset);
                }
                else
                {
                    var size = m_input.ReadInt64();
                    var packed_size = m_input.ReadInt64();
                    var huffman_packed_size = m_input.ReadInt64();
                    var entry = FormatCatalog.Instance.Create<DXA8PackedEntry>(Path.Combine(root, ExtractFileName(name_offset)));
                    entry.Offset = m_header.BaseOffset + offset;
                    entry.UnpackedSize = (uint)size;
                    entry.IsPacked = -1 != packed_size;
                    entry.HuffmanCompressed = -1 != huffman_packed_size;
                    entry.HuffmanSize = (uint)huffman_packed_size;
                    entry.LZSize = (uint)packed_size;
                    //Huffman compression: huffman_packed_size will not exceed 2*HuffmanKB KB. The rest of data is uncompressed (as far as Huffman compressor is concerned).
                    //Add length of uncompressed data to entry.Size
                    if (entry.HuffmanCompressed)
                    {
                        var outBufSize = entry.IsPacked ? packed_size : size;
                        var dx8_hdr = (DxHeaderV8)m_header;
                        if (outBufSize > dx8_hdr.HuffmanKB * 1024 * 2)
                        {
                            huffman_packed_size += outBufSize - dx8_hdr.HuffmanKB * 1024 * 2;
                        }
                    }
                    if (entry.IsPacked||entry.HuffmanCompressed)
                        entry.Size = (uint)(huffman_packed_size!=-1 ? huffman_packed_size:packed_size);
                    else
                        entry.Size = (uint)size;
                    m_dir.Add(entry);
                }
                current_pos += m_entry_size;
            }
        }
    }
}
