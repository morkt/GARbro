using GameRes.Utility;
using System.Collections.Generic;
using System.ComponentModel.Composition;

namespace GameRes.Formats.NipponIchi
{
    [Export(typeof(ArchiveFormat))]
    public class PSFSOpener : ArchiveFormat
    {
        public override string Tag { get { return "DAT/PS_FS ShinHayarigami2"; } }
        public override string Description { get { return "Nippon Ichi Shin Hayarigami2 PS3 resource archive"; } }
        public override uint Signature { get { return 0x465F5350; } } // 'PS_F' of 'PS_FS_V1'
        public override bool IsHierarchic { get { return false; } }
        public override bool CanWrite { get { return false; } }

        public override ArcFile TryOpen(ArcView file)
        {
            int count = Binary.BigEndian(file.View.ReadInt32(8));
            if (!IsSaneCount(count))
                return null;
            long index_offset = 0x10;
            var dir = new List<Entry>(count);
            for (int i = 0; i < count; ++i)
            {
                string name = file.View.ReadString(index_offset, 0x30);
                var entry = Create<Entry>(name);
                entry.Offset = Binary.BigEndian(file.View.ReadUInt32(index_offset + 0x3C));
                entry.Size = Binary.BigEndian(file.View.ReadUInt32(index_offset + 0x34));
                index_offset += 0x40;
                if (!entry.CheckPlacement(file.MaxOffset))
                    return null;
                if (0 == file.View.ReadByte(entry.Offset))
                {
                    entry.Offset += 0x10;
                    entry.Size -= 0x10;
                }
                dir.Add(entry);
            }
            return new ArcFile(file, this, dir);
        }
    }
}
