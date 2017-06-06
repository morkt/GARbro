//! \file       LibScheme.cs
//! \date       Tue Jun 06 22:47:22 2017
//! \brief      Malie encryption schemes.
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

using System;
using System.Collections.Generic;
using GameRes.Cryptography;

namespace GameRes.Formats.Malie
{
    [Serializable]
    public abstract class LibScheme
    {
        uint     DataAlign;

        public LibScheme (uint align)
        {
            DataAlign = align;
        }

        public abstract IMalieDecryptor CreateDecryptor ();

        public virtual long GetAlignedOffset (long offset)
        {
            long align = DataAlign - 1;
            return (offset + align) & ~align;
        }
    }

    [Serializable]
    public class LibCamelliaScheme : LibScheme
    {
        public uint[] Key { get; set; }

        public LibCamelliaScheme (uint[] key) : this (0x1000, key)
        {
        }

        public LibCamelliaScheme (uint align, uint[] key) : base (align)
        {
            Key = key;
        }

        public LibCamelliaScheme (string key) : this (Camellia.GenerateKey (key))
        {
        }

        public LibCamelliaScheme (uint align, string key) : this (align, Camellia.GenerateKey (key))
        {
        }

        public override IMalieDecryptor CreateDecryptor ()
        {
            return new CamelliaDecryptor (Key);
        }
    }

    [Serializable]
    public class LibCfiScheme : LibScheme
    {
        public byte[] Key { get; set; }

        public LibCfiScheme (uint align, byte[] key) : base (align)
        {
            Key = key;
        }

        public override IMalieDecryptor CreateDecryptor ()
        {
            return new CfiDecryptor (Key);
        }
    }

    [Serializable]
    public class MalieScheme : ResourceScheme
    {
        public Dictionary<string, LibScheme> KnownSchemes;
    }
}
