using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Runtime.CompilerServices;
using System.Xml.Linq;

namespace GameRes.Formats.MAGES
{
    [Export(typeof(ArchiveFormat))]
    public class LoveOnceOpener : ArchiveFormat
    {
        public override string Tag { get { return "BIN/LoveOnce"; } }
        public override string Description { get { return "L@ve Once PS3 resource archive"; } }
        public override uint Signature { get { return 0; } } // no header
        public override bool IsHierarchic { get { return false; } }
        public override bool CanWrite { get { return false; } }

        public override ArcFile TryOpen(ArcView filedata)
        {
            ArcView filelist;
            if (filedata.Name.EndsWith("data.bin"))
            {
                string filepath = Path.GetDirectoryName(filedata.Name);
                filepath = Path.Combine(filepath, "list.bin");
                filelist = new ArcView(filepath);
            }
            else if (filedata.Name.EndsWith("list.bin"))
            {
                string filepath = Path.GetDirectoryName(filedata.Name);
                filepath = Path.Combine(filepath, "data.bin");
                filelist = new ArcView(filepath);
                return TryOpen(filelist);
            }
            else return null;

            uint index_offset = 0, count = 0;
            var dir = new List<Entry>();
            while (index_offset < filelist.MaxOffset)
            {
                uint filesize = filelist.View.ReadUInt32(index_offset);
                uint fileoffset = filelist.View.ReadUInt32(index_offset + 8);
                string name = filelist.View.ReadString(index_offset + 0x10, 0x40);
                var entry = Create<Entry>(name);
                entry.Size = filesize;
                entry.Offset = fileoffset;
                if (!entry.CheckPlacement(filedata.MaxOffset))
                    return null;
                dir.Add(entry);
                count++;
                index_offset += 0x50;
            }
            return new ArcFile(filedata, this, dir);
        }
    }
}
