//! \file       ArcSPC.cs
//! \date       Wed Mar 09 09:01:11 2016
//! \brief      CRI container for multiple textures.
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
using GameRes.Compression;

namespace GameRes.Formats.Cri
{
    [Export(typeof(ArchiveFormat))]
    public class SpcOpener : ArchiveFormat
    {
        public override string         Tag { get { return "SPC/CRI"; } }
        public override string Description { get { return "CRI MiddleWare texture container"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public SpcOpener ()
        {
            Extensions = new string[] { "spc", "bip" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.HasExtension (".spc") && !file.Name.HasExtension(".bip"))
                return null;
            uint unpacked_size = file.View.ReadUInt32 (0);
            if (unpacked_size <= 0x20 || unpacked_size > 0x5000000)
                return null;

            var backend = file.CreateStream();
            backend.Position = 4;
            var lzss = new LzssStream (backend);
            var input = new SeekableStream (lzss);
            var base_name = Path.GetFileNameWithoutExtension(file.Name);
            try
            {
                using (var spc = new XtxIndexBuilder (input, base_name))
                {
                    spc.ReadIndex (0);
                    if (spc.Dir.Count > 0)
                        return new SpcArchive (file, this, spc.Dir, input);
                    else
                        throw new InvalidFormatException();
                }
            }
            catch
            {
                //input.Dispose();
                //throw;
                var dir = new List<Entry>();
                var entry = Create<PackedEntry>(base_name);
                entry.Offset = 0;
                entry.Size = (uint)input.Length;
                entry.Type = "image";
                dir.Add(entry);
                return new SpcArchive(file, this, dir, input);
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            return new StreamRegion (((SpcArchive)arc).Source, entry.Offset, entry.Size, true);
        }
    }

    internal sealed class XtxIndexBuilder : IDisposable
    {
        BinaryReader            m_input;
        string                  m_base_name;
        List<Entry>             m_dir = new List<Entry>();
        int                     m_subdir_count = 0;

        public List<Entry>      Dir { get { return m_dir; } }

        public XtxIndexBuilder (Stream input, string base_name)
        {
            m_input = new ArcView.Reader (input);
            m_base_name = base_name;
        }

        public void ReadIndex (uint base_offset, string dir_name = "")
        {
            m_input.BaseStream.Position = base_offset;
            uint first_offset = m_input.ReadUInt32();
            if (0 != (first_offset & 0xF))
                throw new InvalidFormatException();
            int count = (int)(first_offset / 0x10u);
            m_input.BaseStream.Position = base_offset;
            var subdir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                uint offset = m_input.ReadUInt32();
                uint size   = m_input.ReadUInt32();
                if (offset < first_offset || size < 0x20)
                    throw new InvalidFormatException();
                m_input.BaseStream.Seek (8, SeekOrigin.Current);
                var entry = new Entry { Offset = base_offset + offset, Size = size };
                subdir.Add (entry);
            }
            foreach (var entry in subdir)
            {
                m_input.BaseStream.Position = entry.Offset;
                uint signature = m_input.ReadUInt32();
                if (0x787478 == signature) // 'xtx'
                {
                    var file_name = string.Format ("{0}#{1:D4}.xtx", m_base_name, m_dir.Count);
                    entry.Name = Path.Combine (dir_name, file_name);
                    entry.Type = "image";
                    m_dir.Add (entry);
                }
                else
                {
                    var subdir_name = m_subdir_count++.ToString ("D4");
                    ReadIndex ((uint)entry.Offset, Path.Combine (dir_name, subdir_name));
                }
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

    internal class SpcArchive : ArcFile
    {
        public readonly Stream Source;

        public SpcArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, Stream input)
            : base (arc, impl, dir)
        {
            Source = input;
        }

        #region IDisposable Members
        bool _spc_disposed = false;
        protected override void Dispose (bool disposing)
        {
            if (_spc_disposed)
                return;
            if (disposing)
            {
                Source.Dispose();
            }
            _spc_disposed = true;
            base.Dispose (disposing);
        }
        #endregion
    }
}
