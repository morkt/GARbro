//! \file       inflate.cs
//! \date       Tue Jul 08 14:20:38 2014
//! \brief      inflate zlib stream.
//

using System;
using System.IO;
using System.IO.Compression;

class Inflate
{
    public static void Main (string[] args)
    {
        if (args.Length != 2)
            return;
        try
        {
            var input = File.Open (args[0], FileMode.Open, FileAccess.Read);
            input.Position = 2;
            using (var stream = new DeflateStream (input, CompressionMode.Decompress))
            using (var output = File.Create (args[1]))
                stream.CopyTo (output);
            Console.WriteLine ("{0} => {1}", args[0], args[1]);
        }
        catch (Exception X)
        {
            Console.Error.WriteLine (X.Message);
        }
    }
}
