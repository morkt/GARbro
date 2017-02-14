// Game Resource Browser
//
// Copyright (C) 2014-2015 by morkt
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
using System.IO;
using System.Linq;
using System.Windows;
using System.Diagnostics;
using GARbro.GUI.Properties;
using GameRes;
using GameRes.Compression;
using System.Reflection;

namespace GARbro.GUI
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        const StringComparison StringIgnoreCase = StringComparison.OrdinalIgnoreCase;

        public static string       Name { get { return "GARbro"; } }
        public static string FormatsDat { get { return "Formats.dat"; } }

        /// <summary>
        /// Initial browsing directory.
        /// </summary>
        public string InitPath { get; private set; }

        void ApplicationStartup (object sender, StartupEventArgs e)
        {
            string exe_dir = Path.GetDirectoryName (System.Reflection.Assembly.GetExecutingAssembly().Location);
#if DEBUG
            Trace.Listeners.Add (new TextWriterTraceListener (Path.Combine (exe_dir, "trace.log")));
            Trace.AutoFlush = true;
#endif
            Trace.WriteLine ("ApplicationStartup --------------------------------", "GARbro.GUI.App");
            this.DispatcherUnhandledException += (s, args) =>
            {
                Trace.WriteLine (string.Format ("Unhandled exception caught: {0}", args.Exception.Message),
                                 "GARbro.GUI.App");
                Trace.WriteLine (args.Exception.StackTrace, "Stack trace");
            };
            UpgradeSettings();
            try
            {
                if (0 != e.Args.Length)
                {
                    InitPath = Path.GetFullPath (e.Args[0]);
                }
                else if (!string.IsNullOrEmpty (Settings.Default.appLastDirectory))
                {
                    string last_dir = Settings.Default.appLastDirectory;
                    Directory.SetCurrentDirectory (last_dir);
                    InitPath = last_dir;
                }
            }
            catch { }

            if (string.IsNullOrEmpty (InitPath))
                InitPath = Directory.GetCurrentDirectory();

            DeserializeScheme (Path.Combine (FormatCatalog.Instance.DataDirectory, FormatsDat));
            DeserializeScheme (Path.Combine (GetLocalAppDataFolder(), FormatsDat));
        }

        public string GetLocalAppDataFolder ()
        {
            string local_app_data = Environment.GetFolderPath (Environment.SpecialFolder.LocalApplicationData);
            var attribs = Assembly.GetExecutingAssembly().GetCustomAttributes (typeof(AssemblyCompanyAttribute), false);
            string company = attribs.Length > 0 ? ((AssemblyCompanyAttribute)attribs[0]).Company : "";
            return Path.Combine (local_app_data, company, Name);
        }

        public void DeserializeScheme (string scheme_file)
        {
            try
            {
                if (!File.Exists (scheme_file))
                    return;
                using (var file = File.OpenRead (scheme_file))
                    FormatCatalog.Instance.DeserializeScheme (file);
            }
            catch (Exception X)
            {
                Trace.WriteLine (string.Format ("Scheme deserialization failed: {0}", X.Message), "[GARbro.GUI.App]");
            }
        }

        void ApplicationExit (object sender, ExitEventArgs e)
        {
            Settings.Default.Save();
        }

        void UpgradeSettings ()
        {
            if (Settings.Default.UpgradeRequired)
            {
                try
                {
                    Settings.Default.Upgrade();
                    Settings.Default.UpgradeRequired = false;
                    Settings.Default.Save();
                }
                catch (System.Exception X)
                {
                    Trace.WriteLine (string.Format ("Settings upgrade failed: {0}", X.Message), "[GARbro.GUI.App]");
                }
             }
 
            // do not restore in minimized state
            if (Settings.Default.winState == System.Windows.WindowState.Minimized)
                Settings.Default.winState = System.Windows.WindowState.Normal;
        }

        public static bool NavigateUri (Uri uri)
        {
            try
            {
                if (uri.IsAbsoluteUri)
                {
                    Process.Start (new ProcessStartInfo (uri.AbsoluteUri));
                    return true;
                }
                else
                    throw new ApplicationException ("URI is not absolute");
            }
            catch (Exception X)
            {
                Trace.WriteLine ("Link navigation failed: "+X.Message, uri.ToString());
            }
            return false;
        }
    }
}
