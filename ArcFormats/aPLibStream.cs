//! \file       aPLibStream.cs
//! \date       2026-01-09

/*
 * aPLib compression library  -  the smaller the better :)
 *
 * C depacker
 *
 * Copyright (c) 1998-2014 Joergen Ibsen
 * All Rights Reserved
 *
 * http://www.ibsensoftware.com/
 */

// C# port by scientificworld

using System;
using System.Collections.Generic;
using System.IO;

namespace GameRes.Compression {
    public sealed class aPLibCoroutine : Decompressor {
        Stream  m_input;
        uint    m_tag;
        uint    m_bitcount;

        public override void Initialize (Stream input) {
            m_input = input;
        }

        protected override IEnumerator<int> Unpack () {
            uint offs, len, R0, LWM;

            var hist = new List<byte>();

            m_bitcount = 0;
            R0 = uint.MaxValue; // (uint) -1
            LWM = 0;

            m_buffer[m_pos] = ReadByte();
            hist.Add(m_buffer[m_pos++]);
            if (--m_length == 0)
                yield return m_pos;

            while (true) {
                if (GetBit() != 0) {
                    if (GetBit() != 0) {
                        if (GetBit() != 0) {
                            offs = 0;

                            for (int i = 0; i < 4; i++) {
                                offs = (offs << 1) + GetBit();
                            }

                            if (offs != 0) {
                                m_buffer[m_pos] = hist[(int)(hist.Count - offs)];
                            }
                            else {
                                m_buffer[m_pos] = 0x00;
                            }
                            hist.Add(m_buffer[m_pos++]);

                            LWM = 0;
                            if (--m_length == 0)
                                yield return m_pos;
                        }
                        else {
                            offs = (uint)ReadByte();

                            len = 2 + (offs & 0x0001);

                            offs >>= 1;

                            if (offs != 0) {
                                for (; len != 0; len--) {
                                    m_buffer[m_pos] = hist[(int)(hist.Count - offs)];
                                    hist.Add(m_buffer[m_pos++]);
                                    if (--m_length == 0)
                                        yield return m_pos;
                                }
                            }
                            else {
                                yield break;
                            }

                            R0 = offs;
                            LWM = 1;
                        }
                    }
                    else {
                        offs = GetGamma();

                        if ((LWM == 0) && (offs == 2)) {
                            offs = R0;

                            len = GetGamma();

                            for (; len != 0; len--) {
                                m_buffer[m_pos] = hist[(int)(hist.Count - offs)];
                                hist.Add(m_buffer[m_pos++]);
                                if (--m_length == 0)
                                    yield return m_pos;
                            }
                        }
                        else {
                            if (LWM == 0) {
                                offs -= 3;
                            }
                            else {
                                offs -= 2;
                            }

                            offs <<= 8;
                            offs += (uint)ReadByte();

                            len = GetGamma();

                            if (offs >= 32000) {
                                len++;
                            }
                            if (offs >= 1280) {
                                len++;
                            }
                            if (offs < 128) {
                                len += 2;
                            }

                            for (; len != 0; len--) {
                                m_buffer[m_pos] = hist[(int)(hist.Count - offs)];
                                hist.Add(m_buffer[m_pos++]);
                                if (--m_length == 0)
                                    yield return m_pos;
                            }

                            R0 = offs;
                        }

                        LWM = 1;
                    }
                }
                else {
                    m_buffer[m_pos] = ReadByte();
                    hist.Add(m_buffer[m_pos++]);
                    LWM = 0;
                    if (--m_length == 0)
                        yield return m_pos;
                }
            }
        }

        uint GetBit () {
            uint bit;

            if (m_bitcount-- == 0) {
                m_tag = (uint)ReadByte();
                m_bitcount = 7;
            }
            bit = (m_tag >> 7) & 0x01;
            m_tag <<= 1;

            return bit;
        }

        uint GetGamma () {
            uint result = 1;

            do {
                result = (result << 1) + GetBit();
            } while (GetBit() != 0);

            return result;
        }

        byte ReadByte () {
            int b = m_input.ReadByte();
            if (b == -1)
                throw new EndOfStreamException();
            return (byte)b;
        }
    }

    public class aPLibStream : PackedStream<aPLibCoroutine> {
        public aPLibStream (Stream input, CompressionMode mode = CompressionMode.Decompress, bool leave_open = false) : base (input, leave_open) {
            if (mode != CompressionMode.Decompress)
                throw new NotImplementedException ("aPLibStream compression not implemented");
        }
    }
}
