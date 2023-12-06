using GameRes.Utility;
using System.Collections.Generic;
using System.ComponentModel.Composition;

namespace GameRes.Formats.NipponIchi
{
    [Export(typeof(ArchiveFormat))]
    public class CASNOpener : ArchiveFormat
    {
        public override string Tag { get { return "DAT/CASN ShinHayarigami"; } }
        public override string Description { get { return "Nippon Ichi Shin Hayarigami PS3 resource archive"; } }
        public override uint Signature { get { return 0x4E534143; } } // 'CASN'
        public override bool IsHierarchic { get { return false; } }
        public override bool CanWrite { get { return false; } }

        public override ArcFile TryOpen(ArcView file)
        {
            int count = Binary.BigEndian(file.View.ReadUInt16(6));
            long index_offset = count * 10 + 30; //actually count*10 + 28, ignored two bytes of 000C or 000B
            var dir = new List<Entry>(count);
            for (int i = 0; i < count; ++i)
            {

                uint fstart = Binary.BigEndian(file.View.ReadUInt32(index_offset));
                uint flength = Binary.BigEndian(file.View.ReadUInt32(index_offset + 4));
                index_offset += 8;
                byte c;
                List<byte> namebyte = new List<byte>();
                while (true)
                {
                    c = file.View.ReadByte(index_offset);
                    if (c == 0) break;
                    namebyte.Add(c);
                    index_offset++;
                }
                index_offset += 3;
                //var sjis = System.Text.Encoding.GetEncoding("Shift-JIS");
                var name = System.Text.Encoding.ASCII.GetString(namebyte.ToArray());
                var entry = Create<Entry>(name);
                entry.Offset = fstart;
                entry.Size = flength;
                if (!entry.CheckPlacement(file.MaxOffset))
                    return null;
                dir.Add(entry);
            }
            return new ArcFile(file, this, dir);
        }
    }
}
