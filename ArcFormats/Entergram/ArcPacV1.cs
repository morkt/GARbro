using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Text;

namespace GameRes.Formats.Entergram
{
    [Export(typeof(ArchiveFormat))]
    public class PacOpenerV1 : ArchiveFormat
    {
        public override string Tag => "PacV1/Entergram";
        public override string Description => "Entergram Unity resource archive";
        public override uint Signature => 0x20434150;  // PAC/x20
        public override bool IsHierarchic => true;
        public override bool CanWrite => false;

        private static readonly byte[] smHeader = new byte[]
        {
            0x50, 0x41, 0x43, 0x20, 0x56, 0x45, 0x52, 0x2D, 0x31, 0x2E, 0x30, 0x30, 0x00, 0x00, 0x00, 0x00
        };

        public override ArcFile TryOpen(ArcView file)
        {
            if (!file.View.BytesEqual(0L, smHeader))
            {
                return null;
            }

            List<Entry> entries = this.ParseEntry(file);
            if(entries == null)
            {
                return null;
            }

            return new ArcFile(file, this, entries);
        }

        public override Stream OpenEntry(ArcFile arc, Entry entry)
        {
            if(!(entry is PackedEntry e))
            {
                return base.OpenEntry(arc, entry);
            }

            if (e.IsPacked)
            {
                using(ArcViewStream s = arc.File.CreateStream(e.Offset, e.Size))
                {
                    byte[] data = s.ReadBytes(int.MaxValue);
                    data = QLZCompressor.Decompress(data);
                    return new MemoryStream(data, false);
                }
            }
            else
            {
                return base.OpenEntry(arc, entry);
            }
        }

        private List<Entry> ParseEntry(ArcView file)
        {
            bool compressed = Path.GetFileNameWithoutExtension(file.Name).EndsWith("_c");
            using(ArcViewStream stream = file.CreateStream(smHeader.LongLength))
            {
                List<PackedEntry> entries = new List<PackedEntry>();

                // 8 Bytes Entry Mode (Default)
                {
                    stream.Position = 0L;
                    while (stream.Position < stream.Length)
                    {
                        string name = ReadString(stream);

                        long offset = stream.ReadInt64() + 0x10L;
                        long length = stream.ReadInt64();
                        if (!CheckEntry(offset, length, file.MaxOffset))
                        {
                            entries.Clear();
                            break;
                        }

                        PackedEntry entry = Create<PackedEntry>(name);
                        entry.Offset = offset;
                        entry.Size = (uint)length;
                        entry.IsPacked = compressed;
                        entries.Add(entry);

                        stream.Seek(length, SeekOrigin.Current);
                    }
                }

                // 10 Bytes Entry Mode
                if (!entries.Any())
                {
                    stream.Position = 0L;
                    while (stream.Position < stream.Length)
                    {
                        string name = ReadString(stream);

                        long offset = stream.ReadInt64() + 0x14L;
                        stream.Position += 2L;
                        long length = stream.ReadInt64();
                        stream.Position += 2L;
                        if (!CheckEntry(offset, length, file.MaxOffset))
                        {
                            entries.Clear();
                            break;
                        }

                        PackedEntry entry = Create<PackedEntry>(name);
                        entry.Offset = offset;
                        entry.Size = (uint)length;
                        entry.IsPacked = compressed;
                        entries.Add(entry);

                        stream.Seek(length, SeekOrigin.Current);
                    }
                }

                return entries.Cast<Entry>().ToList();
            }
        }

        private static string ReadString(ArcViewStream stream)
        {
            byte[] buf = new byte[0x20];
            if (stream.Read(buf, 0, buf.Length) != buf.Length)
            {
                return string.Empty;
            }

            int len = Array.IndexOf<byte>(buf, 0);
            if (len <= 0)
            {
                return string.Empty;
            }

            return Encoding.UTF8.GetString(buf, 0, len);
        }

        private static bool CheckEntry(long offset, long length, long max)
        {
            return offset >= 0L &&
                   length >= 0L &&
                   offset < max &&
                   length <= max &&
                   length <= uint.MaxValue &&
                   offset <= max - length;
        }
    }
}
