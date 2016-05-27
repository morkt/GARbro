//! \file       Program.cs
//! \date       Mon Jun 30 20:12:13 2014
//! \brief      game resources browser.
//

using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using GameRes;

namespace GARbro
{
    class ConsoleBrowser
    {
        private string m_arc_name;

        void ListFormats ()
        {
            Console.WriteLine ("Recognized resource formats:");
            foreach (var impl in FormatCatalog.Instance.ArcFormats)
            {
                Console.WriteLine ("{0,-4} {1}", impl.Tag, impl.Description);
            }
        }

        void ExtractAll (ArcFile arc)
        {
            arc.ExtractFiles ((i, entry, msg) => {
                if (null != entry)
                {
                    Console.WriteLine ("Extracting {0} ...", entry.Name);
                }
                else if (null != msg)
                {
                    Console.WriteLine (msg);
                }
                return ArchiveOperation.Continue;
            });
        }

        void ExtractFile (ArcFile arc, string name)
        {
            Entry entry = arc.Dir.FirstOrDefault (e => e.Name.Equals (name, StringComparison.OrdinalIgnoreCase));
            if (null == entry)
            {
                Console.Error.WriteLine ("'{0}' not found within {1}", name, m_arc_name);
                return;
            }
            Console.WriteLine ("Extracting {0} ...", entry.Name);
            arc.Extract (entry);
        }
        void TestArc (string[] args)
        {
/*
            if (args.Length > 1)
            {
                uint pass = GameRes.Formats.IntOpener.EncodePassPhrase (args[1]);
                Console.WriteLine ("{0:X8}", pass);
            }
*/
        }
        void Run (string[] args)
        {
            int argn = 0;
            if (args[argn].Equals ("-l"))
            {
                ListFormats();
                return;
            }
            if (args[argn].Equals ("-t"))
            {
                TestArc (args);
                return;
            }
            if (args[argn].Equals ("-x"))
            {
                ++argn;
                if (args.Length < 2)
                {
                    Usage();
                    return;
                }
            }
            m_arc_name = args[argn];
            var arc = ArcFile.TryOpen (m_arc_name);
            if (null == arc)
            {
                Console.Error.WriteLine ("{0}: unknown format", m_arc_name);
                return;
            }
            using (arc)
            {
                if (args.Length > argn+1)
                {
                    for (int i = argn+1; i < args.Length; ++i)
                        ExtractFile (arc, args[i]);
                }
                else if (args[0].Equals ("-x"))
                {
                    ExtractAll (arc);
                }
                else
                {
                    foreach (var entry in arc.Dir.OrderBy (e => e.Offset))
                    {
                        Console.WriteLine ("{0,9} [{2:X8}] {1}", entry.Size, entry.Name, entry.Offset);
                    }
                }
            }
        }

        static void Usage ()
        {
            Console.WriteLine ("Usage: gameres [OPTIONS] ARC [ENTRIES]");
            Console.WriteLine ("    -l   list recognized archive formats");
            Console.WriteLine ("    -x   extract all files");
            Console.WriteLine ("Without options displays contents of specified archive.");
        }

        static void Main (string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            if (0 == args.Length)
            {
                Usage();
                return;
            }
            var listener = new TextWriterTraceListener (Console.Error);
            Trace.Listeners.Add(listener);
            try
            {
                var browser = new ConsoleBrowser();
                browser.Run (args);
            }
            catch (Exception X)
            {
                Console.Error.WriteLine (X.Message);
            }
        }
    }
}
