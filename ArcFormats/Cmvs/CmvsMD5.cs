//! \file       CmvsMD5.cs
//! \date       Mon Nov 30 13:29:27 2015
//! \brief      Cmvs engine MD5 update algorithm.
//
// Copyright (C) 2015-2016 by morkt
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

using GameRes.Utility;

namespace GameRes.Formats.Cmvs
{
    public enum Md5Variant { A, B, Chrono, Memoria, Natsu, Aoi, Mirai }

    public abstract class MD5 : Cryptography.MD5Base
    {
        public MD5 ()
        {
            InitState();
        }

        static public MD5 Create (Md5Variant variant)
        {
            switch (variant)
            {
            case Md5Variant.A: return new Md5VariantA();
            case Md5Variant.B: return new Md5VariantB();
            case Md5Variant.Chrono: return new Md5Chrono();
            case Md5Variant.Memoria: return new Md5Memoria();
            case Md5Variant.Natsu: return new Md5Natsu();
            case Md5Variant.Aoi: return new Md5Aoi();
            case Md5Variant.Mirai: return new Md5Mirai();
            default: throw new System.ArgumentException ("Unknown MD5 variant", "variant");
            }
        }

        protected abstract void InitState ();
        protected abstract void SetResult (uint[] data);

        public void Compute (uint[] data)
        {
            m_buffer[0] = data[0];
            m_buffer[1] = data[1];
            m_buffer[2] = data[2];
            m_buffer[3] = data[3];
            m_buffer[4] = 0x80;
            m_buffer[14] = 0x80;
            Transform();
            SetResult (data);
        }
    }

    public class Md5VariantA : MD5
    {
        protected override void InitState ()
        {
            m_state[0] = 0xC74A2B01;
            m_state[1] = 0xE7C8AB8F;
            m_state[2] = 0xD8BEDC4E;
            m_state[3] = 0x7302A4C5;
        }

        protected override void SetResult (uint[] data)
        {
            data[0] = m_state[3];
            data[1] = m_state[1];
            data[2] = m_state[2];
            data[3] = m_state[0];
        }
    }

    public class Md5Chrono : Md5VariantA
    {
        protected override void SetResult (uint[] data)
        {
            data[0] = m_state[2] ^ 0x45A76C2F;
            data[1] = m_state[1] - 0x5BA17FCB;
            data[2] = m_state[0] ^ 0x79ABE8AD;
            data[3] = m_state[3] - 0x1C08561B;
        }
    }

    public class Md5VariantB : MD5
    {
        protected override void InitState ()
        {
            m_state[0] = 0x53FE9B2C;
            m_state[1] = 0xF2C93EA8;
            m_state[2] = 0xEE81BA59;
            m_state[3] = 0xA2C8973E;
        }

        protected override void SetResult (uint[] data)
        {
            data[0] = m_state[1] ^ 0x49875325;
            data[1] = m_state[2] + 0x54F46D7D;
            data[2] = m_state[3] ^ 0xAD7948B7;
            data[3] = m_state[0] + 0x1D0638AD;
        }
    }

    public class Md5Memoria : MD5
    {
        protected override void InitState ()
        {
            m_state[0] = 0xA79463F9;
            m_state[1] = 0xB6E755C5;
            m_state[2] = 0xC696AF21;
            m_state[3] = 0x6983E978;
        }

        protected override void SetResult (uint[] data)
        {
            data[0] = m_state[1];
            data[1] = m_state[2];
            data[2] = m_state[3];
            data[3] = m_state[0];
        }
    }

    public class Md5Natsu : MD5
    {
        protected override void InitState ()
        {
            m_state[0] = 0x63FE9A7C;
            m_state[1] = 0xC2B93E98;
            m_state[2] = 0xEF91BA5C;
            m_state[3] = 0x72C9A82E;
        }

        protected override void SetResult (uint[] data)
        {
            data[0] = m_state[1] + 0x45876329;
            data[1] = m_state[2] ^ 0x54F36D6C;
            data[2] = m_state[3] + 0x4387A749;
            data[3] = m_state[0] ^ 0xE3F9A742;
        }
    }

    public class Md5Mirai : MD5
    {
        protected override void InitState ()
        {
            m_state[0] = 0x67452301;
            m_state[1] = 0xEFCDAB89;
            m_state[2] = 0x98BADCFE;
            m_state[3] = 0x10325476;
        }

        protected override void SetResult (uint[] data)
        {
            data[0] = m_state[0];
            data[1] = m_state[1];
            data[2] = m_state[2];
            data[3] = m_state[3];
        }
    }

    public class Md5Aoi : MD5
    {
        protected override void InitState ()
        {
            m_state[0] = 0xC74A2B02;
            m_state[1] = 0xE7C8AB8F;
            m_state[2] = 0x38BEBC4E;
            m_state[3] = 0x7531A4C3;
        }

        protected override void SetResult (uint[] data)
        {
            data[0] = m_state[2] ^ 0x53A76D2E;
            data[1] = m_state[1] + 0x5BB17FDA;
            data[2] = m_state[0] + 0x6853E14D;
            data[3] = m_state[3] ^ 0xF5C6A9A3;
        }
    }
}
