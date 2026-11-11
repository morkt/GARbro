using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Text;

namespace GameRes.Formats.CatSystem
{
    internal class PidaEntryV1 : Entry
    {
        public ushort Width;
        public ushort Height;
        public short OffsetX;
        public short OffsetY;
    }

    [Export(typeof(ArchiveFormat))]
    public class PidaOpenerV1 : ArchiveFormat
    {
        public override string Tag => "PidaV1";
        public override string Description => "CatSystem2 engine multi-image";
        public override uint Signature => 0x6DF22373u;
        public override bool IsHierarchic => true;
        public override bool CanWrite => false;

        public PidaOpenerV1()
        {
            ContainedFormats = new[] { "PNG" };
        }

        public override ArcFile TryOpen(ArcView file)
        {
            using (ArcViewStream stream = file.CreateStream())
            {
                using (BinaryReader br = new BinaryReader(stream, Encoding.Unicode, true))
                {
                    stream.Position = 8L;

                    List<PidaEntryV1> entries = new List<PidaEntryV1>();
                    {
                        string fn = br.ReadString();
                        while (!string.IsNullOrEmpty(fn))
                        {
                            PidaEntryV1 e = Create<PidaEntryV1>(fn);
                            e.Offset = br.ReadUInt32();
                            e.Size = 0u;

                            e.Width = br.ReadUInt16();
                            e.Height = br.ReadUInt16();
                            e.OffsetX = br.ReadInt16();
                            e.OffsetY = br.ReadInt16();

                            e.Type = "image";
                            entries.Add(e);

                            fn = br.ReadString();
                        }
                    }

                    if (entries.Any())
                    {
                        long imageDataOffset = stream.Position;

                        foreach (PidaEntryV1 e in entries)
                        {
                            e.Offset += imageDataOffset;
                        }
                        {
                            PidaEntryV1 last = entries.Last();
                            last.Size = (uint)(stream.Length - last.Offset);
                        }
                        for (int i = 0; i < entries.Count - 1; ++i)
                        {
                            PidaEntryV1 curr = entries[i + 0];
                            PidaEntryV1 next = entries[i + 1];
                            curr.Size = (uint)(next.Offset - curr.Offset);
                        }
                    }

                    return new ArcFile(file, this, entries.Cast<Entry>().ToList());
                } 
            }
        }
    }
}
