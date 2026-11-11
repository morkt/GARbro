using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq; // Added for list operations if necessary
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameRes.Utility;

namespace GameRes.Formats.Broccoli
{
    [Export(typeof(ImageFormat))]
    public class GrpFormat : ImageFormat
    {
        public override string         Tag { get { return "GRP/BROCCOLI"; } }
        public override string Description { get { return "Broccoli engine image"; } }
        public override uint     Signature { get { return 0; } } 
        
        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            // We need to decompress to know the real size
            using (var data = DecompressData(file))
            {
                if (data == null) return null;

                var size = data.Length;
                uint width = 0, height = 0;
                int bpp = 32; 

                // Same resolution logic as your Python script
                if (size == 1920000)      { width = 800; height = 600; }
                else if (size == 1228800) { width = 640; height = 480; }
                else if (size == 1536000) { width = 800; height = 480; }
                else if (size == 768000)  { width = 640; height = 400; }
                else if (size == 307200)  { width = 640; height = 480; bpp = 8; }
                else if (size == 96000)   { width = 200; height = 120; }
                else
                {
                    // Fallback: Try to read 4-byte header (Width/Height)
                    if (size > 4)
                    {
                        data.Position = 0;
                        var reader = new BinaryReader(data);
                        ushort w = reader.ReadUInt16();
                        ushort h = reader.ReadUInt16();
                        if (w * h * 4 == size - 4)
                        {
                            width = w; 
                            height = h;
                        }
                    }
                }

                if (width == 0) return null;

                return new ImageMetaData
                {
                    Width = width,
                    Height = height,
                    BPP = bpp,
                };
            }
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            using (var stream = DecompressData(file))
            {
                if (stream == null) throw new InvalidFormatException();

                // Skip 4-byte header if fallback logic was used
                int headerSkip = 0;
                if (stream.Length != info.Width * info.Height * (info.BPP / 8))
                {
                    if (stream.Length == (info.Width * info.Height * 4) + 4)
                        headerSkip = 4;
                }

                stream.Position = headerSkip;

                // Case 1: Isolated Mask (Grayscale)
                if (info.BPP == 8)
                {
                    var gray = new byte[info.Width * info.Height];
                    stream.Read(gray, 0, gray.Length);
                    return ImageData.Create(info, PixelFormats.Gray8, null, gray);
                }

                // Case 2: Colored Image (BGRX)
                // Due to API limitations, we won't load the mask (_m) here.
                // GARbro will show the image with a black/solid background.
                // Use your Python script to combine with the mask later.
                
                var pixels = new byte[info.Width * info.Height * 4];
                stream.Read(pixels, 0, pixels.Length);

                // The format is usually Bgr32 (the 4th byte is garbage/padding, not real alpha)
                return ImageData.Create(info, PixelFormats.Bgr32, null, pixels);
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException("Use repack.py");
        }

        private MemoryStream DecompressData(IBinaryStream input)
        {
            input.Position = 0;
            byte[] buffer = input.ReadBytes(2048);
            int zlibOffset = -1;

            // Search for ZLIB signature (78 9C, etc)
            for (int i = 0; i < buffer.Length - 1; i++)
            {
                if (buffer[i] == 0x78 && 
                   (buffer[i+1] == 0x9C || buffer[i+1] == 0xDA || buffer[i+1] == 0x01))
                {
                    zlibOffset = i;
                    break;
                }
            }

            if (zlibOffset == -1) return null;

            try 
            {
                // Skip ZLIB header (2 bytes) to use standard DeflateStream
                input.Position = zlibOffset + 2; 

                using (var zStream = new System.IO.Compression.DeflateStream(input.AsStream, System.IO.Compression.CompressionMode.Decompress, true))
                {
                    var output = new MemoryStream();
                    zStream.CopyTo(output);
                    output.Position = 0;
                    return output;
                }
            }
            catch
            {
                return null;
            }
        }
    }
}