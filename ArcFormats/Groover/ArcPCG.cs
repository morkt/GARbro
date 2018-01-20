//! \file       ArcPCG.cs
//! \date       2018 Jan 20
//! \brief      Groover CG archive.
//
// Copyright (C) 2018 by morkt
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
using System.Text.RegularExpressions;
using System.Windows.Media;
using GameRes.Utility;

namespace GameRes.Formats.Groover
{
    [Export(typeof(ArchiveFormat))]
    public class DatOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DAT/PCG"; } }
        public override string Description { get { return "Groover resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        static readonly Regex IndexNameRe = new Regex (@"^((.+)0\d)\.dat$", RegexOptions.IgnoreCase);

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.HasExtension (".dat"))
                return null;
            var arc_name = Path.GetFileName (file.Name);
            var match = IndexNameRe.Match (arc_name);
            if (!match.Success)
                return null;
            var base_name = match.Groups[2].Value;
            base_name = VFS.ChangeFileName (file.Name, base_name);
            var index_name = base_name + ".pcg";
            if (!VFS.FileExists (index_name))
            {
                index_name = base_name + ".spf";
                if (!VFS.FileExists (index_name))
                    return null;
            }
            arc_name = match.Groups[1].Value;
            using (var index = VFS.OpenView (index_name))
            {
                int parts_count = index.View.ReadInt32 (0);
                int count = index.View.ReadInt32 (4);
                if (parts_count > 10 || !IsSaneCount (count))
                    return null;
                int entry_size = (int)(index.MaxOffset - 0x198) / count;
                if (entry_size < 0x30)
                    return null;
                int first_index = -1, last_index = -1;
                for (int i = 0; i < parts_count; ++i)
                {
                    int name_pos = 8 + i * 0x20;
                    var name = index.View.ReadString (name_pos, 0x20);
                    if (name == arc_name)
                    {
                        int first_index_pos = 0x148 + i * 4;
                        int last_index_pos = 0x170 + i * 4;
                        first_index = index.View.ReadInt32 (first_index_pos);
                        last_index = index.View.ReadInt32 (last_index_pos);
                        break;
                    }
                }
                if (first_index < 0 || first_index >= last_index || last_index > count)
                    return null;

                uint name_size = entry_size >= 0x48 ? 0x40u : 0x20u;
                int index_offset = 0x198 + entry_size * first_index;
                var dir = new List<Entry> (last_index-first_index);
                for (int i = first_index; i < last_index; ++i)
                {
                    var name = index.View.ReadString (index_offset, name_size);
                    var entry = FormatCatalog.Instance.Create<Entry> (name);
                    entry.Size = index.View.ReadUInt32 (index_offset+name_size);
                    entry.Offset = index.View.ReadUInt32 (index_offset+name_size+4);
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);
                    index_offset += entry_size;
                }
                return new ArcFile (file, this, dir);
            }
        }

        public override IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            try
            {
                if (0x504D434E == input.Signature) // 'NCMP'
                    return new NcmpReader (input);
                if (0x424352 == input.Signature) // 'RCB'
                    return new RcbReader (input);
                return new ImageFormatDecoder (input);
            }
            catch
            {
                input.Dispose();
                throw;
            }
        }
    }

    internal abstract class PcgReaderBase : IImageDecoder
    {
        protected IBinaryStream     m_input;
        private   ImageData         m_image;
        protected int               m_unpacked_size;
        protected int               m_packed_size;

        public Stream            Source { get { m_input.Position = 0; return m_input.AsStream; } }
        public ImageFormat SourceFormat { get { return null; } }
        public ImageMetaData       Info { get; protected set; }
        public PixelFormat       Format { get { return PixelFormats.Bgr24; } }
        public ImageData Image {
            get {
                if (null == m_image)
                {
                    var pixels = Unpack();
                    m_image = ImageData.Create (Info, Format, null, pixels);
                }
                return m_image;
            }
        }

        protected PcgReaderBase (IBinaryStream input)
        {
            m_input = input;
            m_input.Position = 8;
            uint w = m_input.ReadUInt32();
            uint h = m_input.ReadUInt32();
            m_unpacked_size = m_input.ReadInt32();
            m_packed_size = m_input.ReadInt32();
            Info = new ImageMetaData { Width = w, Height = h, BPP = 24 };
        }

        protected abstract byte[] Unpack ();

        #region IDisposable Members
        public void Dispose ()
        {
            Dispose (true);
            GC.SuppressFinalize (this);
        }

        bool m_disposed = false;
        protected virtual void Dispose (bool disposing)
        {
            if (!m_disposed)
            {
                m_input.Dispose();
                m_disposed = true;
            }
        }
        #endregion
    }

    internal class NcmpReader : PcgReaderBase
    {
        public NcmpReader (IBinaryStream input) : base (input) { }

        protected override byte[] Unpack ()
        {
            m_input.Position = 0x18;
            return m_input.ReadBytes (m_unpacked_size);
        }
    }

    internal class RcbReader : PcgReaderBase
    {
        public RcbReader (IBinaryStream input) : base (input) { }

        protected override byte[] Unpack ()
        {
            m_input.Position = 0x18;
            var output = new byte[m_unpacked_size];
            int src = 0;
            int dst = 0;
            while (src < m_packed_size && dst < m_unpacked_size)
            {
                m_input.Read (output, dst, 3);
                int count = m_input.ReadUInt8();
                if (count > 0)
                {
                    if (count > 1)
                        Binary.CopyOverlapped (output, dst, dst+3, (count-1) * 3);
                    dst += count * 3;
                }
            }
            return output;
        }
    }
}
