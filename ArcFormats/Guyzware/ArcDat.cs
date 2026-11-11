using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Utility;

namespace GameRes.Formats.Guyzware
{
    [Export(typeof(ArchiveFormat))]
    public class GdpOpener : ArchiveFormat
    {
        public override string Tag => "DAT/GDP";
        public override string Description => "Guyzware engine";
        public override uint Signature => 0;
        public override bool IsHierarchic => false;
        public override bool CanWrite => false;

        public override ArcFile TryOpen(ArcView file)
        {
            int count = file.View.ReadInt32(0);
            if (!IsSaneCount(count))
                return null;

            var dir = new List<Entry>(count);
            long index_offset = 4;

            // heuristic: small first byte = variable
            byte checkByte = file.View.ReadByte(index_offset);
            bool isVariableLength = checkByte < 64;

            for (int i = 0; i < count; i++)
            {
                if (index_offset >= file.MaxOffset)
                    break;

                string name;
                uint offset, size;

                if (isVariableLength)
                {
                    // Variable length:
                    // [1 byte length (ignored)] [Shift-JIS string] [00]

                    index_offset++; // skip length byte

                    var nameBytes = new List<byte>();

                    while (index_offset < file.MaxOffset)
                    {
                        byte b = file.View.ReadByte(index_offset++);
                        if (b == 0)
                            break;

                        nameBytes.Add(b);
                    }

                    name = Encodings.cp932.GetString(nameBytes.ToArray());
                }
                else
                {
                    // Fixed 32 bytes

                    name = file.View.ReadString(index_offset, 32, Encodings.cp932);
                    name = name.TrimEnd('\0', ' ');
                    index_offset += 32;
                }

                offset = file.View.ReadUInt32(index_offset);
                size   = file.View.ReadUInt32(index_offset + 4);
                index_offset += 8;

                var entry = new Entry
                {
                    Name = name,
                    Offset = offset,
                    Size = size,
                };

                if (!entry.CheckPlacement(file.MaxOffset))
                    return null;

                dir.Add(entry);
            }

            return new ArcFile(file, this, dir);
        }

        public override Stream OpenEntry(ArcFile arc, Entry entry)
        {
            return arc.File.CreateStream(entry.Offset, entry.Size);
        }
    }
}
