//! \file       AudioWAZ.cs
//! \date       2017 Dec 28
//! \brief      LZSS-compressed WAV file.
//
// Copyright (C) 2017 by morkt
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
using GameRes.Compression;

namespace GameRes.Formats.AnotherRoom
{
    [Export(typeof(AudioFormat))]
    public class WazAudio : AudioFormat
    {
        public override string         Tag { get { return "WAZ"; } }
        public override string Description { get { return "LZSS-compressed WAV audio"; } }
        public override uint     Signature { get { return 0x464952FF; } }
        public override bool      CanWrite { get { return false; } }

        public override SoundInput TryOpen (IBinaryStream file)
        {
            using (var lzss = new LzssStream (file.AsStream, LzssMode.Decompress, true))
            {
                var header = new byte[8];
                if (lzss.Read (header, 0, 8) != 8)
                    return null;
                int length = header.ToInt32 (4);
                var wav = new MemoryStream (length+8);
                wav.Write (header, 0, 8);
                lzss.CopyTo (wav);
                var bin = BinaryStream.FromStream (wav, file.Name);
                var sound = Wav.TryOpen (bin);
                if (sound != null)
                    file.Dispose();
                return sound;
            }
        }
    }
}
