using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using GameRes;

namespace GARbro
{
    public class ImageConverter
    {
        static string ProgramName
        {
            get
            {
                return Path.GetFileNameWithoutExtension(System.Reflection.Assembly.GetExecutingAssembly().Location);
            }
        }

        void Usage ()
        {
            Console.WriteLine("Usage: {0} [OPTIONS] IMAGE", ProgramName);
            Console.WriteLine("    -l          list recognized image FormatCatalog.Instance");
            Console.WriteLine("    -t FORMAT   convert image(s) to specified format");
            Console.WriteLine("Without options image metadata is displayed.");
        }

        void ListFormats ()
        {
            Console.WriteLine("Supported image formats:");
            foreach (var impl in FormatCatalog.Instance.ImageFormats)
            {
                Console.Write ("{0,-4} ", impl.Tag);
                if (impl.IsBuiltin) Console.Write ("[builtin] ");
                Console.WriteLine (impl.Description);
            }
        }

        public static ImageFormat FindFormat (string format)
        {
            var range = FormatCatalog.Instance.LookupExtension<ImageFormat> (format);
            return range.FirstOrDefault();
        }

        void PrintMetaData (string filename)
        {
            using (var file = BinaryStream.FromFile (filename))
            {
                var format = ImageFormat.FindFormat (file);
                if (null == format)
                {
                    Console.Error.WriteLine ("{0}: file format not recognized", filename);
                    return;
                }
                var image = format.Item2;
                Console.WriteLine ("{0,16} [{4}] {1}x{2} {3}bpp", filename, image.Width, image.Height, image.BPP, format.Item1.Tag);
            }
        }

        void ConvertFile (string filename, ImageFormat format)
        {
            ImageData image;
            using (var file = BinaryStream.FromFile (filename))
            {
                image = ImageFormat.Read (file);
                if (null == image)
                {
                    Console.Error.WriteLine ("{0}: Unknown image format", filename);
                    return;
                }
            }
            string target_ext = format.Extensions.First();
            string outname = Path.GetFileNameWithoutExtension (filename)+'.'+target_ext;
            Console.WriteLine ("{0} => {1}", filename, outname);
            using (var outfile = new FileStream (outname, FileMode.Create, FileAccess.Write))
            {
                format.Write (outfile, image);
            }
        }

        void Run (string[] args)
        {
            if (args.Length < 1 || args[0] == "/?" || args[0] == "--help")
            {
                Usage();
            }
            else if (args[0] == "-l")
            {
                ListFormats();
            }
            else if (args[0] == "-t")
            {
                if (args.Length < 3)
                {
                    Usage();
                    return;
                }
                ImageFormat format = FindFormat (args[1]);
                if (null == format)
                {
                    Console.Error.WriteLine ("{0}: unknown format specified", args[1]);
                    return;
                }
                for (int i = 2; i < args.Length; ++i)
                {
                    try
                    {
                        ConvertFile (args[i], format);
                    }
                    catch (Exception X)
                    {
                        Console.Error.WriteLine ("{0}: {1}", args[i], X.Message);
                    }
                }
            }
            else
            {
                foreach (var filename in args)
                {
                    PrintMetaData (filename);
                }
            }
        }

        static void Main(string[] args)
        {
            try
            {
                var program = new ImageConverter();
                program.Run (args);
            }
            catch (Exception X)
            {
                Console.Error.WriteLine ("{0}: {1}", ProgramName, X.Message);
            }
        }
    }
}
