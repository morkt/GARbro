//! \file       ArcAil.cs
//! \date       Mon Apr 20 21:21:49 2015
//! \brief      Ail resource archives.
//
// Copyright (C) 2015 by morkt
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

using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Utility;

namespace GameRes.Formats.Ail
{
    [Export(typeof(ArchiveFormat))]
    [ExportMetadata("Priority", -1)]
    public class DatOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DAT/Ail"; } }
        public override string Description { get { return "Ail resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public DatOpener ()
        {
            Extensions = new string[] { "dat", "snl" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (0);
            if (!IsSaneCount (count))
                return null;
            var dir = ReadIndex (file, 4, count);
            if (null == dir)
                return null;
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            var pentry = entry as PackedEntry;
            if (null == pentry || !pentry.IsPacked)
                return input;
            using (input)
            {
                byte[] data = new byte[pentry.UnpackedSize];
                LzssUnpack (input, data);
                return new BinMemoryStream (data, entry.Name);
            }
        }

        internal List<Entry> ReadIndex (ArcView file, uint index_offset, int count)
        {
            var base_name = Path.GetFileNameWithoutExtension (file.Name);
            long offset = index_offset + count*4;
            if (offset >= file.MaxOffset)
                return null;
            var dir = new List<Entry> (count/2);
            for (int i = 0; i < count; ++i)
            {
                uint size = file.View.ReadUInt32 (index_offset);
                if (size != 0 && size != uint.MaxValue)
                {
                    var entry = new PackedEntry
                    {
                        Name = string.Format ("{0}#{1:D5}", base_name, i),
                        Offset = offset,
                        Size = size,
                        IsPacked = false,
                    };
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);
                    offset += size;
                }
                index_offset += 4;
            }
            if (0 == dir.Count || (file.MaxOffset - offset) > 0x80000)
                return null;
            DetectFileTypes (file, dir);
            return dir;
        }

        internal void DetectFileTypes (ArcView file, List<Entry> dir)
        {
            byte[] preview = new byte[16];
            byte[] sign_buf = new byte[4];
            foreach (PackedEntry entry in dir)
            {
                uint extra = 6;
                if (extra > entry.Size)
                    continue;
                uint signature = file.View.ReadUInt32 (entry.Offset);
                if (1 == (signature & 0xFFFF))
                {
                    entry.IsPacked = true;
                    entry.UnpackedSize = file.View.ReadUInt32 (entry.Offset+2);
                }
                else if (0 == signature || file.View.AsciiEqual (entry.Offset+4, "OggS"))
                {
                    extra = 4;
                }
                entry.Offset += extra;
                entry.Size   -= extra;
                if (entry.IsPacked)
                {
                    file.View.Read (entry.Offset, preview, 0, (uint)preview.Length);
                    using (var input = new MemoryStream (preview))
                    {
                        LzssUnpack (input, sign_buf);
                        signature = LittleEndian.ToUInt32 (sign_buf, 0);
                    }
                }
                else
                {
                    signature = file.View.ReadUInt32 (entry.Offset);
                }
                if (0 != signature)
                    SetEntryType (entry, signature);
            }
        }

        static void SetEntryType (Entry entry, uint signature)
        {
            if (0xBA010000 == signature)
            {
                entry.Type = "video";
                entry.Name = Path.ChangeExtension (entry.Name, "mpg");
            }
            else
            {
                var res = AutoEntry.DetectFileType (signature);
                if (null != res)
                    entry.ChangeType (res);
            }
        }

        /// <summary>
        /// Custom LZSS decompression with frame pre-initialization and reveresed control bits meaning.
        /// </summary>
        static void LzssUnpack (Stream input, byte[] output)
        {
            int frame_pos = 0xfee;
            byte[] frame = new byte[0x1000];
            for (int i = 0; i < frame_pos; ++i)
                frame[i] = 0x20;
            int dst = 0;
            int ctl = 0;

            while (dst < output.Length)
            {
                ctl >>= 1;
                if (0 == (ctl & 0x100))
                {
                    ctl = input.ReadByte();
                    if (-1 == ctl)
                        break;
                    ctl |= 0xff00;
                }
                if (0 == (ctl & 1))
                {
                    int v = input.ReadByte();
                    if (-1 == v)
                        break;
                    output[dst++] = (byte)v;
                    frame[frame_pos++] = (byte)v;
                    frame_pos &= 0xfff;
                }
                else
                {
                    int offset = input.ReadByte();
                    if (-1 == offset)
                        break;
                    int count = input.ReadByte();
                    if (-1 == count)
                        break;
                    offset |= (count & 0xf0) << 4;
                    count   = (count & 0x0f) + 3;

                    for (int i = 0; i < count; i++)
                    {	
                        if (dst >= output.Length)
                            break;
                        byte v = frame[offset++];
                        offset &= 0xfff;
                        frame[frame_pos++] = v;
                        frame_pos &= 0xfff;
                        output[dst++] = v;
                    }
                }
            }
        }
    }
}
