using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Compression;

namespace GameRes.Formats.KID
{
    // Custom entry class to store metadata required for P2T reconstruction
    public class P2tEntry : Entry
    {
        public byte[] TimHeaderChunk { get; set; }
        public int RealUncompressedSize { get; set; }
        public uint CompressedSize { get; set; }
    }

    [Export(typeof(ArchiveFormat))]
    public class P2tOpener : ArchiveFormat
    {
        public override string Tag { get { return "P2T"; } }
        public override string Description { get { return "KID P2T archive"; } }
        public override uint Signature { get { return 0; } }
        public override bool IsHierarchic { get { return false; } }
        public override bool CanWrite { get { return false; } }

        // Standard TIM2 header magic prefix
        private static readonly byte[] Tim2Magic = new byte[] {
            0x54, 0x49, 0x4D, 0x32, 0x04, 0x00, 0x01, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
        };

        public override ArcFile TryOpen(ArcView file)
        {
            if (file.MaxOffset < 0x20) return null;

            uint headerEnd = file.View.ReadUInt32(0x08);
            uint numFiles = file.View.ReadUInt32(0x0C);
            uint dataStartBase = file.View.ReadUInt32(0x10);

            if (numFiles == 0 || numFiles > 0xFFFF || headerEnd >= file.MaxOffset) return null;

            // Basic validation check at the start of the file table
            uint checkFF = file.View.ReadUInt32(headerEnd + 48);
            if (checkFF != 0xFFFFFFFF) return null;

            var dir = new List<Entry>((int)numFiles);
            long currentEntryPtr = headerEnd;
            const int EntrySize = 64;

            for (int i = 0; i < numFiles; i++)
            {
                // Mapping structure:
                // 48-51: FFFFFFFF padding
                // 52-55: Data Offset (Raw)
                // 56-59: Flag (usually 01)
                // 60-63: Compressed Length
                uint rawOffset = file.View.ReadUInt32(currentEntryPtr + 52);
                uint compressedLen = file.View.ReadUInt32(currentEntryPtr + 60);

                long sizeInfoOffset = dataStartBase + rawOffset;
                if (sizeInfoOffset + 4 > file.MaxOffset) break;

                uint realUncompressedSize = file.View.ReadUInt32(sizeInfoOffset);
                byte[] timHeaderChunk = file.View.ReadBytes(currentEntryPtr, 48);

                var entry = new P2tEntry
                {
                    Name = string.Format("{0:D4}.tm2", i),
                    Type = "image",
                    Offset = sizeInfoOffset + 4,
                    Size = realUncompressedSize, // Display uncompressed size in list
                    RealUncompressedSize = (int)realUncompressedSize,
                    CompressedSize = compressedLen,
                    TimHeaderChunk = timHeaderChunk
                };

                dir.Add(entry);
                currentEntryPtr += EntrySize;
            }

            return new ArcFile(file, this, dir);
        }

        public override Stream OpenEntry(ArcFile arc, Entry entry)
        {
            var p2tEntry = entry as P2tEntry;
            if (p2tEntry == null) return base.OpenEntry(arc, entry);

            // Read the compressed block using the stored length
            var compressedData = arc.File.View.ReadBytes(entry.Offset, p2tEntry.CompressedSize);

            // LZSS decompression (Okumura variant)
            var decompressedBody = LzssDecompress(compressedData);

            // Align data to expected uncompressed size
            if (decompressedBody.Length != p2tEntry.RealUncompressedSize)
                Array.Resize(ref decompressedBody, p2tEntry.RealUncompressedSize);

            // Reconstruct final file: Magic + Table Header Chunk + Decompressed Data
            int finalSize = Tim2Magic.Length + p2tEntry.TimHeaderChunk.Length + decompressedBody.Length;
            var outputData = new byte[finalSize];

            Buffer.BlockCopy(Tim2Magic, 0, outputData, 0, Tim2Magic.Length);
            Buffer.BlockCopy(p2tEntry.TimHeaderChunk, 0, outputData, Tim2Magic.Length, p2tEntry.TimHeaderChunk.Length);
            Buffer.BlockCopy(decompressedBody, 0, outputData, Tim2Magic.Length + p2tEntry.TimHeaderChunk.Length, decompressedBody.Length);

            return new MemoryStream(outputData);
        }

        private byte[] LzssDecompress(byte[] input)
        {
            var output = new List<byte>(input.Length * 2);
            const int n = 4096;
            const int threshold = 2;
            int r = n - 18;
            var textBuf = new byte[n + 18 - 1];
            Array.Clear(textBuf, 0, textBuf.Length);

            int flags = 0;
            int srcPos = 0;

            while (srcPos < input.Length)
            {
                flags >>= 1;
                if ((flags & 0x100) == 0)
                {
                    if (srcPos >= input.Length) break;
                    flags = input[srcPos++] | 0xFF00;
                }

                if ((flags & 1) != 0)
                {
                    if (srcPos >= input.Length) break;
                    byte c = input[srcPos++];
                    output.Add(c);
                    textBuf[r] = c;
                    r = (r + 1) & (n - 1);
                }
                else
                {
                    if (srcPos + 1 >= input.Length) break;
                    int i = input[srcPos++];
                    int j = input[srcPos++];
                    int offset = i | ((j & 0xF0) << 4);
                    int len = (j & 0x0F) + threshold;
                    for (int k = 0; k <= len; k++)
                    {
                        byte c = textBuf[(offset + k) & (n - 1)];
                        output.Add(c);
                        textBuf[r] = c;
                        r = (r + 1) & (n - 1);
                    }
                }
            }
            return output.ToArray();
        }
    }
}