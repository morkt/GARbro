//! \file       ArcVF.cs
//! \date       Wed Jun 08 00:27:36 2016
//! \brief      LiveMaker resource archive.
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
using System.Diagnostics;
using System.IO;
using GameRes.Compression;

namespace GameRes.Formats.LiveMaker
{
    [Export(typeof(ArchiveFormat))]
    public class VffOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DAT/vf"; } }
        public override string Description { get { return "LiveMaker resource archive"; } }
        public override uint     Signature { get { return 0x666676; } } // 'vff'
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public VffOpener ()
        {
            Extensions = new string[] { "dat" };
            Signatures = new uint[] { 0x666676, 0 };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            uint base_offset = 0;
            ArcView index_file = file;
            ArcView extra_file = null;
            try
            {
                // possible filesystem structure:
                //   game.dat  -- main archive body
                //   game.001  -- [optional] extra part
                //   game.ext  -- [optional] separate index (could be included into the main body)

                uint signature = index_file.View.ReadUInt32 (0);
                if (file.Name.EndsWith (".exe", StringComparison.InvariantCultureIgnoreCase)
                    && (0x5A4D == (signature & 0xFFFF))) // 'MZ'
                {
                    base_offset = SkipExeData (index_file);
                    signature = index_file.View.ReadUInt32 (base_offset);
                }
                else if (!file.Name.EndsWith (".dat", StringComparison.InvariantCultureIgnoreCase))
                {
                    return null;
                }
                else if (0x666676 != signature)
                {
                    var ext_filename = Path.ChangeExtension (file.Name, ".ext");
                    if (!VFS.FileExists (ext_filename))
                        return null;
                    index_file = VFS.OpenView (ext_filename);
                    signature = index_file.View.ReadUInt32 (0);
                }
                if (0x666676 != signature)
                    return null;
                int count = index_file.View.ReadInt32 (base_offset+6);
                if (!IsSaneCount (count))
                    return null;

                var dir = ReadIndex (index_file, base_offset, count);
                if (null == dir)
                    return null;
                long max_offset = file.MaxOffset;
                for (int i = 0; i < dir.Count; ++i)
                {
                    if (!dir[i].CheckPlacement (max_offset))
                    {
                        if (extra_file != null)
                        {
                            // remove entries that don't fit into game.dat+game.001
                            int discard = dir.Count - i;
                            Trace.WriteLine (string.Format ("{0} entries didn't fit and were discarded", discard), "[vff]");
                            dir.RemoveRange (i, discard);
                            break;
                        }
                        var extra_filename = Path.ChangeExtension (file.Name, ".001");
                        if (!VFS.FileExists (extra_filename))
                            return null;
                        extra_file = VFS.OpenView (extra_filename);
                        max_offset += extra_file.MaxOffset;
                        if (!dir[i].CheckPlacement (max_offset))
                            return null;
                    }
                }
                if (null == extra_file)
                    return new ArcFile (file, this, dir);
                return new VffArchive (file, this, dir, extra_file);
            }
            catch
            {
                if (extra_file != null)
                    extra_file.Dispose();
                throw;
            }
            finally
            {
                if (index_file != file)
                    index_file.Dispose();
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var vff = arc as VffArchive;
            Stream input = null;
            if (vff != null)
                input = vff.OpenStream (entry);
            else
                input = arc.File.CreateStream (entry.Offset, entry.Size);

            var pent = entry as PackedEntry;
            if (pent != null && pent.IsPacked)
                return new ZLibStream (input, CompressionMode.Decompress);
            else
                return input;
        }

        List<Entry> ReadIndex (ArcView file, uint base_offset, int count)
        {
            uint index_offset = base_offset+0xA;
            var name_buffer = new byte[0x100];
            var rnd = new TpRandom (0x75D6EE39u);
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                uint name_length = file.View.ReadUInt32 (index_offset);
                index_offset += 4;
                if (0 == name_length || name_length > name_buffer.Length)
                    return null;
                if (name_length != file.View.Read (index_offset, name_buffer, 0, name_length))
                    return null;
                index_offset += name_length;

                var name = DecryptName (name_buffer, (int)name_length, rnd);
                dir.Add (FormatCatalog.Instance.Create<PackedEntry> (name));
            }
            rnd.Reset();
            long offset = base_offset + (file.View.ReadInt64 (index_offset) ^ (int)rnd.GetRand32());
            foreach (var entry in dir)
            {
                index_offset += 8;
                long next_offset = base_offset + (file.View.ReadInt64 (index_offset) ^ (int)rnd.GetRand32());
                entry.Offset = offset;
                entry.Size = (uint)(next_offset - offset);
                offset = next_offset;
            }
            index_offset += 8;
            foreach (PackedEntry entry in dir)
            {
                entry.IsPacked = 0 == file.View.ReadByte (index_offset++);
            }
            return dir;
        }

        string DecryptName (byte[] name_buf, int name_length, TpRandom key)
        {
            for (int i = 0; i < name_length; ++i)
            {
                name_buf[i] ^= (byte)key.GetRand32();
            }
            return Encodings.cp932.GetString (name_buf, 0, name_length);
        }

        uint SkipExeData (ArcView file)
        {
            var exe = new ExeFile (file);
            return (uint)exe.Overlay.Offset;
        }
    }

    internal class TpRandom
    {
        uint    m_seed;
        uint    m_current;

        public TpRandom (uint seed)
        {
            m_seed = seed;
            m_current = 0;
        }

        public uint GetRand32 ()
        {
            m_current += m_current << 2;
            m_current += m_seed;
            return m_current;
        }

        public void Reset ()
        {
            m_current = 0;
        }
    }

    internal class VffArchive : ArcFile
    {
        readonly ArcView ExtraFile;

        public VffArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, ArcView extra_file)
            : base (arc, impl, dir)
        {
            ExtraFile = extra_file;
        }

        internal Stream OpenStream (Entry entry)
        {
            if (entry.Offset < File.MaxOffset && entry.Offset+entry.Size > File.MaxOffset)
            {
                var first_part = File.View.ReadBytes (entry.Offset, entry.Size);
                var second_part = ExtraFile.CreateStream (0, entry.Size - (uint)first_part.Length);
                return new PrefixStream (first_part, second_part);
            }
            else if (entry.Offset >= File.MaxOffset)
            {
                return ExtraFile.CreateStream (entry.Offset, entry.Size);
            }
            else
                return File.CreateStream (entry.Offset, entry.Size);
        }

        bool _vff_disposed = false;
        protected override void Dispose (bool disposing)
        {
            if (!_vff_disposed)
            {
                if (disposing && ExtraFile != null)
                    ExtraFile.Dispose();
                _vff_disposed = true;
            }
            base.Dispose (disposing);
        }
    }
}
