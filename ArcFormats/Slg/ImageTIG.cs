//! \file       ImageTIG.cs
//! \date       Sat Jun 04 18:00:15 2016
//! \brief      SLG system encrypted PNG images.
//
// Copyright (C) 2016 by morkt
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
using System.Security.Cryptography;

namespace GameRes.Formats.Slg
{
    [Export(typeof(ImageFormat))]
    public class TigFormat : PngFormat
    {
        public override string         Tag { get { return "TIG"; } }
        public override string Description { get { return "SLG system encrypted PNG image"; } }
        public override uint     Signature { get { return 0x7CF3C28B; } }
        public override bool      CanWrite { get { return false; } }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            using (var proxy = new ProxyStream (stream.AsStream, true))
            using (var crypt = new CryptoStream (proxy, new TigTransform(), CryptoStreamMode.Read))
            using (var input = new BinaryStream (crypt, stream.Name))
                return base.ReadMetaData (input);
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            using (var proxy = new ProxyStream (stream.AsStream, true))
            using (var crypt = new CryptoStream (proxy, new TigTransform(), CryptoStreamMode.Read))
            using (var input = new BinaryStream (crypt, stream.Name))
                return base.Read (input, info);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("TigFormat.Write not implemented");
        }
    }

    internal sealed class TigTransform : ICryptoTransform
    {
        const int BlockSize = 1;
        uint m_key;

        public bool          CanReuseTransform { get { return true; } }
        public bool CanTransformMultipleBlocks { get { return true; } }
        public int              InputBlockSize { get { return BlockSize; } }
        public int             OutputBlockSize { get { return BlockSize; } }

        public TigTransform ()
        {
            m_key = 0x7F7F7F7F;
        }

        public int TransformBlock (byte[] inputBuffer, int inputOffset, int inputCount,
                                   byte[] outputBuffer, int outputOffset)
        {
            for (int i = 0; i < inputCount; ++i)
            {
                m_key *= 0x343FD;
                m_key += 0x269EC3;
                outputBuffer[outputOffset+i] = (byte)(inputBuffer[inputOffset+i] - (m_key >> 16));
            }
            return inputCount;
        }

        public byte[] TransformFinalBlock (byte[] inputBuffer, int inputOffset, int inputCount)
        {
            byte[] outputBuffer = new byte[inputCount];
            TransformBlock (inputBuffer, inputOffset, inputCount, outputBuffer, 0);
            return outputBuffer;
        }

        public void Dispose ()
        {
            System.GC.SuppressFinalize (this);
        }
    }
}
