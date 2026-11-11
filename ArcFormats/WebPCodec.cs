//! \file       WebPCodec.cs
//! \date       Sun Apr 12 2026
//! \brief      Google WebP image format.
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
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace GameRes.Formats
{
    public class WebPCodec
    {
        [DllImport("libwebp.dll", EntryPoint = "WebPGetInfo", CallingConvention = CallingConvention.Cdecl)]
        public static extern int WebPGetInfo ([MarshalAs(UnmanagedType.LPArray)] byte[] data, UIntPtr data_size, ref int width, ref int height);

        [DllImport("libwebp.dll", EntryPoint = "WebPDecodeBGRAInto", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr WebPDecodeBGRAInto ([MarshalAs(UnmanagedType.LPArray)] byte[] data, UIntPtr data_size, IntPtr output_buffer, UIntPtr output_buffer_size, int output_stride);

        [DllImport("kernel32.dll", SetLastError = true, CallingConvention = CallingConvention.StdCall)]
        static extern IntPtr LoadLibraryEx (string lpFileName, IntPtr hReservedNull, uint dwFlags);

        static bool loaded = false;

        const uint LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR = 0x00000100;
        const uint LOAD_LIBRARY_SEARCH_SYSTEM32 = 0x00000800;

        public static void Load ()
        {
            if (loaded)
                return;
            var dir = Path.GetDirectoryName (Assembly.GetExecutingAssembly ().Location);
            dir = Path.Combine (dir, (IntPtr.Size == 4) ? "x86" : "x64");
            var path = Path.Combine (dir, "libwebp.dll");
            path = Path.GetFullPath (path);
            var lib = LoadLibraryEx (path, IntPtr.Zero, LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_SYSTEM32);
            if (IntPtr.Zero == lib)
                throw new Win32Exception (Marshal.GetLastWin32Error ());
            loaded = true;
        }
    }
}
