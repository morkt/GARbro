using System.Collections.Generic;
using System.ComponentModel.Composition;

namespace GameRes.Formats.FC01
{
    [Export(typeof(ArchiveFormat))]
    public class SCXAOpener : ArchiveFormat
    {
        public override string Tag { get { return "ARC/SCXA Pia Carrot e Youkoso 4 Xbox360"; } }
        public override string Description { get { return "Pia Carrot e Youkoso 4 Xbox360 resource archive"; } }
        public override uint Signature { get { return 0x41584353; } } // 'SCXA'
        public override bool IsHierarchic { get { return false; } }
        public override bool CanWrite { get { return false; } }

        public override ArcFile TryOpen(ArcView file)
        {
            uint datastart = file.View.ReadUInt32(4);
            int count = file.View.ReadInt32(8);
            if (!IsSaneCount(count))
                return null;
            uint tnstart = (uint)count * 4 + 12;
            var dir = new List<Entry>(count);
            uint index_offset;
            for (int i = 0; i < count; ++i)
            {
                uint namestart = file.View.ReadUInt32(i * 4 + 12);
                uint fstart = file.View.ReadUInt32(tnstart + namestart);
                uint flength = file.View.ReadUInt32(tnstart + namestart + 4);
                index_offset = tnstart + namestart + 8;
                byte c;
                List<byte> namebyte = new List<byte>();
                while (true)
                {
                    c = file.View.ReadByte((long)index_offset);
                    if (c == 0 | index_offset > datastart) break;
                    namebyte.Add(c);
                    index_offset++;
                }
                var name = System.Text.Encoding.ASCII.GetString(namebyte.ToArray());
                var entry = Create<Entry>(name);
                entry.Offset = datastart + fstart;
                entry.Size = flength;
                if (!entry.CheckPlacement(file.MaxOffset))
                    return null;
                dir.Add(entry);
            }
            return new ArcFile(file, this, dir);
        }
    }
}
