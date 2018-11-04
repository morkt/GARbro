//! \file       ImageRPGMV.cs
//! \date       2018 Oct 14
//! \brief      RPG Maker encrypted PNG image.
//
// Copyright (C) 2018 by morkt
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

using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace GameRes.Formats.RPGMaker
{
    internal class RpgmvpMetaData : ImageMetaData
    {
        public byte[]   Key;
    }

    [Export(typeof(ImageFormat))]
    public class RpgmvpFormat : ImageFormat
    {
        public override string         Tag { get { return "RPGMVP"; } }
        public override string Description { get { return "RPG Maker engine image format"; } }
        public override uint     Signature { get { return 0x4D475052; } } // 'RPGMV'

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x14);
            if (header[4] != 'V')
                return null;
            var key = RpgmvDecryptor.LastKey ?? RpgmvDecryptor.FindKeyFor (file.Name);
            if (null == key)
                return null;
            for (int i = 0; i < 4; ++i)
                header[0x10+i] ^= key[i];
            if (!header.AsciiEqual (0x10, "\x89PNG"))
            {
                RpgmvDecryptor.LastKey = null;
                return null;
            }
            RpgmvDecryptor.LastKey = key;
            using (var png = RpgmvDecryptor.DecryptStream (file, key, true))
            {
                var info = Png.ReadMetaData (png);
                if (null == info)
                    return null;
                return new RpgmvpMetaData {
                    Width = info.Width,
                    Height = info.Height,
                    OffsetX = info.OffsetX,
                    OffsetY = info.OffsetY,
                    BPP = info.BPP,
                    Key = key,
                };
            }
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (RpgmvpMetaData)info;
            using (var png = RpgmvDecryptor.DecryptStream (file, meta.Key, true))
                return Png.Read (png, info);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("RpgmvpFormat.Write not implemented");
        }
    }

    internal class RpgmvDecryptor
    {
        public static IBinaryStream DecryptStream (IBinaryStream input, byte[] key, bool leave_open = false)
        {
            input.Position = 0x10;
            var header = input.ReadBytes (key.Length);
            for (int i = 0; i < key.Length; ++i)
                header[i] ^= key[i];
            var result = new PrefixStream (header, new StreamRegion (input.AsStream, input.Position, leave_open));
            return new BinaryStream (result, input.Name);
        }

        static byte[] GetKeyFromString (string hex)
        {
            if ((hex.Length & 1) != 0)
                throw new System.ArgumentException ("invalid key string");
            var key = new byte[hex.Length/2];
            for (int i = 0; i < key.Length; ++i)
            {
                key[i] = (byte)(HexToInt (hex[i * 2]) << 4 | HexToInt (hex[i * 2 + 1]));
            }
            return key;
        }

        static int HexToInt (char x)
        {
            if (char.IsDigit (x))
                return x - '0';
            else
                return char.ToUpper (x) - 'A' + 10;
        }

        static byte[] ParseSystemJson (string filename)
        {
            var json = File.ReadAllText (filename, Encoding.UTF8);
            var serializer = new JavaScriptSerializer();
            var sys = serializer.DeserializeObject (json) as IDictionary;
            if (null == sys)
                return null;
            var key = sys["encryptionKey"] as string;
            if (null == key)
                return null;
            return GetKeyFromString (key);
        }

        public static byte[] FindKeyFor (string filename)
        {
            foreach (var system_filename in FindSystemJson (filename))
            {
                if (File.Exists (system_filename))
                    return ParseSystemJson (system_filename);
            }
            return null;
        }

        static IEnumerable<string> FindSystemJson (string filename)
        {
            var dir_name = Path.GetDirectoryName (filename);
            yield return Path.Combine (dir_name, @"..\..\data\System.json");
            yield return Path.Combine (dir_name, @"..\..\..\www\data\System.json");
            yield return Path.Combine (dir_name, @"..\data\System.json");
            yield return Path.Combine (dir_name, @"data\System.json");
        }

        internal static readonly byte[] DefaultKey = {
            0x77, 0x4E, 0x46, 0x45, 0xFC, 0x43, 0x2F, 0x71, 0x47, 0x95, 0xA2, 0x43, 0xE5, 0x10, 0x13, 0xD8
        };

        internal static byte[] LastKey = null;
    }
}
