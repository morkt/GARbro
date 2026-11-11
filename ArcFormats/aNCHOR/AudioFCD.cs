//! \file       AudioFCD.cs
//! \date       2026-01-19
//! \brief      AGES Mk2 audio format.
//
// Copyright (C) 2026 by morkt
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
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using GameRes.Cryptography;
using GameRes.Utility;

namespace GameRes.Formats.Anchor
{
    [Export(typeof(AudioFormat))]
    public class FcdAudio : AudioFormat
    {
        public override string         Tag { get { return "FCD"; } }
        public override string Description { get { return "AGES Mk2 audio format"; } }
        public override uint     Signature { get { return 0x00444346; } } // 'FCD\x00'

        public FcdAudio ()
        {
            Extensions = new string[] { "fcd" };
        }

        public override SoundInput TryOpen (IBinaryStream file)
        {
            file.Position = 4;

            uint version = Binary.BigEndian (file.ReadUInt16());
            if (version != 2)
                return null;

            uint count = Binary.BigEndian (file.ReadUInt16());
            uint offset = Binary.BigEndian (file.ReadUInt32());
            Stream region = new StreamRegion (file.AsStream, offset);

            if (count == 0 && offset == 0x0C)
                return new OggInput (region);

            if (count == 1 && offset == 0x1C)
            {
                file.Position = 0x0C;
                uint type = Binary.BigEndian (file.ReadUInt32());
                if (type != 2)
                    return null;

                var md5 = new MD5();
                md5.Initialize();
                md5.Update (file.ReadBytes (0xC), 0, 0xC);
                md5.Update (DefaultScheme.Md5Addition, 0, 0x4000);
                md5.Final();

                var key = new byte[16];
                Buffer.BlockCopy (md5.State, 0, key, 0, 16);
                var encryption = new Mk2Blowfish (key, DefaultScheme.Context);
                var stream = new InputCryptoStream (region, encryption.CreateDecryptor());
                var mem = new MemoryStream();
                stream.CopyTo (mem);
                mem.Position = 0;
                return new OggInput (mem);
            }

            return null;
        }

        FcdScheme DefaultScheme = new FcdScheme();

        public override ResourceScheme Scheme
        {
            get { return DefaultScheme; }
            set { DefaultScheme = (FcdScheme)value; }
        }
    }

    [Serializable]
    public class FcdScheme : ResourceScheme
    {
        public byte[] Context;
        public byte[] Md5Addition;
    }
}
