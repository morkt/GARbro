// Game Resource Browser
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
using System.IO;
using System.Configuration;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Threading;
using System.Reflection;
using System.Diagnostics;
using GARbro.GUI.Strings;
using GARbro.GUI.Properties;
using GameRes;

namespace GARbro.GUI
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        const StringComparison StringIgnoreCase = StringComparison.OrdinalIgnoreCase;

        public static string Name { get { return "GARbro"; } }

        /// <summary>
        /// Initial browsing directory.
        /// </summary>
        public string InitPath { get; private set; }

        void ApplicationStartup (object sender, StartupEventArgs e)
        {
#if DEBUG
            string trace_dir = Path.GetDirectoryName (System.Reflection.Assembly.GetExecutingAssembly().Location);
            Trace.Listeners.Add (new TextWriterTraceListener (Path.Combine (trace_dir, "trace.log")));
            Trace.AutoFlush = true;
#endif
            Trace.WriteLine ("ApplicationStartup --------------------------------", "GARbro.GUI.App");
            this.DispatcherUnhandledException += (s, args) =>
            {
                Trace.WriteLine (string.Format ("Unhandled exception caught: {1}", args.Exception.Message),
                                 "GARbro.GUI.App");
                Trace.WriteLine (args.Exception.StackTrace, "Stack trace");
            };
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
        }

        void ApplicationExit (object sender, ExitEventArgs e)
        {
            Settings.Default.Save();
        }
    }
}
