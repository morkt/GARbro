//! \file       AudioAF2.cs
//! \date       2018 Mar 26
//! \brief      CsWare audio format.
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

using System;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Utility;

namespace GameRes.Formats.CsWare
{
    [Export(typeof(AudioFormat))]
    public sealed class Af2Audio : AudioFormat
    {
        public override string         Tag { get { return "AF2"; } }
        public override string Description { get { return "CsWare audio format"; } }
        public override uint     Signature { get { return 0x32714661; } } // 'aFq2'
        public override bool      CanWrite { get { return false; } }

        public Af2Audio ()
        {
            Extensions = new string[] { "af2", "pmd" };
        }

        public override SoundInput TryOpen (IBinaryStream file)
        {
            var header = file.ReadHeader (0x20);
            var format = new WaveFormat {
                FormatTag = 1,
                SamplesPerSecond = BigEndian.ToUInt32 (header, 4),
                Channels = BigEndian.ToUInt16 (header, 8),
                BitsPerSample = BigEndian.ToUInt16 (header, 10),
            };
            format.BlockAlign = (ushort)(format.SamplesPerSecond * format.BitsPerSample / 8);
            format.SetBPS();
        }
    }
}
