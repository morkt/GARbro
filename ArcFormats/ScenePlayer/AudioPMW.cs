//! \file       AudioPMW.cs
//! \date       Thu Apr 30 21:22:48 2015
//! \brief      ScenePlayer compressed WAV file.
//
// Copyright (C) 2015 by morkt
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

namespace GameRes.Formats.ScenePlayer
{
    [Export(typeof(AudioFormat))]
    public class PmwAudio : WaveAudio
    {
        public override string         Tag { get { return "PMW"; } }
        public override string Description { get { return "ScenePlayer compressed WAV audio"; } }
        public override uint     Signature { get { return 0; } }
        
        public override SoundInput TryOpen (Stream file)
        {
            int first = file.ReadByte();
            if ((first ^ 0x21) != 0x78) // doesn't look like zlib stream
                return null;
            file.Position = 0;
            using (var input = new XoredStream (file, 0x21, true))
            using (var zstream = new ZLibStream (input, CompressionMode.Decompress))
            {
                SoundInput sound = null;
                var wav = new MemoryStream();
                try
                {
                    zstream.CopyTo (wav);
                    wav.Position = 0;
                    sound = new WaveInput (wav);
                }
                finally
                {
                    if (null == sound)
                        wav.Dispose();
                    else
                        file.Dispose();
                }
                return sound;
            }
        }

        public override void Write (SoundInput source, Stream output)
        {
            using (var wav = new XoredStream (output, 0x21, true))
            using (var zstream = new ZLibStream (wav, CompressionMode.Compress, CompressionLevel.Level9))
                base.Write (source, zstream);
        }
    }
}
