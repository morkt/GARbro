//! \file       Utility.cs
//! \date       Sun Jul 06 07:40:34 2014
//! \brief      utility classes.
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
        public static string Plural (int n, string en_singular)
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
                var res = guiStrings.ResourceManager.GetString ("LP"+en_singular+suffix);
                return res ?? en_singular;
            }
            catch
            {
                return en_singular;
            }
        }

        // Localization.Format ("{0:file:files} copied", count);
//        public static string Format (string format, params object[] args);
    }
}
