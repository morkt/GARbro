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

        public string InitPath { get; private set; }
        public string CurrentPath { get; private set; }
        public ArcFile CurrentArchive { get; private set; }

        void ApplicationStartup (object sender, StartupEventArgs e)
        {
            Trace.WriteLine ("ApplicationStartup --------------------------------", "GARbro.GUI.App");
            if (0 != e.Args.Length)
            {
                InitPath = Path.GetFullPath (e.Args[0]);
            }
            else if (!string.IsNullOrEmpty (Settings.Default.appLastDirectory))
            {
                try
                {
                    string last_dir = Settings.Default.appLastDirectory;
                    Directory.SetCurrentDirectory (last_dir);
                    InitPath = last_dir;
                }
                catch { }
            }

            if (string.IsNullOrEmpty (InitPath))
                InitPath = Directory.GetCurrentDirectory();

           CurrentPath = "";
        }

        void ApplicationExit (object sender, ExitEventArgs e)
        {
            Settings.Default.Save();
        }

        public ICollection<Entry> GetDirectoryList (string path)
        {
            var info = new DirectoryInfo (path);
            var list = new List<Entry>();
            foreach (var subdir in info.EnumerateDirectories())
            {
                if (0 != (subdir.Attributes & (FileAttributes.Hidden | FileAttributes.System)))
                    continue;
                list.Add (new SubDirEntry (subdir.Name));
            }
            foreach (var file in info.EnumerateFiles())
            {
                if (0 != (file.Attributes & (FileAttributes.Hidden | FileAttributes.System)))
                    continue;
                var entry = FormatCatalog.Instance.CreateEntry (file.Name);
                entry.Size = (uint)Math.Min (file.Length, uint.MaxValue);
                list.Add (entry);
            }
            return list;
        }

        public ArcFile GetArchive (string path)
        {
            if (path.Equals (CurrentPath, StringIgnoreCase))
                return CurrentArchive;
            FormatCatalog.Instance.LastError = null;
            var arc = ArcFile.TryOpen (path);
            if (null == arc)
            {
                if (null != FormatCatalog.Instance.LastError)
                    throw FormatCatalog.Instance.LastError;
                throw new UnknownFormatException();
            }
            if (null != CurrentArchive)
                CurrentArchive.Dispose();
            CurrentPath = path;
            CurrentArchive = arc;
            return CurrentArchive;
        }

        public void ResetCache ()
        {
            if (null != CurrentArchive)
                CurrentArchive.Dispose();
            CurrentArchive = null;
            CurrentPath = "";
        }

        // Update UI on demand.

        private static DispatcherOperationCallback exitFrameCallback =
            new DispatcherOperationCallback(ExitFrame);

        /// <summary>
        /// Processes all UI messages currently in the message queue.
        /// </summary>
        public static void DoEvents()
        {
            // Create new nested message pump.
            DispatcherFrame nestedFrame = new DispatcherFrame();

            // Dispatch a callback to the current message queue, when getting called,
            // this callback will end the nested message loop.
            // note that the priority of this callback should be lower than the that of UI event messages.
            DispatcherOperation exitOperation = Dispatcher.CurrentDispatcher.BeginInvoke(
                    DispatcherPriority.Background, exitFrameCallback, nestedFrame);

            // pump the nested message loop, the nested message loop will
            // immediately process the messages left inside the message queue.
            Dispatcher.PushFrame(nestedFrame);

            // If the "exitFrame" callback doesn't get finished, Abort it.
            if (exitOperation.Status != DispatcherOperationStatus.Completed)
                exitOperation.Abort();
        }

        static Object ExitFrame(Object state)
        {
            DispatcherFrame frame = state as DispatcherFrame;

            // Exit the nested message loop.
            frame.Continue = false;
            return null;
        }
    }

    public class UnknownFormatException : Exception
    {
        public UnknownFormatException () : base (guiStrings.MsgUnknownFormat) { }
        public UnknownFormatException (string path)
            : base (string.Format ("{1}: {0}", guiStrings.MsgUnknownFormat, path))
        { }
    }
}
