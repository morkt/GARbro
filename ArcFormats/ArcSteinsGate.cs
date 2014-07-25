//! \file       ArcSteinsGate.cs
//! \date       Thu Jul 24 23:36:01 2014
//! \brief      Nitro+ Steins;Gate archive implementation.
//

using System.IO;
using System.Text;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using GameRes.Formats.Strings;
using GameRes.Utility;

namespace GameRes.Formats
{
    [Export(typeof(ArchiveFormat))]
    public class NpaSteinsGateOpener : ArchiveFormat
    {
        public override string Tag { get { return "NPA-SG"; } }
        public override string Description { get { return arcStrings.NPASteinsGateDescription; } }
        public override uint Signature { get { return 0; } }
        public override bool IsHierarchic { get { return true; } }

        public NpaSteinsGateOpener ()
        {
            Extensions = new string[] { "npa" };
        }

        internal static readonly byte[] KeyString = {
            'B'^0xff, 'U'^0xff, 'C'^0xff, 'K'^0xff,
            'T'^0xff, 'I'^0xff, 'C'^0xff, 'K'^0xff
        };

        public override ArcFile TryOpen (ArcView file)
        {
            int index_size = file.View.ReadInt32 (0);
            if (index_size < 0x14 || index_size >= file.MaxOffset || index_size > 0xffffff)
                return null;

            var stream = new SteinsGateEncryptedStream (file, 4, (uint)index_size);
            using (var header = new BinaryReader (stream, Encoding.Unicode))
            {
                int entry_count = header.ReadInt32();
                if (entry_count <= 0)
                    return null;
                int average_entry_size = index_size / entry_count;
                if (average_entry_size < 0x11)
                    return null;

                var dir = new List<Entry> ((int)entry_count);
                for (uint i = 0; i < entry_count; ++i)
                {
                    int name_length = header.ReadInt32();
                    if (name_length+0x10 > index_size)
                        return null;
                    string filename = new string (header.ReadChars (name_length/2));

                    var entry = FormatCatalog.Instance.CreateEntry (filename);
                    entry.Size = header.ReadUInt32();
                    entry.Offset = header.ReadInt64();
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);

                    index_size -= name_length+0x10;
                }
                return new ArcFile (file, this, dir);
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            return new SteinsGateEncryptedStream (arc.File, entry.Offset, entry.Size);
        }

        internal void Encrypt (byte[] buffer, int offset, int count)
        {
            for (int i = 0; i < count; ++i)
            {
                buffer[offset+i] ^= KeyString[i & 7];
            }
        }
    }

    public class SteinsGateEncryptedStream : ArcView.ArcStream
    {
        public SteinsGateEncryptedStream (ArcView file, long offset, uint size)
            : base (file, offset, size)
        {
        }

        public override int Read (byte[] buffer, int offset, int count)
        {
            int position = (int)Position & 7;
            int read = base.Read (buffer, offset, count);
            if (read > 0)
            {
                for (int i = 0; i < read; ++i)
                {
                    buffer[offset+i] ^= NpaSteinsGateOpener.KeyString[(position+i)&7];
                }
            }
            return read;
        }

        public override int ReadByte ()
        {
            int position = (int)Position & 7;
            int b = base.ReadByte();
            if (-1 != b)
            {
                b ^= NpaSteinsGateOpener.KeyString[position];
            }
            return b;
        }
    }
}
