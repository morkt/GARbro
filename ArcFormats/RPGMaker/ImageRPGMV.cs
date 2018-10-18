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

using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;

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

        internal static readonly byte[] DefaultKey = {
            0x77, 0x4E, 0x46, 0x45, 0xFC, 0x43, 0x2F, 0x71, 0x47, 0x95, 0xA2, 0x43, 0xE5, 0x10, 0x13, 0xD8
        };

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x14);
            if (header[4] != 'V')
                return null;
            var key = DefaultKey;
            for (int i = 0; i < 4; ++i)
                header[0x10+i] ^= key[i];
            if (!header.AsciiEqual (0x10, "\x89PNG"))
                return null;
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
            var result = new PrefixStream (header, new StreamRegion (input.AsStream, 0x20, leave_open));
            return new BinaryStream (result, input.Name);
        }
    }
}
