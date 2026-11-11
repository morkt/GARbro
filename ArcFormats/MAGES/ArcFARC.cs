using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Text;

namespace GameRes.Formats.MAGES
{
    [Export(typeof(ArchiveFormat))]
    public class FARCOpener : ArchiveFormat
    {
        public override string Tag { get { return "DAT/FARC Rozen Maiden PS3 archive"; } }
        public override string Description { get { return "MAGES Rozen Maiden Wechseln Sie Welt ab PS3 BLJM61120 archive"; } }
        public override uint Signature { get { return 0x43524146; } } // 'FARC'
        public override bool IsHierarchic { get { return false; } }
        public override bool CanWrite { get { return false; } }

        public override ArcFile TryOpen(ArcView file)
        {
            int count = file.View.ReadInt32(20);
            if (!IsSaneCount(count))
                return null;
            uint datanamestart = (uint)count * 20 + 36;
            var dir = new List<Entry>(count);
            for (int i = 0; i < count; ++i)
            {
                long namepl = file.View.ReadUInt32(20 * i + 36 + 16);
                long index_offset = datanamestart + namepl;
                byte c;
                List<byte> namebyte = new List<byte>();
                while (true)
                {
                    c = file.View.ReadByte(index_offset);
                    if (c == 0) break;
                    namebyte.Add(c);
                    index_offset++;
                }
                var sjis = Encoding.GetEncoding("Shift-JIS");
                var name = sjis.GetString(namebyte.ToArray());
                var entry = Create<Entry>(name);
                entry.Offset = file.View.ReadUInt32(20 * i + 36);
                entry.Size = file.View.ReadUInt32(20 * i + 36 + 4);
                if (entry.Size == 0) 
                    continue;
                dir.Add(entry);
            }
            return new ArcFile(file, this, dir);
        }
    }
}
