//! \file       AudioPWV.cs
//! \date       2019 Jan 10
//! \brief      Mermaid compressed WAVE file.
//
// Copyright (C) 2019 by morkt
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

namespace GameRes.Formats.Mermaid
{
    [Export(typeof(AudioFormat))]
    public class PwvAudio : AudioFormat
    {
        public override string         Tag { get { return "PWV"; } }
        public override string Description { get { return "Mermaid compressed audio"; } }
        public override uint     Signature { get { return 0x46495200; } } // '\0RIF'
        public override bool      CanWrite { get { return false; } }

        public override SoundInput TryOpen (IBinaryStream file)
        {
            if (!file.Name.HasExtension ("PWV"))
                return null;
            var header = file.ReadHeader (0x10);
            if (!header.AsciiEqual (9, "WAVE"))
                return null;
            var output = new MemoryStream ((int)file.Length);
            try
            {
                file.Position = 0;
                Unpack (file, output);
                output.Position = 0;
                var wave = new WaveInput (output);
                file.Dispose();
                return wave;
            }
            catch
            {
                output.Dispose();
                throw;
            }
        }

        void Unpack (IBinaryStream input, Stream output)
        {
            var buffer = new byte[16];
            int ctl;
            while ((ctl = input.ReadByte()) != -1)
            {
                if (0 == ctl)
                {
                    input.Read (buffer, 0, 16);
                    output.Write (buffer, 0, 16);
                }
                else if (1 == ctl)
                {
                    for (int i = 0; i < 16; i += 2)
                    {
                        buffer[i] = input.ReadUInt8();
                        buffer[i+1] = 0;
                    }
                    output.Write (buffer, 0, 16);
                }
                else if (8 == ctl)
                {
                    for (int i = 0; i < 16; i += 2)
                    {
                        buffer[i] = input.ReadUInt8();
                        buffer[i+1] = 0xFF;
                    }
                    output.Write (buffer, 0, 16);
                }
                else if (15 == ctl)
                {
                    int count = input.ReadUInt8();
                    if (count > buffer.Length)
                        buffer = new byte[count];
                    input.Read (buffer, 0, count);
                    output.Write (buffer, 0, count);
                }
                else if (input.PeekByte() != -1)
                    throw new InvalidFormatException();
            }
        }
    }
}
