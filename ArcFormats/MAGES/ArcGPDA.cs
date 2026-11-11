using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Text;

namespace GameRes.Formats.MAGES
{
    [Export(typeof(ArchiveFormat))]
    public class GpdaOpener : ArchiveFormat
    {
        public override string Tag { get { return "DAT/GPDA"; } }
        public override string Description { get { return "PCSG00543 resource archive"; } }
        public override uint Signature { get { return 0x41445047; } } // 'GPDA'
        public override bool IsHierarchic { get { return false; } }
        public override bool CanWrite { get { return false; } }

        public override ArcFile TryOpen(ArcView file)
        {
            int count = file.View.ReadInt32(0x0C);
            if (!IsSaneCount(count))
                return null;
            var dir = new List<Entry>(count);
            for (int i = 0; i < count; ++i)
            {
                uint filename_offset = file.View.ReadUInt32(16 * i + 16 + 12);
                uint filename_length = file.View.ReadUInt32(filename_offset) - 1;
                string name = file.View.ReadString(filename_offset + 4, filename_length);
                /*byte c;
                List<byte> namebyte = new List<byte>();
                while (true)
                {
                    c = file.View.ReadByte((long)index_offset);
                    if (c == 0 | index_offset > filename_end) break;
                    namebyte.Add(c);
                    index_offset++;
                }*/
                //var sjis = System.Text.Encoding.GetEncoding("Shift-JIS");
                //var name = Encoding.ASCII.GetString(namebyte.ToArray());
                var entry = Create<Entry>(name);

                entry.Offset = file.View.ReadUInt32(16 * i + 16);
                entry.Size = file.View.ReadUInt32(16 * i + 16 + 8);
                if (!entry.CheckPlacement(file.MaxOffset))
                    return null;
                dir.Add(entry);
            }
            return new ArcFile(file, this, dir);
        }
    }
}
