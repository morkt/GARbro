using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Text;

namespace GameRes.Formats.Musica
{
    [Export(typeof(ArchiveFormat))]
    public class PakOpener : ArchiveFormat
    {
        public override string Tag { get; } = "PAK";
        public override string Description { get; } = "Musica engine legacy resource archive";
        public override uint Signature { get; } = 0;
        public override bool IsHierarchic { get; } = true;
        public override bool CanWrite { get; } = false;

        public PakOpener()
        {
            Extensions = new[] { "pak" };
            ContainedFormats = new string[] { "PNG", "OGG" };
        }

        static readonly HashSet<string> PakImageNames = new HashSet<string>()
        {
            "bg", "st",
        };

        static readonly HashSet<string> PakAudioNames = new HashSet<string>()
        {
            "bgm", "se", "voice",
        };

        private string GetType(string pakName, string entryName)
        {
            if (PakImageNames.Contains(pakName))
            {
                return "image";
            }

            if (PakAudioNames.Contains(pakName))
            {
                return "audio";
            }

            return FormatCatalog.Instance.GetTypeFromName(entryName, ContainedFormats);
        }

        public override ArcFile TryOpen(ArcView view)
        {
            Stream input = view.CreateStream();
            using(input = new NegStream(input))
            {
                using(ArcView.Reader reader = new ArcView.Reader(input))
                {
                    int count = reader.ReadInt32();
                    if (count <= 0)
                    {
                        return null;
                    }

                    List<Entry> entries = new List<Entry>(count);
                    for(int i = 0; i < count; ++i)
                    {
                        uint indexLen = reader.ReadUInt32();
                        long indexStart = input.Position;

                        string name = input.ReadCString();
                        if (string.IsNullOrWhiteSpace(name))
                        {
                            return null;
                        }
                        uint size = reader.ReadUInt32();
                        uint offset = reader.ReadUInt32();

                        Entry entry = new Entry() { Name = name, Offset = offset, Size = size };
                        if (!entry.CheckPlacement(view.MaxOffset))
                        {
                            return null;
                        }
                        entry.Type = this.GetType(Path.GetFileNameWithoutExtension(view.Name), entry.Name);
                        entries.Add(entry);

                        input.Position = indexStart + indexLen;
                    }
                    return new PakArchive(view, this, entries, input.Position);
                }
            }
        }

        public override Stream OpenEntry(ArcFile arc, Entry entry)
        {
            if (!(arc is PakArchive pakArc))
            {
                return base.OpenEntry(arc, entry);
            }

            return new NegStream(base.OpenEntry(arc, pakArc.GetEntry(entry)));
        }
    }

    internal class PakArchive : ArcFile
    {
        private long m_IndexSize;
        public PakArchive(ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, long indexSize) : base(arc, impl, dir)
        {
            m_IndexSize = indexSize;
        }

        public Entry GetEntry(Entry e)
        {
            return new Entry
            {
                Name = e.Name,
                Offset = e.Offset + m_IndexSize,
                Size = e.Size,
                Type = e.Type,
            };
        }
    }

    //neg reg8
    public class NegStream : ProxyStream
    {
        public NegStream(Stream stream, bool leave_open = false) : base(stream, leave_open)
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int read = BaseStream.Read(buffer, offset, count);
            for (int i = 0; i < read; ++i)
            {
                buffer[offset + i] = (byte)-buffer[offset + i];
            }
            return read;
        }

        public override int ReadByte()
        {
            int b = BaseStream.ReadByte();
            if (-1 != b)
            {
                b = (byte)-b;
            }
            return b;
        }

        byte[] write_buf;

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (null == write_buf)
                write_buf = new byte[81920];
            while (count > 0)
            {
                int chunk = Math.Min(write_buf.Length, count);
                for (int i = 0; i < chunk; ++i)
                {
                    write_buf[i] = (byte)-buffer[offset + i];
                }
                BaseStream.Write(write_buf, 0, chunk);
                offset += chunk;
                count -= chunk;
            }
        }

        public override void WriteByte(byte value)
        {
            BaseStream.WriteByte((byte)-value);
        }
    }
}
