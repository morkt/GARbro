using GameRes.Utility;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.Kid
{
    [Export(typeof(ArchiveFormat))]
    public class DATRAWOpener : ArchiveFormat
    {
        public override string Tag { get { return "DAT/KID & MAGES PS2 DAT RAW"; } }
        public override string Description { get { return "DAT/KID & MAGES PS2 DAT RAW"; } }
        public override uint Signature { get { return 0; } } //actually zero
        public override bool IsHierarchic { get { return false; } }
        public override bool CanWrite { get { return false; } }

        private static readonly uint dataEntryCount = 4096;

        public override ArcFile TryOpen(ArcView file)
        {
            var archivename = Path.GetFileNameWithoutExtension(file.Name);
            var dir = new List<Entry>();
            for (int i = 0; i < dataEntryCount; i++)
            {
                uint offset = file.View.ReadUInt32(i * 8);
                uint size = file.View.ReadUInt32(i * 8 + 4);
                offset = offset * 2048 + 0x8000;
                size *= 1024;
                if (offset > file.MaxOffset || size > file.MaxOffset)
                {
                    throw new InvalidFormatException();
                }
                if (size == 0) continue;

                var entry = Create<Entry>(archivename + i.ToString("D5"));
                entry.Offset = offset;
                entry.Size = size;
                dir.Add(entry);
            }
            if (dir.Count == 0)
                return null;
            return new ArcFile(file, this, dir);
        }

        /*public override Stream OpenEntry(ArcFile arc, Entry entry)
        {
            IBinaryStream input = arc.File.CreateStream(entry.Offset, entry.Size, entry.Name);
            if (input.Signature == 0x535043) // 'CPS'
            {
                using (input)
                    return UnpackCps(input);
            }
            return input.AsStream;
        }*/
        Stream UnpackCps(IBinaryStream input)
        {
            var header = input.ReadHeader(0x10);
            int packed_size = header.ToInt32(4);
            int compression = header.ToUInt16(0xA);
            int unpacked_size = header.ToInt32(0xC);

            //input.Seek(-4, SeekOrigin.End);
            input.Seek(packed_size - 4, SeekOrigin.Begin);
            uint key_offset = input.ReadUInt32() - 0x7534682;
            input.Position = key_offset;
            uint key = input.ReadUInt32() + key_offset + 0x3786425;

            var decryptor = new CpsTransform(packed_size, (int)key_offset, key);
            using (var decoded = new InputCryptoStream(input.AsStream, decryptor))
            using (var cps = new BinaryStream(decoded, input.Name))
            {
                var output = new byte[unpacked_size];
                if ((compression & 1) != 0)
                {
                    cps.ReadInt32();
                    UnpackLnd(cps, output);
                }
                else if ((compression & 2) != 0)
                {
                    UnpackLnd16(cps, output);
                }
                else
                {
                    cps.ReadInt32();
                    cps.Read(output, 0, unpacked_size);
                }
                return new BinMemoryStream(output);
            }
        }
        internal static void UnpackLnd(IBinaryStream input, byte[] output)
        {
            int unpacked_size = output.Length;
            int dst = 0;
            while (dst < unpacked_size)
            {
                int ctl = input.ReadByte();
                if (-1 == ctl)
                    break;
                if ((ctl & 0x80) != 0)
                {
                    if ((ctl & 0x40) != 0)
                    {
                        int count = (ctl & 0x1F) + 2;
                        if ((ctl & 0x20) != 0)
                            count += input.ReadUInt8() << 5;
                        count = Math.Min(count, unpacked_size - dst);
                        byte v = input.ReadUInt8();
                        for (int i = 0; i < count; ++i)
                            output[dst++] = v;
                    }
                    else
                    {
                        int count = ((ctl >> 2) & 0xF) + 2;
                        int offset = ((ctl & 3) << 8) + input.ReadUInt8() + 1;
                        count = Math.Min(count, unpacked_size - dst);
                        Binary.CopyOverlapped(output, dst - offset, dst, count);
                        dst += count;
                    }
                }
                else if ((ctl & 0x40) != 0)
                {
                    int length = Math.Min((ctl & 0x3F) + 2, unpacked_size - dst);
                    int count = input.ReadUInt8();
                    input.Read(output, dst, length);
                    dst += length;
                    count = Math.Min(count * length, unpacked_size - dst);
                    if (count > 0)
                    {
                        Binary.CopyOverlapped(output, dst - length, dst, count);
                        dst += count;
                    }
                }
                else
                {
                    int count = (ctl & 0x1F) + 1;
                    if ((ctl & 0x20) != 0)
                        count += input.ReadUInt8() << 5;
                    count = Math.Min(count, unpacked_size - dst);
                    input.Read(output, dst, count);
                    dst += count;
                }
            }
        }
        static void UnpackLnd16(IBinaryStream input, byte[] output)
        {
            throw new NotImplementedException("KID Lnd16 compression not implemented.");
        }
    }
}
