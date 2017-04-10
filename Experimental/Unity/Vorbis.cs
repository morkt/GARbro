//! \file       Vorbis.cs
//! \date       Fri Apr 07 21:07:30 2017
//! \brief      partial libvorbis port.
//
// Only parts crucial for FSB5 decoding got implemented.
// Parts that are not used in FSB5 decoder are left out completely or commented out.
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
// -----------------------------------------------------------------------------
//
// libvorbis Copyright (c) 2002-2008 Xiph.org Foundation
//
// Redistribution and use in source and binary forms, with or without
// modification, are permitted provided that the following conditions
// are met:
//
// - Redistributions of source code must retain the above copyright
// notice, this list of conditions and the following disclaimer.
//
// - Redistributions in binary form must reproduce the above copyright
// notice, this list of conditions and the following disclaimer in the
// documentation and/or other materials provided with the distribution.
//
// - Neither the name of the Xiph.org Foundation nor the names of its
// contributors may be used to endorse or promote products derived from
// this software without specific prior written permission.
//
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
// ``AS IS'' AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
// LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR
// A PARTICULAR PURPOSE ARE DISCLAIMED.  IN NO EVENT SHALL THE FOUNDATION
// OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
// SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT
// LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
// DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY
// THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
// (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
// OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
//

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace GameRes.Formats.Vorbis
{
    // struct vorbis_info
    // https://xiph.org/vorbis/doc/libvorbis/vorbis_info.html
    class VorbisInfo
    {
        public int  Version;
        public int  Channels;
        public int  Rate;
        public int  BitrateUpper;
        public int  BitrateNominal;
        public int  BitrateLower;

        public CodecSetupInfo CodecSetup = new CodecSetupInfo();

        const int TransormB = 1;
        const int WindowB = 1;
        const int TimeB = 1;
        const int FloorB = 2;
        const int ResB = 3;
        const int MapB = 1;

        Func<OggBitStream, VorbisInfoFloor>[] FloorMethods;

        public VorbisInfo ()
        {
            FloorMethods = new Func<OggBitStream, VorbisInfoFloor>[] {
                UnpackFloor0,
                UnpackFloor1
            };
        }

        // https://xiph.org/vorbis/doc/libvorbis/vorbis_synthesis_headerin.html
        public void SynthesisHeaderin (VorbisComment vc, OggPacket op)
        {
            using (var input = new OggBitStream (op))
            {
                int packtype = input.ReadUInt8();
                var buf = input.ReadBytes (6);
                if (!buf.AsciiEqual ("vorbis"))
                    throw new InvalidDataException ("Not an Ogg/Vorbis stream.");
                switch (packtype)
                {
                case 1:
                    if (!op.BoS)
                        throw InvalidHeader();
                    if (Rate != 0)
                        throw InvalidHeader();
                    UnpackInfo (input);
                    break;

                case 3:
                    if (0 == Rate)
                        throw InvalidHeader();
                    vc.UnpackComment (input);
                    break;

                case 5:
                    if (0 == Rate || null == vc.Vendor)
                        throw InvalidHeader();
                    UnpackBooks (input);
                    break;

                default:
                    throw InvalidHeader();
                }
            }
        }

        internal static int iLog (uint num)
        {
            int bits = 0;
            while (num != 0)
            {
                ++bits;
                num >>= 1;
            }
            return bits;
        }

        internal static int CountBits (uint num)
        {
            int bits = 0;
            if (num != 0)
                --num;
            while (num != 0)
            {
                ++bits;
                num >>= 1;
            }
            return bits;
        }

        /// <summary>
        /// Count number of bits set in <paramref name="num"/>.
        /// </summary>
        internal static int CountSetBits (uint num)
        {
            int bits = 0;
            while (num != 0)
            {
                bits += (int)num & 1;
                num >>= 1;
            }
            return bits;
        }

        // https://www.xiph.org/vorbis/doc/libvorbis/vorbis_packet_blocksize.html
        public int PacketBlockSize (OggPacket op)
        {
            using (var input = new OggBitStream (op))
            {
                // Check the packet type
                if (input.ReadBits (1) != 0)
                    throw new InvalidDataException ("Not an audio data packet.");

                int modebits = 0;
                for (int v = CodecSetup.Modes; v > 1; v >>= 1)
                {
                    modebits++;
                }

                // read our mode and pre/post windowsize
                int mode = input.ReadBits (modebits);

                if (-1 == mode)
                    throw new InvalidDataException ("Invalid Ogg/Vorbis packet.");

                return CodecSetup.BlockSizes[CodecSetup.ModeParam[mode].BlockFlag];
            }
        }

        void UnpackInfo (OggBitStream input)
        {
            Version = input.ReadInt32();
            if (Version != 0)
                throw new InvalidDataException ("Invalid Vorbis encoder version.");
            Channels = input.ReadUInt8();
            Rate = input.ReadInt32();

            BitrateUpper = input.ReadInt32();
            BitrateNominal = input.ReadInt32();
            BitrateLower = input.ReadInt32();

            CodecSetup.BlockSizes[0] = 1 << input.ReadBits (4);
            CodecSetup.BlockSizes[1] = 1 << input.ReadBits (4);

            if (input.ReadBits (1) != 1)
                throw InvalidHeader();
        }

        void UnpackBooks (OggBitStream input)
        {
            // codebooks
            CodecSetup.Books = input.ReadUInt8() + 1;
            if (CodecSetup.Books <= 0)
                throw InvalidHeader();

            for (int i = 0; i < CodecSetup.Books; ++i)
            {
                var param = StaticBookUnpack (input);
                if (null == param)
                    throw InvalidHeader();
                CodecSetup.BookParam[i] = param;
            }

            // time backend settings; hooks are unused
            int times = input.ReadBits (6) + 1;
            if (times <= 0)
                throw InvalidHeader();
            for (int i = 0; i < times; ++i)
            {
                int test = input.ReadBits (16);
                if (test < 0 || test >= TimeB)
                    throw InvalidHeader();
            }

            // floor backend settings
            CodecSetup.Floors = input.ReadBits (6) + 1;
            if (CodecSetup.Floors <= 0)
                throw InvalidHeader();
            for (int i = 0; i < CodecSetup.Floors; i++)
            {
                int floor_type = input.ReadBits (16);
                if (floor_type < 0 || floor_type >= FloorB)
                    throw InvalidHeader();
                CodecSetup.FloorType[i] = floor_type;
                var param = FloorMethods[floor_type] (input);
                if (null == param)
                    throw InvalidHeader();
                CodecSetup.FloorParam[i] = param;
            }

            // residue backend settings
            CodecSetup.Residues = input.ReadBits (6) + 1;
            if (CodecSetup.Residues <= 0)
                throw InvalidHeader();
            for (int i = 0; i < CodecSetup.Residues; ++i)
            {
                int residue_type = input.ReadBits (16);
                if (residue_type < 0 || residue_type >= ResB)
                    throw InvalidHeader();
                CodecSetup.ResidueType[i] = residue_type;
                var param = UnpackResidue (input);
                if (null == param)
                    throw InvalidHeader();
                CodecSetup.ResidueParam[i] = param;
            }

            // map backend settings
            CodecSetup.Maps = input.ReadBits (6) + 1;
            if (CodecSetup.Maps <= 0)
                throw InvalidHeader();
            for (int i = 0; i < CodecSetup.Maps; ++i)
            {
                int map_type = input.ReadBits (16);
                if (map_type < 0 || map_type >= MapB)
                    throw InvalidHeader();
                CodecSetup.MapType[i] = map_type;
                var param = UnpackMapping (input);
                if (null == param)
                    throw InvalidHeader();
                CodecSetup.MapParam[i] = param;
            }

            // mode settings
            CodecSetup.Modes = input.ReadBits (6) + 1;
            if (CodecSetup.Modes <= 0)
                throw InvalidHeader();
            for (int i = 0; i < CodecSetup.Modes; ++i)
            {
                CodecSetup.ModeParam[i].BlockFlag = input.ReadBits (1);
                CodecSetup.ModeParam[i].WindowType = input.ReadBits (16);
                CodecSetup.ModeParam[i].TransformType = input.ReadBits (16);
                CodecSetup.ModeParam[i].Mapping = input.ReadBits (8);

                if (CodecSetup.ModeParam[i].WindowType >= WindowB ||
                    CodecSetup.ModeParam[i].TransformType >= WindowB ||
                    CodecSetup.ModeParam[i].Mapping >= CodecSetup.Maps ||
                    CodecSetup.ModeParam[i].Mapping < 0)
                    throw InvalidHeader();
            }
            if (input.ReadBits (1) != 1)
                throw InvalidHeader();
        }

        StaticCodebook StaticBookUnpack (OggBitStream input)
        {
            // make sure alignment is correct
            if (input.ReadBits (24) != 0x564342)
                return null;

            var s = new StaticCodebook();

            // first the basic parameters
            s.dim = input.ReadBits (16);
            s.entries = input.ReadBits (24);
            if (-1 == s.entries)
                return null;

            if (iLog ((uint)s.dim) + iLog ((uint)s.entries) > 24)
                return null;

            // codeword ordering.... length ordered or unordered?
            switch (input.ReadBits (1))
            {
            case 0:
                // allocated but unused entries?
                int unused = input.ReadBits (1);
                // unordered
                s.lengthlist = new byte[s.entries];

                // allocated but unused entries?
                if (unused > 0)
                {
                    // yes, unused entries
                    for(int i = 0; i < s.entries; ++i)
                    {
                        if (input.ReadBits (1) > 0)
                        {
                            int num = input.ReadBits (5);
                            if (-1 == num)
                                return null;
                            s.lengthlist[i] = (byte)(num+1);
                        }
                        else
                            s.lengthlist[i] = 0;
                    }
                }
                else
                {
                    // all entries used; no tagging
                    for (int i = 0; i < s.entries; ++i)
                    {
                        int num = input.ReadBits (5);
                        if (-1 == num)
                            return null;
                        s.lengthlist[i] = (byte)(num+1);
                    }
                }
                break;

            case 1: // ordered
                int length = input.ReadBits (5) + 1;
                if (0 == length)
                    return null;
                s.lengthlist = new byte[s.entries];

                for (int i = 0; i < s.entries; )
                {
                    int num = input.ReadBits (iLog ((uint)(s.entries-i)));
                    if (-1 == num || length > 32 || num > s.entries-i
                        || (num > 0 && ((num-1) >> (length-1)) > 1))
                        return null;
                    for (int j = 0; j < num; ++j, ++i)
                        s.lengthlist[i] = (byte)length;
                    length++;
                }
                break;

            default:
                return null;
            }

            // Do we have a mapping to unpack?
            switch((s.maptype = input.ReadBits (4)))
            {
            case 0: // no mapping
                break;

            case 1: case 2:
                // implicitly populated value mapping
                // explicitly populated value mapping

                s.q_min = input.ReadInt32();
                s.q_delta = input.ReadInt32();
                s.q_quant = input.ReadBits (4) + 1;
                s.q_sequencep = input.ReadBits (1);
                if (-1 == s.q_sequencep)
                    return null;
                int quantvals = 0;
                switch (s.maptype)
                {
                case 1:
                    quantvals = s.dim == 0 ? 0 : s.Maptype1Quantvals();
                    break;
                case 2:
                    quantvals = s.entries * s.dim;
                    break;
                }

                // quantized values
                s.quantlist = new int[quantvals];
                for (int i = 0; i < quantvals; ++i)
                    s.quantlist[i] = input.ReadBits (s.q_quant);

                if (quantvals > 0 && s.quantlist[quantvals-1] == -1)
                    return null;
                break;

            default: // EOF
                return null;
            }

            // all set
            return s;
        }

        VorbisInfoFloor UnpackFloor0 (OggBitStream input)
        {
            var info = new VorbisInfoFloor();
            int order = input.ReadBits (8);
            int rate = input.ReadBits (16);
            int barkmap = input.ReadBits (16);
            int ampbits = input.ReadBits (6);
            int ampdB = input.ReadBits (8);
            int numbooks = input.ReadBits (4) + 1;

            if (order < 1 || rate < 1 || barkmap < 1 || numbooks < 1)
                return null;

            for (int j = 0; j < numbooks; ++j)
            {
                int books = input.ReadByte();
                if (books < 0 || books >= CodecSetup.Books)
                    return null;
            }
            return info;
        }

        VorbisInfoFloor UnpackFloor1 (OggBitStream input)
        {
            int max_class = -1;

            var info = new VorbisInfoFloor();
            // read partitions
            int partitions = input.ReadBits (5); // only 0 to 31 legal
            var partition_class = new int[partitions];
            for (int j = 0; j < partitions; ++j)
            {
                partition_class[j] = input.ReadBits (4); // only 0 to 15 legal
                if (partition_class[j] < 0)
                    return null;
                if (max_class < partition_class[j])
                    max_class = partition_class[j];
            }

            // read partition classes
            var class_dim = new int[max_class+1];
            for (int j = 0; j < max_class+1; ++j)
            {
                class_dim[j] = input.ReadBits (3) + 1; // 1 to 8
                int class_subs = input.ReadBits (2); // 0,1,2,3 bits
                if (class_subs < 0)
                    return null;
                if (class_subs > 0)
                    input.ReadBits (8); // class_book
                for (int k = 0; k < (1 << class_subs); ++k)
                {
                    int class_subbook = input.ReadBits (8) - 1; // info.class_subbook[j][k]
                }
            }

            // read the post list
            int mult = input.ReadBits (2) + 1;     // only 1,2,3,4 legal now
            int rangebits = input.ReadBits (4);
            if (rangebits < 0)
                return null;

            int count = 0;
//            var postlist = new int[VorbisInfoFloor.Posit + 2];
            for (int j = 0, k = 0; j < partitions; ++j)
            {
                count += class_dim[partition_class[j]];
                if (count > VorbisInfoFloor.Posit)
                    return null;
                for (; k < count; ++k)
                {
                    int t = input.ReadBits (rangebits);
                    if (t < 0 || t >= (1 << rangebits))
                        return null;
//                    postlist[k+2] = t;
                }
            }
//            postlist[0] = 0;
//            postlist[1] = 1<<rangebits;

            // don't allow repeated values in post list as they'd result in
            // zero-length segments
            /*
            var indices = Enumerable.Range (0, count+2).OrderBy (i => postlist[i]).ToArray();
            for (int j = 1; j < count+2; j++)
                if(postlist[indices[j-1]] == postlist[indices[j]])
                    return null;
            */
            return info;
        }

        object UnpackResidue (OggBitStream input)
        {
            var info = new VorbisInfoResidue();

            info.begin = input.ReadBits (24);
            info.end = input.ReadBits (24);
            info.grouping = input.ReadBits (24) + 1;
            info.partitions = input.ReadBits (6) + 1;
            info.groupbook = input.ReadBits (8);

            // check for premature EOP
            if (info.groupbook < 0)
                return null;

            int acc = 0;
            for (int j = 0; j < info.partitions; ++j)
            {
                int cascade = input.ReadBits (3);
                int cflag = input.ReadBits (1);
                if (cflag < 0)
                    return null;
                if (cflag > 0)
                {
                    int c = input.ReadBits (5);
                    if (c < 0)
                        return null;
                    cascade |= c << 3;
                }
                // info.secondstages[j] = cascade;

                acc += CountSetBits ((uint)cascade);
            }
            for (int j = 0; j < acc; ++j)
            {
                int book = input.ReadBits (8);
                if (book < 0)
                    return null;
                if (book >= CodecSetup.Books)
                    return null;
//                if (CodecSetup.book_param[book].maptype == 0) return null;
//                info.booklist[j] = book;
            }
            if (info.groupbook >= CodecSetup.Books)
                return null;

            /*
            int entries = CodecSetup.book_param[info.groupbook].entries;
            int dim = CodecSetup.book_param[info.groupbook].dim;
            int partvals = 1;
            if (dim < 1)
                return null;
            while (dim > 0)
            {
                partvals *= info.partitions;
                if (partvals > entries)
                    return null;
                dim--;
            }
            info.partvals = partvals;
            */
            return info;
        }

        VorbisInfoMapping UnpackMapping (OggBitStream input)
        {
            var info = new VorbisInfoMapping();

            int b = input.ReadBits (1);
            if (b < 0)
                return null;
            if (b > 0)
            {
                info.submaps = input.ReadBits (4) + 1;
                if (info.submaps <= 0)
                    return null;
            }
            else
                info.submaps = 1;

            b = input.ReadBits (1);
            if (b < 0)
                return null;
            if (b > 0)
            {
                info.coupling_steps = input.ReadBits (8) + 1;
                if (info.coupling_steps <= 0)
                    return null;
                for (int i = 0; i < info.coupling_steps; ++i)
                {
                    int bits = CountBits ((uint)Channels);
                    int testM = input.ReadBits (bits);
                    int testA = input.ReadBits (bits);
                    if (testM < 0 || testA < 0 || testM == testA
                        || testM >= Channels || testA >= Channels)
                        return null;
                }
            }

            if (input.ReadBits (2) != 0)
                return null;

            if (info.submaps > 1)
            {
                for (int i = 0; i < Channels; ++i)
                {
                    int chmuxlist = input.ReadBits (4);
                    if (chmuxlist >= info.submaps || chmuxlist < 0)
                        return null;
                }
            }
            for (int i = 0; i < info.submaps; ++i)
            {
                input.ReadByte(); // time submap unused
                int floorsubmap = input.ReadByte();
                if (floorsubmap >= CodecSetup.Floors || floorsubmap < 0)
                    return null;
                int residuesubmap = input.ReadByte();
                if (residuesubmap >= CodecSetup.Residues || residuesubmap < 0)
                    return null;
            }
            return info;
        }

        internal static InvalidDataException InvalidHeader ()
        {
            return new InvalidDataException ("Invalid header in Ogg/Vorbis stream.");
        }
    }

    // struct vorbis_comment
    // https://xiph.org/vorbis/doc/libvorbis/vorbis_comment.html
    class VorbisComment
    {
        public List<byte[]> Comments;
        public byte[]       Vendor;

        internal static readonly byte[] EncodeVendorString = Encoding.UTF8.GetBytes ("mørkt GARbro 20170407");

        public VorbisComment ()
        {
            Comments = new List<byte[]>();
        }

        // https://xiph.org/vorbis/doc/libvorbis/vorbis_commentheader_out.html
        public void HeaderOut (OggPacket packet)
        {
            using (var buf = new MemoryStream())
            using (var output = new BinaryWriter (buf))
            {
                // preamble
                output.Write ((byte)3);
                output.Write ("vorbis".ToCharArray());

                // vendor
                output.Write (EncodeVendorString.Length);
                output.Write (EncodeVendorString);

                // comments
                output.Write (Comments.Count);
                foreach (var comment in Comments)
                {
                    if (comment != null && comment.Length > 0)
                    {
                        output.Write (comment.Length);
                        output.Write (comment);
                    }
                    else
                    {
                        output.Write (0);
                    }
                }
                output.Write ((byte)1);
                output.Flush();

                packet.SetPacket (1, buf.ToArray());
                packet.BoS = false;
                packet.EoS = false;
                packet.GranulePos = 0;
            }
        }

        internal void UnpackComment (OggBitStream input)
        {
            int vendor_len = input.ReadInt32();
            if (vendor_len < 0)
                throw VorbisInfo.InvalidHeader();
            var vendor = input.ReadBytes (vendor_len);

            int count = input.ReadInt32();
            if (count < 0)
                throw VorbisInfo.InvalidHeader();

            var comments = new List<byte[]> (count);
            for (int i = 0; i < count; ++i)
            {
                int len = input.ReadInt32();
                if (len < 0)
                    throw VorbisInfo.InvalidHeader();
                var bytes = input.ReadBytes (len);
                comments.Add (bytes);
            }
            if (input.ReadBits (1) != 1)
                throw VorbisInfo.InvalidHeader();

            this.Vendor = vendor;
            this.Comments = comments;
        }
    }

    // codec_setup_info
    class CodecSetupInfo
    {
        public int[]    BlockSizes = new int[2];

        public int      Modes;
        public int      Maps;
        public int      Floors;
        public int      Residues;
        public int      Books;

        internal VorbisInfoMode[]   ModeParam = new VorbisInfoMode[64];
        internal int[]              ResidueType = new int[64];
        internal object[]           ResidueParam = new object[64];
        internal int[]              FloorType = new int[64];
        internal object[]           FloorParam = new object[64];
        internal int[]              MapType = new int[64];
        internal VorbisInfoMapping[] MapParam = new VorbisInfoMapping[64];
        internal StaticCodebook[]   BookParam = new StaticCodebook[256];
    }

    // struct vorbis_info_mode
    struct VorbisInfoMode
    {
        public int  BlockFlag;
        public int  WindowType;
        public int  TransformType;
        public int  Mapping;
    }

    // struct static_codebook
    class StaticCodebook
    {
        public int      dim;        // codebook dimensions (elements per vector)
        public int      entries;    // codebook entries
        public byte[]   lengthlist; // codeword lengths in bits

        // mapping ***************************************************************
        public int      maptype;    // 0=none
                                    // 1=implicitly populated values from map column
                                    // 2=listed arbitrary values

        // The below does a linear, single monotonic sequence mapping.
        public int      q_min;      // packed 32 bit float; quant value 0 maps to minval
        public int      q_delta;    // packed 32 bit float; val 1 - val 0 == delta
        public int      q_quant;    // bits: 0 < quant <= 16
        public int      q_sequencep; // bitflag

        public int[]    quantlist;  // map == 1: (int)(entries^(1/dim)) element column map
                                    // map == 2: list of dim*entries quantized entry vals

        internal int Maptype1Quantvals ()
        {
            int vals = (int)Math.Floor (Math.Pow ((float)entries, 1.0f / dim));

            // the above *should* be reliable, but we'll not assume that FP is
            // ever reliable when bitstream sync is at stake; verify via integer
            // means that vals really is the greatest value of dim for which
            // vals^b->bim <= b->entries.
            // treat the above as an initial guess
            for (;;)
            {
                int acc = 1;
                int acc1 = 1;
                for (int i = 0; i < dim; ++i)
                {
                    acc *= vals;
                    acc1 *= vals+1;
                }
                if (acc <= entries && acc1 > entries)
                    break;
                if (acc > entries)
                    vals--;
                else
                    vals++;
            }
            return vals;
        }
    }

    class VorbisInfoMapping
    {
        public int  submaps;
        public int  coupling_steps;
    }

    class VorbisInfoFloor
    {
        public const int Posit = 63;
    }

    class VorbisInfoResidue
    {
        public int  begin;
        public int  end;
        public int  grouping;
        public int  partitions;
        public int  groupbook;
    }
}
