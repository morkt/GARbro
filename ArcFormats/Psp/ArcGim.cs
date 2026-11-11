using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameRes.Utility;

namespace GameRes.Formats.Psp
{
    internal class GimMetaData : ImageMetaData
    {
        public int ImageInfoOffset;
        public int PaletteInfoOffset;
        public int PaletteBlockEnd;
        public bool IsLittleEndian;
        public int Format;
        public int Order; // 0=Linear, 1=Swizzled
        public int ImgDataRelOffset;
        public int PalDataRelOffset;
        public int BufferWidth; // Aligned width
    }

    [Export(typeof(ImageFormat))]
    public class GimFormat : ImageFormat
    {
        public override string Tag { get { return "GIM"; } }
        public override string Description { get { return "Sony GIM Image"; } }
        public override uint Signature { get { return 0; } }

        public override ImageMetaData ReadMetaData(IBinaryStream file)
        {
            var header = file.ReadBytes(16);
            if (header.Length < 16) return null;

            // Header check for Endianness detection
            bool littleEndian = true;
            if (header[0] == 'M' && header[1] == 'I' && header[2] == 'G') littleEndian = true;
            else if (header[0] == 'G' && header[1] == 'I' && header[2] == 'M') littleEndian = false;
            else return null;

            // Read the entire stream into an array to facilitate navigation
            // GIMs are small, so this is safe and fast.
            byte[] data = new byte[file.Length];
            file.Position = 0;
            file.Read(data, 0, (int)file.Length);

            int imageInfoOffset = -1;
            int paletteInfoOffset = -1;
            int paletteBlockEnd = -1;
            int offset = 0x10;
            int loop = 0;

            // Block scanning loop (Verviewer style)
            while (offset + 0x10 <= data.Length && loop < 128)
            {
                ushort id = ReadUInt16(data, offset, littleEndian);
                // 0x02=EndFile, 0x03=EndImage (Skip), 0x04=Image, 0x05=Palette, 0xFF=FileInfo
                if (id == 0xFF) break; 

                uint size = ReadUInt32(data, offset + 4, littleEndian);
                uint next = ReadUInt32(data, offset + 8, littleEndian);
                uint headerSize = ReadUInt32(data, offset + 0xC, littleEndian);

                if (size < headerSize || headerSize == 0) break; // Invalid data

                // "Next" logic: If 0, usually the next block is sequential (size)
                int nextOffset = (next != 0) ? (int)next : (int)size;

                int blockStart = offset;
                int blockEnd = blockStart + (int)size;
                int subHeader = blockStart + (int)headerSize;

                if (blockEnd > data.Length) break;

                if (id == 4) // Image Section
                {
                    if (imageInfoOffset < 0) imageInfoOffset = subHeader;
                }
                else if (id == 5) // Palette Section
                {
                    if (paletteInfoOffset < 0)
                    {
                        paletteInfoOffset = subHeader;
                        paletteBlockEnd = blockEnd;
                    }
                }

                offset = blockStart + nextOffset;
                loop++;
            }

            if (imageInfoOffset < 0) return null;

            ushort imgFormat = ReadUInt16(data, imageInfoOffset + 4, littleEndian);
            ushort pixelOrder = ReadUInt16(data, imageInfoOffset + 6, littleEndian);
            ushort width = ReadUInt16(data, imageInfoOffset + 8, littleEndian);
            ushort height = ReadUInt16(data, imageInfoOffset + 0xA, littleEndian);
            ushort bpp = ReadUInt16(data, imageInfoOffset + 0xC, littleEndian);
            uint imgRel = ReadUInt32(data, imageInfoOffset + 0x1C, littleEndian);
            uint palRel = 0;

            if (paletteInfoOffset > 0)
                 palRel = ReadUInt32(data, paletteInfoOffset + 0x1C, littleEndian);

            // Texture Alignment (Buffer Width)
            // PSP aligns in blocks of 16 bytes (128 bits)
            int align = 16; 
            int pixelsPerBlock = (align * 8) / Math.Max(1, (int)bpp);
            int bufferWidth = (width + pixelsPerBlock - 1) & ~(pixelsPerBlock - 1);

            return new GimMetaData
            {
                Width = width,
                Height = height,
                BPP = bpp,
                Format = imgFormat,
                Order = pixelOrder,
                BufferWidth = bufferWidth,
                ImageInfoOffset = imageInfoOffset,
                PaletteInfoOffset = paletteInfoOffset,
                PaletteBlockEnd = paletteBlockEnd,
                ImgDataRelOffset = (int)imgRel,
                PalDataRelOffset = (int)palRel,
                IsLittleEndian = littleEndian
            };
        }

        public override ImageData Read(IBinaryStream file, ImageMetaData info)
        {
            var meta = (GimMetaData)info;
            
            byte[] data = new byte[file.Length];
            file.Position = 0;
            file.Read(data, 0, (int)file.Length);

            int imgDataOffset = meta.ImageInfoOffset + meta.ImgDataRelOffset;
            
            // Safe pixel buffer reading
            int width = (int)meta.Width;
            int height = (int)meta.Height;
            int bpp = meta.BPP;
            int bufferWidth = meta.BufferWidth;

            // Internal PSP Stride (can be larger than image width)
            int bufferStride = (bufferWidth * bpp) / 8;
            int totalBytes = bufferStride * height;

            if (imgDataOffset + totalBytes > data.Length)
                totalBytes = Math.Max(0, data.Length - imgDataOffset);

            byte[] pixels = new byte[totalBytes];
            Buffer.BlockCopy(data, imgDataOffset, pixels, 0, totalBytes);

            // --- UNSWIZZLE ---
            if (meta.Order == 1)
            {
                pixels = UnswizzlePSP(pixels, width, height, bufferWidth, bpp);
            }
            else
            {
                // If linear but with lateral padding, remove it
                if (bufferWidth != width)
                    pixels = RemovePadding(pixels, width, height, bufferWidth, bpp);
                
                // If 4bpp Linear, we still need to fix Nibbles
                if (bpp == 4)
                    SwapNibbles(pixels);
            }

            // --- PALETTE ---
            BitmapPalette palette = null;
            if (meta.Format == 0x04 || meta.Format == 0x05) 
            {
                if (meta.PaletteInfoOffset > 0 && meta.PaletteBlockEnd > 0)
                {
                    int palDataOffset = meta.PaletteInfoOffset + meta.PalDataRelOffset;
                    ushort palFmt = ReadUInt16(data, meta.PaletteInfoOffset + 4, meta.IsLittleEndian);
                    int entrySize = (palFmt == 3) ? 4 : 2;
                    
                    int palBytes = meta.PaletteBlockEnd - palDataOffset;
                    int colorCount = palBytes / entrySize;
                    
                    // Limit colors by BPP
                    if (meta.Format == 0x04) colorCount = Math.Min(colorCount, 16);
                    else if (meta.Format == 0x05) colorCount = Math.Min(colorCount, 256);

                    if (palDataOffset + (colorCount * entrySize) <= data.Length)
                    {
                        Color[] colors = new Color[colorCount];
                        for(int i=0; i<colorCount; i++)
                        {
                            int pOff = palDataOffset + (i * entrySize);
                            if (entrySize == 4) // RGBA8888
                            {
                                byte r = data[pOff];
                                byte g = data[pOff+1];
                                byte b = data[pOff+2];
                                byte a = data[pOff+3];
                                // IMPORTANT: Swap R and B to fix "blue face"
                                colors[i] = Color.FromArgb(a, r, g, b); 
                            }
                            else // 16 bit
                            {
                                ushort p = ReadUInt16(data, pOff, meta.IsLittleEndian);
                                colors[i] = DecodePspColor(p, palFmt);
                            }
                        }
                        palette = new BitmapPalette(colors);
                    }
                }
            }

            PixelFormat format = PixelFormats.Bgra32;
            int outputStride = (width * bpp + 7) / 8;
            byte[] outputPixels = pixels;

            if (bpp == 8) format = PixelFormats.Indexed8;
            else if (bpp == 4) format = PixelFormats.Indexed4;
            else if (bpp == 16) 
            {
                format = PixelFormats.Bgra32;
                outputPixels = Convert16to32(pixels, width, height, meta.Format, meta.IsLittleEndian);
                outputStride = width * 4;
            }
            else if (bpp == 32) format = PixelFormats.Bgra32;

            return ImageData.Create(info, format, palette, outputPixels, outputStride);
        }

        public override void Write(Stream file, ImageData image)
        {
            throw new NotImplementedException();
        }

        // --- HELPERS ---

        static ushort ReadUInt16(byte[] data, int offset, bool littleEndian)
        {
            if (offset + 1 >= data.Length) return 0;
            return littleEndian
                ? (ushort)(data[offset] | (data[offset + 1] << 8))
                : (ushort)((data[offset] << 8) | data[offset + 1]);
        }

        static uint ReadUInt32(byte[] data, int offset, bool littleEndian)
        {
             if (offset + 3 >= data.Length) return 0;
             return littleEndian
                ? (uint)(data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24))
                : (uint)((data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3]);
        }

        static byte[] UnswizzlePSP(byte[] src, int width, int height, int bufferWidth, int bpp)
        {
            // 4BPP TRICK: Treat 4BPP as 8BPP with half width for coordinate calculation.
            // This moves bytes correctly. THEN we swap nibbles.
            int procBpp = bpp;
            int procWidth = width;
            int procBufferWidth = bufferWidth;
            
            if (bpp == 4)
            {
                procBpp = 8;
                procWidth = width / 2;
                procBufferWidth = bufferWidth / 2;
            }

            int blockWidth = 16; 
            int blockHeight = 8;
            
            if (procBpp == 16) { blockWidth = 16; blockHeight = 4; }
            else if (procBpp == 32) { blockWidth = 8; blockHeight = 4; }

            int dstStride = (procWidth * procBpp) / 8;
            byte[] dst = new byte[dstStride * height];
            int bppByte = procBpp / 8;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < procWidth; x++)
                {
                    int bx = x / blockWidth;
                    int by = y / blockHeight;
                    int mx = x % blockWidth;
                    int my = y % blockHeight;

                    int blocksPerRow = procBufferWidth / blockWidth;
                    int blockSize = blockWidth * blockHeight * bppByte;
                    int blockIdx = (by * blocksPerRow) + bx;
                    int srcOffset = (blockIdx * blockSize) + ((my * blockWidth + mx) * bppByte);
                    int dstOffset = (y * dstStride) + (x * bppByte);

                    if (srcOffset + bppByte <= src.Length && dstOffset + bppByte <= dst.Length)
                    {
                        // For 4bpp, bppByte is 1 (because we simulate 8bpp).
                        byte val = src[srcOffset];
                        
                        // 4BPP LINES FIX:
                        // Unswizzle moved the correct Byte to the correct place.
                        // Now we need to invert nibbles (High/Low) because WPF reads differently from PSP.
                        if (bpp == 4)
                        {
                            val = (byte)(((val & 0x0F) << 4) | ((val & 0xF0) >> 4));
                        }

                        dst[dstOffset] = val;
                        
                        // For other formats (>8bpp), normal copy
                        if (bpp > 8)
                        {
                            for(int k=1; k<bppByte; k++) dst[dstOffset+k] = src[srcOffset+k];
                        }
                    }
                }
            }
            return dst;
        }

        static byte[] RemovePadding(byte[] src, int width, int height, int bufferWidth, int bpp)
        {
            int srcStride = (bufferWidth * bpp + 7) / 8;
            int dstStride = (width * bpp + 7) / 8;
            byte[] dst = new byte[dstStride * height];

            for (int y = 0; y < height; y++)
            {
                int copyLen = Math.Min(srcStride, dstStride);
                int sOff = y * srcStride;
                int dOff = y * dstStride;
                if (sOff + copyLen <= src.Length)
                    Array.Copy(src, sOff, dst, dOff, copyLen);
            }
            return dst;
        }

        static void SwapNibbles(byte[] data)
        {
            for (int i = 0; i < data.Length; i++)
            {
                byte b = data[i];
                data[i] = (byte)(((b & 0x0F) << 4) | ((b & 0xF0) >> 4));
            }
        }

        Color DecodePspColor(ushort v, int fmt)
        {
            int r=0, g=0, b=0, a=255;
            if (fmt == 0) { // 5650
                r=(v&0x1F)<<3; g=((v>>5)&0x3F)<<2; b=((v>>11)&0x1F)<<3;
            } else if (fmt == 1) { // 5551
                r=(v&0x1F)<<3; g=((v>>5)&0x1F)<<3; b=((v>>10)&0x1F)<<3; a=(v>>15)!=0?255:0;
            } else if (fmt == 2) { // 4444
                r=(v&0xF)<<4; g=((v>>4)&0xF)<<4; b=((v>>8)&0xF)<<4; a=((v>>12)&0xF)<<4;
            }
            // Swap R and B to fix inverted colors
            return Color.FromArgb((byte)a, (byte)b, (byte)g, (byte)r); 
        }

        byte[] Convert16to32(byte[] inp, int w, int h, int fmt, bool le)
        {
            byte[] outp = new byte[w * h * 4];
            for(int i=0; i<w*h; i++) {
                if(i*2+1>=inp.Length) break;
                ushort v = le 
                    ? (ushort)(inp[i*2] | (inp[i*2+1] << 8)) 
                    : (ushort)((inp[i*2] << 8) | inp[i*2+1]);
                Color c = DecodePspColor(v, fmt);
                int o = i*4;
                outp[o]=c.B; outp[o+1]=c.G; outp[o+2]=c.R; outp[o+3]=c.A;
            }
            return outp;
        }
    }
}