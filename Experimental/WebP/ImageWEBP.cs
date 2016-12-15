//! \file       ImageWEBP.cs
//! \date       Wed Apr 06 07:16:39 2016
//! \brief      Google WEBP image format.
//
// Copyright (C) 2016 by morkt
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to
// deal in the Software without restriction, including without limitation the
// rights to use, copy, modify, merge, publish, distribute, sublicense, and/or
// sell copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
// IN THE SOFTWARE.
//

using System;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameRes.Utility;

namespace GameRes.Formats.Google
{
    internal class WebPMetaData : ImageMetaData
    {
        public WebPFeature  Flags;
        public bool         IsLossless;
        public bool         HasAlpha;
        public long         DataOffset;
        public int          DataSize;
        public long         AlphaOffset;
        public int          AlphaSize;
    }

    [Flags]
    internal enum WebPFeature : uint
    {
        Fragments  = 0x0001,
        Animation  = 0x0002,
        Xmp        = 0x0004,
        Exif       = 0x0008,
        Alpha      = 0x0010,
        Iccp       = 0x0020,
    }

    [Export(typeof(ImageFormat))]
    public class WebPFormat : ImageFormat
    {
        public override string         Tag { get { return "WEBP"; } }
        public override string Description { get { return "Google WebP image format"; } }
        public override uint     Signature { get { return 0; } }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            if (0x46464952 != stream.Signature) // 'RIFF'
                return null;
            if (!stream.ReadHeader (12).AsciiEqual (8, "WEBP"))
                return null;
            var header = new byte[0x10];
            bool found_vp8x = false;
            var info = new WebPMetaData();
            int chunk_size;
            for (;;)
            {
                if (8 != stream.Read (header, 0, 8))
                    return null;
                chunk_size = LittleEndian.ToInt32 (header, 4);
                int aligned_size = (chunk_size + 1) & ~1;
                if (!found_vp8x && Binary.AsciiEqual (header, 0, "VP8X"))
                {
                    found_vp8x = true;
                    if (chunk_size < 10)
                        return null;
                    if (chunk_size > header.Length)
                        header = new byte[chunk_size];
                    if (chunk_size != stream.Read (header, 0, chunk_size))
                        return null;
                    info.Flags = (WebPFeature)LittleEndian.ToUInt32 (header, 0);
                    info.Width  = 1 + (uint)header.ToInt24 (4);
                    info.Height = 1 + (uint)header.ToInt24 (7);
                    if ((long)info.Width * info.Height >= (1L << 32))
                        return null;
                    continue;
                }
                if (Binary.AsciiEqual (header, 0, "VP8 ") || Binary.AsciiEqual (header, 0, "VP8L"))
                {
                    info.IsLossless = header[3] == 'L';
                    info.DataOffset = stream.Position;
                    info.DataSize = aligned_size;
                    if (!found_vp8x)
                    {
                        if (chunk_size < 10 || 10 != stream.Read (header, 0, 10))
                            return null;
                        if (info.IsLossless)
                        {
                            if (header[0] != 0x2F || (header[4] >> 5) != 0)
                                return null;
                            uint wh = LittleEndian.ToUInt32 (header, 1);
                            info.Width  = (wh & 0x3FFFu) + 1;
                            info.Height = ((wh >> 14) & 0x3FFFu) + 1;
                            info.HasAlpha = 0 != (header[4] & 0x10);
                        }
                        else
                        {
                            if (header[3] != 0x9D || header[4] != 1 || header[5] != 0x2A)
                                return null;
                            if (0 != (header[0] & 1)) // not a keyframe
                                return null;
                            info.Width  = LittleEndian.ToUInt16 (header, 6) & 0x3FFFu;
                            info.Height = LittleEndian.ToUInt16 (header, 8) & 0x3FFFu;
                        }
                    }
                    break;
                }
                if (Binary.AsciiEqual (header, 0, "ALPH"))
                {
                    info.AlphaOffset = stream.Position;
                    info.AlphaSize   = chunk_size;
                }
                stream.Seek (aligned_size, SeekOrigin.Current);
            }
            if (0 == info.Width || 0 == info.Height)
                return null;
            return info;
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            LibWebPLoader.Load();
            var input = stream.ReadBytes ((int)stream.Length);
            var bitmap = new WriteableBitmap ((int)info.Width, (int)info.Height, ImageData.DefaultDpiX,
                                              ImageData.DefaultDpiY, PixelFormats.Bgra32, null);
            bitmap.Lock();
            try
            {
                var output = bitmap.BackBuffer;
                int stride = bitmap.BackBufferStride;
                unsafe
                {
                    fixed (byte* data = input)
                    {
                        var result = WebPDecodeBGRAInto ((IntPtr)data, (UIntPtr)input.Length, output,
                                                         (UIntPtr)(stride * info.Height), stride);
                        if (result != output)
                            throw new InvalidFormatException ("WebP image decoder failed.");
                    }
                }
                bitmap.AddDirtyRect (new Int32Rect (0, 0, (int)info.Width, (int)info.Height));
            }
            finally
            {
                bitmap.Unlock();
            }
            bitmap.Freeze();
            return new ImageData (bitmap, info);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("WebPFormat.Write not implemented");
        }

        [DllImport ("libwebp.dll", EntryPoint = "WebPDecodeBGRAInto", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr WebPDecodeBGRAInto ([InAttribute()] IntPtr data, UIntPtr data_size, IntPtr output_buffer, UIntPtr output_buffer_size, int output_stride);
    }

    internal static class LibWebPLoader
    {
        [DllImport ("kernel32.dll", SetLastError = true, CallingConvention = CallingConvention.StdCall)]
        static extern IntPtr LoadLibraryEx (string lpFileName, IntPtr hReservedNull, uint dwFlags);

        static bool loaded = false;

        const uint LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR = 0x00000100;
        const uint LOAD_LIBRARY_SEARCH_SYSTEM32     = 0x00000800;

        public static void Load ()
        {
            if (loaded)
                return;
            var folder = Path.GetDirectoryName (Assembly.GetExecutingAssembly().Location);
            folder = Path.Combine (folder, (IntPtr.Size == 4) ? "x86" : "x64");
            var fullPath = Path.Combine (folder, "libwebp.dll");
            fullPath = Path.GetFullPath (fullPath);
            var handle = LoadLibraryEx (fullPath, IntPtr.Zero, LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_SYSTEM32);
            if (IntPtr.Zero == handle)
                throw new Win32Exception (Marshal.GetLastWin32Error());
            loaded = true;
        }
    }
}
