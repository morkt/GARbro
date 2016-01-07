//! \file       Utility.cs
//! \date       Sun Jul 06 07:40:34 2014
//! \brief      utility classes.
//
// Copyright (C) 2014 by morkt
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
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Input;
using GARbro.GUI.Strings;

namespace GARbro.GUI
{
    internal class NativeMethods
    {
        [DllImport ("shlwapi.dll", CharSet = CharSet.Unicode)]
        internal static extern int StrCmpLogicalW (string psz1, string psz2);

        [DllImport ("gdi32.dll")]
        internal static extern int GetDeviceCaps (IntPtr hDc, int nIndex);

        [DllImport ("user32.dll")]
        internal static extern IntPtr GetDC (IntPtr hWnd);

        [DllImport ("user32.dll")]
        internal static extern int ReleaseDC (IntPtr hWnd, IntPtr hDc);
    }

    public static class Desktop
    {
        public static int DpiX { get { return dpi_x; } }
        public static int DpiY { get { return dpi_y; } }
        
        public const int LOGPIXELSX = 88;
        public const int LOGPIXELSY = 90;

        private static int dpi_x = GetCaps (LOGPIXELSX);
        private static int dpi_y = GetCaps (LOGPIXELSY);

        public static int GetCaps (int cap)
        {
            IntPtr hdc = NativeMethods.GetDC (IntPtr.Zero);
            if (hdc == IntPtr.Zero)
                return 96;
            int dpi = NativeMethods.GetDeviceCaps (hdc, cap);
            NativeMethods.ReleaseDC (IntPtr.Zero, hdc);
            return dpi;
        }
    }

    public sealed class NumericStringComparer : IComparer<string>
    {
        public int Compare (string a, string b)
        {
            return NativeMethods.StrCmpLogicalW (a, b);
        }
    }

    public class WaitCursor : IDisposable
    {
        private Cursor m_previousCursor;

        public WaitCursor()
        {
            m_previousCursor = Mouse.OverrideCursor;
            Mouse.OverrideCursor = Cursors.Wait;
        }

        #region IDisposable Members
        bool disposed = false;
        public void Dispose()
        {
            Dispose (true);
            GC.SuppressFinalize (this);
        }

        protected virtual void Dispose (bool disposing)
        {
            if (!disposed)
            {
                Mouse.OverrideCursor = m_previousCursor;
                disposed = true;
            }
        }
        #endregion
    }

    public static class Localization
    {
        public static string Plural (int n, string msg_id)
        {
            string suffix;
            if (CultureInfo.CurrentUICulture.Name == "ru-RU")
            {
                suffix = (n%10==1 && n%100!=11 ? "1" : n%10>=2 && n% 10<=4 && (n%100<10 || n%100>=20) ? "2" : "3");
            }
            else // assume en-EN
            {
                suffix = 1 == n ? "1" : "2";
            }
            try
            {
                var res = guiStrings.ResourceManager.GetString (msg_id+suffix);
                if (null == res)
                {
                    Trace.WriteLine (string.Format ("Missing string resource for '{0}' token", msg_id+suffix));
                    if (suffix != "1")
                        res = guiStrings.ResourceManager.GetString (msg_id+"1");
                    if (null == res)
                        res = guiStrings.ResourceManager.GetString (msg_id);
                }
                return res ?? msg_id;
            }
            catch (Exception X)
            {
                Trace.WriteLine (X.Message, "Localization.Plural");
                return msg_id;
            }
        }

        public static string Format (string msg_id, int n)
        {
            return string.Format (Plural (n, msg_id), n);
        }

        // Localization.Format ("{0:file:files} copied", count);
//        public static string Format (string format, params object[] args);
    }
}
