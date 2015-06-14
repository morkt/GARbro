//! \file       AudioWA1.cs
//! \date       Thu Apr 16 11:49:16 2015
//! \brief      FFA System compressed WAV format.
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

using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Text;
using GameRes.Utility;

namespace GameRes.Formats.Ffa
{
    [Export(typeof(AudioFormat))]
    public class Wa1Audio : AudioFormat
    {
        public override string         Tag { get { return "WA1"; } }
        public override string Description { get { return "FFA System wave audio format"; } }
        public override uint     Signature { get { return 0; } }

        private static int ReadInt32 (Stream file)
        {
            int dword = file.ReadByte();
            dword |= file.ReadByte() << 8;
            dword |= file.ReadByte() << 16;
            dword |= file.ReadByte() << 24;
            return dword;
        }

        public override SoundInput TryOpen (Stream file)
        {
            int packed = ReadInt32 (file);
            if (packed < 0)
                return null;
            byte[] input;
            if (packed > 9)
            {
                if ((packed + 8) != file.Length)
                    return null;
                int unpacked = ReadInt32 (file);
                if (unpacked <= 0)
                    return null;
                using (var reader = new LzssReader (file, packed, unpacked))
                {
                    reader.Unpack();
                    if (Binary.AsciiEqual (reader.Data, 0, "RIFF"))
                    {
                        var sound = new WaveInput (new MemoryStream (reader.Data));
                        file.Dispose();
                        return sound;
                    }
                    input = reader.Data;
                }
            }
            else
            {
                if (0x46464952 != ReadInt32 (file)) // 'RIFF'
                    return null;
                file.Position = 0;
                input = new byte[file.Length];
                file.Read (input, 0, input.Length);
            }
            var wa1 = new Wa1Reader (input);
            wa1.Unpack();
            var wav = new WaveInput (new MemoryStream (wa1.Data));
            file.Dispose();
            return wav;
        }
    }

    internal class Wa1Reader
    {
        byte[]  m_input;
        byte[]  m_output;
        int     m_type;
        int     m_data_size;

        public int    Type { get { return m_type; } }
        public byte[] Data { get { return m_output; } }

        public Wa1Reader (byte[] input)
        {
            m_input = input;
            m_type = LittleEndian.ToInt32 (m_input, 0);
            if (0 != m_type && 4 != m_type && 8 != m_type)
                throw new InvalidFormatException();
            if (!Binary.AsciiEqual (m_input, 4, "RIFF") || !Binary.AsciiEqual (m_input, 0x28, "data"))
                throw new InvalidFormatException();
            m_data_size = LittleEndian.ToInt32 (m_input, 0x2c);
            m_output = new byte[m_data_size+0x2c];
            Buffer.BlockCopy (m_input, 4, m_output, 0, 0x2c);
            LittleEndian.Pack (m_output.Length-8, m_output, 4);
        }

        static ushort[] word_456CA0 = new ushort[] {
            0x39, 0x39, 0x39, 0x39, 0x4D, 0x66, 0x80, 0x99,
            0x39, 0x39, 0x39, 0x39, 0x4D, 0x66, 0x80, 0x99,
        };

        public byte[] Unpack ()
        {
            switch (Type)
            {
            case 0: UnpackV0(); break;
            case 4: UnpackV4(); break;
            case 8: UnpackV8(); break;
            }
            return m_output;
        }

        void UnpackV4 ()
        {
            int src = 0x30;
            int dst = 0x2c;
            uint v72 = 127;
            int v73 = 0;
            int v74 = 0;
            int a5 = 0;
            for (int v71 = m_data_size >> 1; v71 != 0; --v71)
            {
                int v170 = v73;
                int v75 = a5;
                int v76 = (ushort)a5;
                int v77 = v75 >> 16;
                int v81;
                int v78;
                int v79;
                int v80;
                if ((byte)v77 < 8u)
                {
                    v78 = m_input[src++];
                    v79 = v78 << v77;
                    v77 = (v77 + 8) & 0xff;
                    v76 |= v79;
                }
                if ((v76 & 3) == 2)
                {
                    v80 = v76 >> 2;
                    v77 = (v77 - 2) & 0xff;
                    v81 = 0;
                }
                else if (0 != (v76 & 3))
                {
                    if ((v76 & 7) == 5)
                    {
                        v80 = v76 >> 3;
                        v77 = (v77 - 3) & 0xff;
                        v81 = 1;
                    }
                    else if ( (v76 & 7) == 1 )
                    {
                        v80 = v76 >> 3;
                        v77 = (v77 - 3) & 0xff;
                        v81 = 9;
                    }
                    else if ( (v76 & 0xF) == 11 )
                    {
                        v80 = v76 >> 4;
                        v77 = (v77 - 4) & 0xff;
                        v81 = 2;
                    }
                    else if ((v76 & 0xF) == 3)
                    {
                        v80 = v76 >> 4;
                        v77 = (v77 - 4) & 0xff;
                        v81 = 10;
                    }
                    else if ((v76 & 0x1F) == 23)
                    {
                        v80 = v76 >> 5;
                        v77 = (v77 - 5) & 0xff;
                        v81 = 3;
                    }
                    else if ((v76 & 0x1F) == 7)
                    {
                        v80 = v76 >> 5;
                        v77 = (v77 - 5) & 0xff;
                        v81 = 11;
                    }
                    else if ((v76 & 0x3F) == 47)
                    {
                        v80 = v76 >> 6;
                        v77 = (v77 - 6) & 0xff;
                        v81 = 4;
                    }
                    else if ((v76 & 0x3F) == 15)
                    {
                        v80 = v76 >> 6;
                        v77 = (v77 - 6) & 0xff;
                        v81 = 12;
                    }
                    else if ((v76 & 0x7F) == 95)
                    {
                        v80 = v76 >> 7;
                        v77 = (v77 - 7) & 0xff;
                        v81 = 5;
                    }
                    else if ((v76 & 0x7F) == 31)
                    {
                        v80 = v76 >> 7;
                        v77 = (v77 - 7) & 0xff;
                        v81 = 13;
                    }
                    else
                    {
                        switch (v76 & 0xff)
                        {
                        case 0x7F:  v81 = 6;  break;
                        case 0xFF:  v81 = 14; break;
                        case 0xBF:  v81 = 7;  break;
                        default:    v81 = 15; break;
                        }
                        v80 = v76 >> 8;
                        v77 = (v77 - 8) & 0xff;
                    }
                }
                else
                {
                    v80 = v76 >> 2;
                    v77 = (v77 - 2) & 0xff;
                    v81 = 8;
                }
                int v82 = (v77 << 16) | v80;
                v73 = v170;
                int v171 = v82;
                v82 = v81;
                int v83 = v81;
                v82 = (2 * (v81 & 7) + 1) & 0xff;

                uint v84 = (v72 * (uint)(ushort)v82) >> 3;
                uint v85 = v84 & 0xffff;

                if (0 != (v83 & 8))
                {
                    int dword = v74 << 16 | (v73 & 0xffff);
                    dword -= (int)v85;
                    v73 = dword & 0xffff;
                    v74 = dword >> 16;
                    if (v74 < 0 && v73 < 0x8000u)
                    {
                        v73 = -32768;
                        v74 = -1;
                    }
                }
                else
                {
                    int dword = v74 << 16 | (v73 & 0xffff);
                    dword += (int)v85;
                    v73 = dword & 0xffff;
                    v74 = dword >> 16;
                    if (v74 >= 0 && v73 >= 0x8000u)
                    {
                        v73 = 32767;
                        v74 = 0;
                    }
                }
                uint v88 = (uint)word_456CA0[v81] * v72;
                v72 = (v88 >> 6) & 0xffff;
                if (v72 < 0x7fu)
                    v72 = 0x7f;
                else if (v72 > 0x6000u)
                    v72 = 0x6000;
                LittleEndian.Pack ((ushort)v73, m_output, dst);
                dst += 2;
                a5 = v171;
            }
        }

        void UnpackV0 ()
        {
            int src = 0x30;
            int dst = 0x2c;
            uint v12 = 127;
            int v13 = 0;
            int v14 = 0;
            int a5 = 0;
            for (int v11 = m_data_size >> 1; v11 != 0; --v11)
            {
                if ((a5 >> 8) == 1)
                {
                    a5 = m_input[src++];
                }
                else
                {
                    a5 = 0x100 | (m_input[src] >> 4);
                }
                int v15 = a5 & 0xF;
                int v160 = v15;
                int v16 = v15;

                uint v17 = v12 * (uint)(byte)(2 * (v15 & 7) + 1) >> 3;
                uint v18 = v17 & 0xffff;

                if (0 != (v16 & 8))
                {
                    int dword = v14 << 16 | (v13 & 0xffff);
                    dword -= (int)v18;
                    v13 = dword & 0xffff;
                    v14 = dword >> 16;
                    if ( v14 < 0 && v13 < 0x8000u )
                    {
                        v13 = -32768;
                        v14 = -1;
                    }
                }
                else
                {
                    int dword = v14 << 16 | (v13 & 0xffff);
                    dword += (int)v18;
                    v13 = dword & 0xffff;
                    v14 = dword >> 16;
                    if ( v14 >= 0 && v13 >= 0x8000u )
                    {
                        v13 = 32767;
                        v14 = 0;
                    }
                }
                uint v21 = (uint)word_456CA0[v160] * v12;
                v12 = (v21 >> 6) & 0xffff;
                if (v12 < 0x7Fu)
                    v12 = 127;
                else if (v12 > 0x6000u)
                    v12 = 0x6000;
                LittleEndian.Pack ((ushort)v13, m_output, dst);
                dst += 2;
            }
        }

        void UnpackV8 ()
        {
            int src = 0x30;
            int dst = 0x2c;
            int v95 = m_data_size >> 2;
            for (int i = 0; i < 2; ++i)
            {
                int a5 = i;
                uint v96 = 127;
                int v97 = 0;
                int v98 = 0;
                int v172 = dst;
                for (int j = 0; j < v95; ++j)
                {
                    if (1 == (a5 >> 8))
                    {
                        a5 = m_input[src++];
                    }
                    else
                    {
                        a5 = 0x100 | (m_input[src] >> 4);
                    }
                    int v99 = a5 & 0xF;
                    int v150 = v99;
                    int v100 = v99;

                    uint v101 = v96 * (uint)(byte)(2 * (v99 & 7) + 1) >> 3;
                    uint v102 = v101 & 0xffff;
                    if (0 != (v100 & 8))
                    {
                        int dword = v98 << 16 | (v97 & 0xffff);
                        dword -= (int)v102;
                        v97 = dword & 0xffff;
                        v98 = dword >> 16;
                        if ( v98 < 0 && v97 < 0x8000u )
                        {
                            v97 = -32768;
                            v98 = -1;
                        }
                    }
                    else
                    {
                        int dword = v98 << 16 | (v97 & 0xffff);
                        dword += (int)v102;
                        v97 = dword & 0xffff;
                        v98 = dword >> 16;
                        if ( v98 >= 0 && v97 >= 0x8000u )
                        {
                            v97 = 32767;
                            v98 = 0;
                        }
                    }
                    uint v105 = (uint)word_456CA0[v150] * v96;
                    v96 = (v105 >> 6) & 0xffff;

                    if (v96 < 0x7Fu)
                        v96 = 127;
                    else if (v96 > 0x6000u)
                        v96 = 0x6000;
                    LittleEndian.Pack ((ushort)v97, m_output, dst);
                    dst += 4;
                }
                dst = v172 + 2;
            }
        }
    }
}
