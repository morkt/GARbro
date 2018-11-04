//! \file       AudioRPGMV.cs
//! \date       2018 Oct 14
//! \brief      RPG Maker encrypted OGG audio.
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

namespace GameRes.Formats.RPGMaker
{
    [Export(typeof(AudioFormat))]
    public sealed class RpgmvoAudio : AudioFormat
    {
        public override string         Tag { get { return "RPGMVO"; } }
        public override string Description { get { return "RPG Maker audio format (Ogg/Vorbis)"; } }
        public override uint     Signature { get { return 0x4D475052; } } // 'RPGMV'
        public override bool      CanWrite { get { return false; } }

        public override SoundInput TryOpen (IBinaryStream file)
        {
            var header = file.ReadHeader (0x14);
            if (header[4] != 'V')
                return null;
            var key = RpgmvDecryptor.LastKey ?? RpgmvDecryptor.FindKeyFor (file.Name);
            if (null == key)
                return null;
            for (int i = 0; i < 4; ++i)
                header[0x10+i] ^= key[i];
            if (!header.AsciiEqual (0x10, "OggS"))
            {
                RpgmvDecryptor.LastKey = null;
                return null;
            }
            RpgmvDecryptor.LastKey = key;
            var ogg = RpgmvDecryptor.DecryptStream (file, key);
            return OggAudio.Instance.TryOpen (ogg);
        }
    }
}
