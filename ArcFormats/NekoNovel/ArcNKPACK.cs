using System;
using System.IO;
using System.Collections.Generic;
using System.Text;
using GameRes.Compression;
using System.ComponentModel.Composition;
using CommunityToolkit.HighPerformance;

namespace GameRes.Formats.NekoNovel
{
    [Export(typeof(ArchiveFormat))]
    public class NkpackOpener : ArchiveFormat
    {
        public override string Tag => "NKPACK/NekoNovel";
        public override string Description => "NekoNovel resource archive";
        public override uint Signature => 0u;
        public override bool IsHierarchic => true;
        public override bool CanWrite => false;

        public NkpackOpener()
        {
            Extensions = new string[] { "nkpack" };
        }

        private static readonly byte[] s_mHeader = new byte[]
        {
            0x0F, 0x00, 0x00, 0x00, 0x4E, 0x4B, 0x4E, 0x4F, 0x45, 0x56, 0x4C, 0x20, 0x50, 0x41, 0x43, 0x4B,
            0x41, 0x47, 0x45
        };

        public override ArcFile TryOpen(ArcView file)
        {
            if (!file.View.BytesEqual(0L, s_mHeader))
            {
                return null;
            }
            using (ArcViewStream stream = file.CreateStream())
            {
                stream.Seek(-4L, SeekOrigin.End);
                uint entryOffset = ~stream.ReadUInt32();

                stream.Seek(entryOffset, SeekOrigin.Begin);
                string signature = ReadString(stream);

                int count = stream.ReadInt32();
                List<Entry> entries = new List<Entry>(count);
                for (int i = 0; i < count; ++i)
                {
                    string name = ReadString(stream);

                    PackedEntry entry = Create<PackedEntry>(name);
                    entry.Offset = ~stream.ReadUInt32();
                    entry.UnpackedSize = stream.ReadUInt32();
                    entry.Size = ~stream.ReadUInt32();
                    entry.IsPacked = true;

                    if (!entry.CheckPlacement(file.MaxOffset))
                    { 
                        return null;
                    }

                    entries.Add(entry);
                }

                return new ArcFile(file, this, entries);
            }
        }

        public override Stream OpenEntry(ArcFile arc, Entry entry)
        {
            if (!(entry is PackedEntry e))
            {
                return arc.File.CreateStream(entry.Offset, entry.Size);
            }
            return new ZLibStream(arc.File.CreateStream(e.Offset, e.Size), CompressionMode.Decompress);
        }

        private unsafe static string ReadString(Stream stream)
        {
            uint length = 0u;
            stream.Read(new Span<byte>(&length, 4));

            if (length == 0u)
            {
                return string.Empty;
            }

            byte[] data = new byte[length];
            stream.Read(data, 0, data.Length);

            return Encoding.UTF8.GetString(data);
        }
    }
}
