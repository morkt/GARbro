using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Text;

namespace GameRes.Formats.MAGES
{
    [Export(typeof(ArchiveFormat))]
    public class ARC20Opener : ArchiveFormat
    {
        public override string Tag { get { return "ARC/Princess Soft ARC20"; } }
        public override string Description { get { return "Princess Soft PS2 resource archive"; } }
        public override uint Signature { get { return 0x20435241; } } // 'ARC\x20'
        public override bool IsHierarchic { get { return false; } }
        public override bool CanWrite { get { return false; } }

        public override ArcFile TryOpen(ArcView file)
        {
            int count = file.View.ReadInt32(4);
            if (!IsSaneCount(count))
                return null;
            uint filename_end = file.View.ReadUInt32(8);
            var dir = new List<Entry>(count);
            uint index_offset;
            for (int i = 0; i < count; ++i)
            {
                index_offset = file.View.ReadUInt32(16 * i + 16);
                byte c;
                List<byte> namebyte = new List<byte>();
                while (true) 
                {
                    c = file.View.ReadByte((long)index_offset);
                    if (c == 0 | index_offset > filename_end) break;
                    namebyte.Add(c);
                    index_offset++;
                }
                var sjis = System.Text.Encoding.GetEncoding("Shift-JIS");
                var name = sjis.GetString(namebyte.ToArray());
                var entry = Create<PackedEntry>(name);
                
                entry.Offset = file.View.ReadUInt32(16 * i + 16 + 4) * 2048;

                entry.Size = file.View.ReadUInt32(16 * i + 16 + 8);
                entry.UnpackedSize = file.View.ReadUInt32(16 * i + 16 + 8);
                if (!entry.CheckPlacement(file.MaxOffset))
                    return null;
                dir.Add(entry);
            }
            return new ArcFile(file, this, dir);
        }
    }
}
