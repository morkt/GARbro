//! \file       OggStream.cs
//! \date       Sat Apr 08 01:43:58 2017
//! \brief      libogg partial implementation.
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
using System.IO;
using System.Text;
using GameRes.Utility;

namespace GameRes.Formats.Vorbis
{
    internal sealed class OggBitStream : IDisposable
    {
        LsbBitStream    m_input;

        public OggBitStream (OggPacket input)
        {
            // certainly an overhead to create a new stream for every packet, but it's so convenient
            var buf = new MemoryStream (input.Packet);
            m_input = new LsbBitStream (buf);
        }

        /// <summary>Read <paramref name="count"/> bits from a stream.</summary>
        /// <returns>-1 if there was not enough bits in a stream</returns>
        public int ReadBits (int count)
        {
            if (count <= 24)
                return m_input.GetBits (count);
            else if (count > 32)
                throw new ArgumentOutOfRangeException ("count", "Attempted to read more than 32 bits from OggBitStream.");
            int lo = m_input.GetBits (24);
            return m_input.GetBits (count - 24) << 24 | lo;
        }

        /// <summary>Read 8-bit integer from bitstream.</summary>
        /// <returns>-1 if there was not enough bits in a stream</returns>
        public int ReadByte ()
        {
            return ReadBits (8);
        }

        /// <summary>Read 8-bit integer from bitstream.</summary>
        /// <exception cref="EndOfStreamException">Thrown if there's not enough bits in a stream.</exception>
        public byte ReadUInt8 ()
        {
            int b = ReadBits (8);
            if (-1 == b)
                throw new EndOfStreamException();
            return (byte)b;
        }

        /// <summary>Read 32-bit integer from bitstream.</summary>
        /// <exception cref="EndOfStreamException">Thrown if there's not enough bits in a stream.</exception>
        public int ReadInt32 ()
        {
            int lo = ReadBits (16);
            int hi = ReadBits (16);
            if (-1 == lo || -1 == hi)
                throw new EndOfStreamException();
            return hi << 16 | lo;
        }

        /// <summary>Attempt to read <paramref name="count"/> bytes from stream.</summary>
        /// <exception cref="EndOfStreamException">Thrown if there's not enough bytes in a bitstream.</exception>
        public byte[] ReadBytes (int count)
        {
            var buf = new byte[count];
            for (int i = 0; i < count; ++i)
                buf[i] = ReadUInt8();
            return buf;
        }

        bool m_disposed = false;
        public void Dispose ()
        {
            if (!m_disposed)
            {
                m_input.Dispose();
                m_disposed = true;
            }
        }
    }

    // struct ogg_packet
    // https://xiph.org/ogg/doc/libogg/ogg_packet.html
    internal class OggPacket
    {
        public byte[]   Packet;
        public bool     BoS;
        public bool     EoS;

        public long     GranulePos;
        public long     PacketNo;

        public void SetPacket (long packet_no, byte[] packet)
        {
            PacketNo = packet_no;
            Packet = packet;
        }
    }

    // struct ogg_stream_state
    // https://xiph.org/ogg/doc/libogg/ogg_stream_state.html
    internal class OggStreamState
    {
        byte[]  BodyData;       // bytes from packet bodies
        int     BodyStorage;    // storage elements allocated
        int     BodyFill;       // elements stored; fill mark
        int     BodyReturned;   // elements of fill returned

        int[]   LacingVals;     // The values that will go to the segment table granulepos values for headers.
        long[]  GranuleVals;    // Not compact this way, but it is simple coupled to the lacing fifo.
        int     LacingStorage;
        int     LacingFill;

        byte[]  Header;         // working space for header encode
        int     HeaderFill;

        bool    EoS;            // set when we have buffered the last packet in the logical bitstream
        bool    BoS;            // set after we've written the initial page of a logical bitstream
        int     SerialNo;
        int     PageNo;
        long    PacketNo;       // sequence number for decode; the framing knows where there's a hole in the data,
                                // but we need coupling so that the codec (which is in a seperate abstraction
                                // layer) also knows about the gap
        long    GranulePos;

        // https://xiph.org/ogg/doc/libogg/ogg_stream_init.html
        public OggStreamState (int serial_no)
        {
            BodyStorage = 0x4000;
            LacingStorage = 0x400;

            BodyData = new byte[BodyStorage];
            LacingVals = new int[LacingStorage];
            GranuleVals = new long[LacingStorage];
            Header = new byte[282];

            SerialNo = serial_no;
        }

        public void Clear ()
        {
            BodyStorage = 0;
            BodyFill = 0;
            BodyReturned = 0;
            LacingStorage = 0;
            LacingFill = 0;
            HeaderFill = 0;
            EoS = false;
            BoS = false;
            SerialNo = 0;
            PageNo = 0;
            PacketNo = 0;
            GranulePos = 0;
        }

        public bool PacketIn (OggPacket op)
        {
            int bytes = op.Packet.Length;
            int lacing_vals = bytes / 255 + 1;

            if (BodyReturned > 0)
            {
                // advance packet data according to the body_returned pointer.
                // We had to keep it around to return a pointer into the buffer last call.

                BodyFill -= BodyReturned;
                if (BodyFill > 0)
                    Buffer.BlockCopy (BodyData, BodyReturned, BodyData, 0, BodyFill);
                BodyReturned = 0;
            }

            // make sure we have the buffer storage
            if(!BodyExpand (bytes) || !LacingExpand (lacing_vals))
                return false;

            // Copy in the submitted packet.
            Buffer.BlockCopy (op.Packet, 0, BodyData, BodyFill, op.Packet.Length);
            BodyFill += op.Packet.Length;

            // Store lacing vals for this packet
            int i;
            for (i = 0; i < lacing_vals-1; ++i)
            {
                LacingVals[LacingFill + i] = 0xFF;
                GranuleVals[LacingFill + i] = GranulePos;
            }
            LacingVals[LacingFill + i] = bytes % 0xFF;
            GranulePos = GranuleVals[LacingFill+i] = op.GranulePos;

            // flag the first segment as the beginning of the packet
            LacingVals[LacingFill] |= 0x100;

            LacingFill += lacing_vals;
            PacketNo++;
            EoS = op.EoS;

            return true;
        }

        public void Write (Stream output)
        {
            var page = new OggPage();
            while (PageOut (page))
            {
                output.Write (page.Header, 0, page.HeaderLength);
                output.Write (page.Body, page.BodyStart, page.BodyLength);
            }
        }

        public void Flush (Stream output)
        {
            var page = new OggPage();
            while (Flush (page, true, 0x1000))
            {
                output.Write (page.Header, 0, page.HeaderLength);
                output.Write (page.Body, page.BodyStart, page.BodyLength);
            }
        }

        public bool PageOut (OggPage page)
        {
            bool force = EoS && (LacingFill > 0) || (LacingFill > 0 && !BoS);
            return Flush (page, force, 0x1000);
        }

        bool BodyExpand (int needed)
        {
            if (BodyStorage - needed <= BodyFill)
            {
                if (BodyStorage > int.MaxValue - needed)
                {
                    Clear();
                    return false;
                }
                int body_storage = BodyStorage + needed;
                if (body_storage < int.MaxValue - 1024)
                    body_storage += 1024;
                Array.Resize (ref BodyData, body_storage);
                BodyStorage = body_storage;
            }
            return true;
        }

        bool LacingExpand (int needed)
        {
            if (LacingStorage - needed <= LacingFill)
            {
                if (LacingStorage > int.MaxValue - needed)
                {
                    Clear();
                    return false;
                }
                int lacing_storage = LacingStorage + needed;
                if (lacing_storage < int.MaxValue - 32)
                    lacing_storage += 32;
                Array.Resize (ref LacingVals, lacing_storage);
                Array.Resize (ref GranuleVals, lacing_storage);
                LacingStorage = lacing_storage;
            }
            return true;
        }

        bool Flush (OggPage og, bool force, int fill)
        {
            int maxvals = Math.Min (LacingFill, 0xFF);
            if (0 == maxvals)
                return false;

            // construct a page
            // decide how many segments to include

            int vals = 0;
            int acc = 0;
            long granule_pos = -1;

            // If this is the initial header case, the first page must only include
            // the initial header packet
            if (!BoS)   // 'initial header page' case
            {
                granule_pos = 0;
                for (vals = 0; vals < maxvals; vals++)
                {
                    if ((LacingVals[vals] & 0xFF) < 0xFF)
                    {
                        vals++;
                        break;
                    }
                }
            }
            else
            {
                int packets_done = 0;
                int packet_just_done = 0;
                for (vals = 0; vals < maxvals; vals++)
                {
                    if (acc > fill && packet_just_done >= 4)
                    {
                        force = true;
                        break;
                    }
                    acc += LacingVals[vals] & 0xFF;
                    if ((LacingVals[vals] & 0xFF) < 0xFF)
                    {
                        granule_pos = GranuleVals[vals];
                        packet_just_done = ++packets_done;
                    }
                    else
                        packet_just_done = 0;
                }
                if (0xFF == vals)
                    force = true;
            }
            if (!force)
                return false;

            // construct the header in temp storage
            Encoding.ASCII.GetBytes ("OggS", 0, 4, Header, 0);

            // stream structure version
            Header[4] = 0;

            // continued packet flag?
            Header[5] = 0;
            if ((LacingVals[0] & 0x100) == 0)
                Header[5] |= 1;
            // first page flag?
            if (!BoS)
                Header[5] |= 2;
            // last page flag?
            if (EoS && LacingFill == vals)
                Header[5] |= 4;
            BoS = true;

            // 64 bits of PCM position
            LittleEndian.Pack (granule_pos, Header, 6);

            // 32 bits of stream serial number
            LittleEndian.Pack (SerialNo, Header, 14);

            // 32 bits of page counter (we have both counter and page header because this
            // val can roll over)
            if (-1 == PageNo)
                PageNo = 0;
            LittleEndian.Pack (PageNo, Header, 18);
            ++PageNo;

            int bytes = 0;
            // segment table
            Header[26] = (byte)vals;
            for (int i = 0; i < vals; ++i)
                bytes += Header[i+27] = (byte)LacingVals[i];

            // set pointers in the ogg_page struct
            og.Header = Header;
            og.HeaderLength = HeaderFill = vals + 27;
            og.Body = BodyData;
            og.BodyStart = BodyReturned;
            og.BodyLength = bytes;

            // advance the lacing data and set the body_returned pointer
            LacingFill -= vals;
            Array.Copy (LacingVals, vals, LacingVals, 0, LacingFill);
            Array.Copy (GranuleVals, vals, GranuleVals, 0, LacingFill);
            BodyReturned += bytes;

            // calculate the checksum
            og.SetChecksum();

            return true;
        }
    }

    // struct ogg_page
    // https://xiph.org/ogg/doc/libogg/ogg_page.html
    internal class OggPage
    {
        public byte[]   Header;
        public int      HeaderLength;
        public byte[]   Body;
        public int      BodyStart;
        public int      BodyLength;

        public void SetChecksum ()
        {
            Header[22] = Header[23] = Header[24] = Header[25] = 0;

            uint crc = Crc32Normal.UpdateCrc (0, Header, 0, HeaderLength);
            crc = Crc32Normal.UpdateCrc (crc, Body, BodyStart, BodyLength);

            LittleEndian.Pack (crc, Header, 22);
        }
    }
}
