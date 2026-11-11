using GameRes.Utility;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.Kid
{
    [Export(typeof(ArchiveFormat))]
    public class KlzOpener: ArchiveFormat
    {
        public override string Tag { get { return "KLZ/KID PS2"; } }
        public override string Description { get { return "KID PS2 compressed image format with multi TIM2"; } }
        public override uint Signature { get { return 0; } }
        public override bool IsHierarchic { get { return false; } }
        public override bool CanWrite { get { return false; } }

        public KlzOpener()
        {
            Extensions = new string[] { "klz" };
        }

        public override ArcFile TryOpen(ArcView file)
        {
            if (!file.Name.HasExtension(".klz"))
                return null;
            uint unpacked_size = Binary.BigEndian(file.View.ReadUInt32(0));
            if (unpacked_size <= 0x20 || unpacked_size > 0x5000000)
                return null;

            var backend = file.CreateStream();
            var input = KlzFormat.LzhStreamDecode(backend);
            var base_name = Path.GetFileNameWithoutExtension(file.Name);
            var dir = GetEntries(input, base_name);
            if (dir == null || dir.Count == 0)
            {
                return null;
            }
            else
            {
                return new KlzArchive(file, this, dir, input);
            }
            //throw new NotImplementedException();
        }

        public override Stream OpenEntry(ArcFile arc, Entry entry)
        {
            return new StreamRegion(((KlzArchive)arc).Source, entry.Offset, entry.Size, true);
        }

        internal static List<Entry> GetEntries (Stream input, string base_name)
        {
            var entries = new List<Entry>();
            BinaryReader m_input = new ArcView.Reader(input);
            int count = 0;
            m_input.BaseStream.Position = 0;
            while (m_input.BaseStream.Position < m_input.BaseStream.Length)
            {
                while (true)
                {
                    try
                    {
                        uint sign = m_input.ReadUInt32();
                        m_input.ReadBytes(12);
                        if (sign == 0x324D4954) //TIM2
                        {
                            //m_input.BaseStream.Seek(-4, SeekOrigin.Current);
                            break;
                        }
                    }
                    catch (EndOfStreamException)
                    {
                        return entries;
                    }
                }
                long tell = m_input.BaseStream.Position - 16;
                uint size = m_input.ReadUInt32() + 16;
                string name = base_name + "_" + count.ToString("D2");
                if (tell + size > m_input.BaseStream.Length)
                {
                    size = (uint)(m_input.BaseStream.Length - tell);
                    name += "_incomplete";
                }
                var entry = new Entry {
                    Name = name + ".tm2",
                    Size = size,
                    Offset = tell,
                    Type = "image"
                };
                count++;
                entries.Add(entry);
                m_input.BaseStream.Position = tell + size;
            }
            return entries;
        }
    }

    internal class KlzArchive : ArcFile
    {
        public readonly Stream Source;
        public KlzArchive(ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, Stream input)
            : base (arc, impl, dir)
        {
            Source = input;
        }

        #region IDisposable Members
        bool _spc_disposed = false;
        protected override void Dispose(bool disposing)
        {
            if (_spc_disposed)
                return;
            if (disposing)
            {
                Source.Dispose();
            }
            _spc_disposed = true;
            base.Dispose(disposing);
        }
        #endregion
    }
}
