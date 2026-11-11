using GameRes.Utility;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Xml.Linq;

namespace GameRes.Formats.MAGES
{
    [Export(typeof(ArchiveFormat))]
    public class GTFOpener : ArchiveFormat
    {
        public override string Tag { get { return "GTF/Rozen Maiden PS3 Image archive"; } }
        public override string Description { get { return "MAGES Rozen Maiden Wechseln Sie Welt ab PS3 BLJM61120 Image archive"; } }
        public override uint Signature { get { return 0xFF000202; } } // 'FF000202'
        public override bool IsHierarchic { get { return false; } }
        public override bool CanWrite { get { return false; } }

        public override ArcFile TryOpen(ArcView file)
        {
            int count = Binary.BigEndian(file.View.ReadInt32(8));
            if (!IsSaneCount(count))
                return null;
            string filename = Path.GetFileNameWithoutExtension(file.Name);
            var dir = new List<Entry>(count);
            for (int i = 0; i < count; i++)
            {
                var entry = Create<Entry_RawImage>(filename + '_' + i.ToString());
                entry.Offset = Binary.BigEndian(file.View.ReadUInt32(16 + 36 * i));
                entry.Size = Binary.BigEndian(file.View.ReadUInt32(16 + 36 * i + 4));
                entry.width = Binary.BigEndian(file.View.ReadUInt16(32 + 36 * i));
                entry.height = Binary.BigEndian(file.View.ReadUInt16(32 + 36 * i + 2));
                entry.Type = "image";
                dir.Add(entry);
            }
            return new ArcFile(file, this, dir);
        }

        public override Stream OpenEntry(ArcFile arc, Entry entry)
        {
            var compentry = (Entry_RawImage)entry;
            IBinaryStream input = arc.File.CreateStream(entry.Offset, entry.Size, entry.Name);
            return ReadImageGTF(input, compentry.width, compentry.height);
        }

        internal class Entry_RawImage : Entry
        {
            public ushort width { get; set; }
            public ushort height { get; set; }
        }

        /*[Export(typeof(ImageFormat))]
        public class GTFFormat : ImageFormat
        {
            public override string Tag { get { return "MAGES PS3/PSV Image Format"; } }
            public override string Description { get { return "MAGES PS3/PSV Image Format"; } }
            public override uint Signature { get { return 0; } }

            public override ImageMetaData ReadMetaData(IBinaryStream file)
            {
                throw new System.NotImplementedException();
            }
            public override ImageData Read(IBinaryStream file, ImageMetaData info)
            {
                throw new System.NotImplementedException();
            }
            public override void Write(Stream file, ImageData bitmap)
            {
                throw new System.NotImplementedException();
            }
        }*/

        /// <summary>
        /// This method returns MAGES/ImageBIN format Image data stream, please ensure it's decoding method exists.
        /// </summary>
        /// <param name="input">GTF or BIN raw ARGB32 data, start in the upper left corner, each row.</param>
        /// <param name="width"></param>
        /// <param name="height"></param>
        /// <returns>MAGES/ImageBIN format Image data stream</returns>
        static Stream ReadImageGTF(IBinaryStream input, ushort width, ushort height)
        {
            //List<byte> bytes = new List<byte>();
            byte[] widths = BitConverter.GetBytes(width);
            byte[] heights = BitConverter.GetBytes(height);
            byte[] bpp = BitConverter.GetBytes((uint)32);
            /*Stream output = new MemoryStream();
            output.Write(widths, 0, widths.Length);
            output.Write(heights, 0, heights.Length);
            output.Write(bpp, 0, bpp.Length);
            output.Write(input.ReadBytes((int)input.Length), 0, (int)input.Length);
            byte[] bytes = new byte[output.Length];
            output.Seek(0, SeekOrigin.Begin);
            output.Read(bytes, 0, bytes.Length);
            output.Dispose();*/

            byte[] inputData = input.ReadBytes((int)input.Length);
            byte[] outputData = new byte[widths.Length + heights.Length + bpp.Length + inputData.Length];
            Buffer.BlockCopy(widths, 0, outputData, 0, widths.Length);
            Buffer.BlockCopy(heights, 0, outputData, widths.Length, heights.Length);
            Buffer.BlockCopy(bpp, 0, outputData, widths.Length + heights.Length, bpp.Length);
            Buffer.BlockCopy(inputData, 0, outputData, widths.Length + heights.Length + bpp.Length, inputData.Length);
            return new BinMemoryStream(outputData);

            /*List<byte> pixels = new List<byte>();
            for (int i = 0; i < input.Length; i++)
            {
                var pixel = input.ReadBytes(4); //ARGB
                                               //BGRA
                pixels.Add(pixel[3]);
                pixels.Add(pixel[2]);
                pixels.Add(pixel[1]);
                pixels.Add(pixel[0]);
            }*/
        }
    }
}
