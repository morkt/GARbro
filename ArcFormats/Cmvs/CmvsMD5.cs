//! \file       CmvsMD5.cs
//! \date       Mon Nov 30 13:29:27 2015
//! \brief      Cmvs engine MD5 update algorithm.
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

using GameRes.Utility;

namespace GameRes.Formats.Cmvs
{
    public enum Md5Variant { A, B, Chrono }

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
}
