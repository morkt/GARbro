//! \file       deflate.cs
//! \date       Tue Jul 08 15:01:34 2014
//! \brief      deflate file into zlib stream.
//

using System;
using System.IO;
using ZLibNet;

class Inflate
{
    public static void Main (string[] args)
    {
        if (args.Length != 2)
            return;
        try
        {
            using (var input = File.Open (args[0], FileMode.Open, FileAccess.Read))
                using (var output = File.Create (args[1]))
                    using (var stream = new DeflateStream (output, CompressionMode.Compress, CompressionLevel.Level5))
                        input.CopyTo (stream);
            Console.WriteLine ("{0} => {1}", args[0], args[1]);
        }
        catch (Exception X)
        {
            Console.Error.WriteLine (X.Message);
        }
    }
}
