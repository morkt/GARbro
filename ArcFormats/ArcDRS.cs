//! \file       ArcDRS.cs
//! \date       Thu Aug 21 06:11:09 2014
//! \brief      Digital Romance System archive implementation.
//
// Copyright (C) 2014 by morkt
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
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using GameRes.Utility;
using GameRes.Formats.Strings;
using GameRes.Formats.Properties;

namespace GameRes.Formats.DRS
{
    [Export(typeof(ArchiveFormat))]
    public class DrsOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DRS"; } }
        public override string Description { get { return "Digital Romance System resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool     CanCreate { get { return false; } }

        public DrsOpener ()
        {
            Extensions = Enumerable.Empty<string>(); // DRS archives have no extensions
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (file.MaxOffset > uint.MaxValue)
                return null;
            int dir_size = file.View.ReadUInt16 (0);
            if (dir_size < 0x20 || 0 != (dir_size & 0xf) || dir_size + 2 >= file.MaxOffset)
                return null;
            byte first = file.View.ReadByte (2);
            if (0 == first)
                return null;
            file.View.Reserve (0, (uint)dir_size + 2);
            int dir_offset = 2;

            uint next_offset = file.View.ReadUInt32 (dir_offset+12);
            if (next_offset > file.MaxOffset || next_offset < dir_size+2)
                return null;
            var encoding = Encodings.cp932.WithFatalFallback();
            byte[] name_raw = new byte[12];

            int count = dir_size / 0x10 - 1;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                file.View.Read (dir_offset, name_raw, 0, 12);
                int name_length = name_raw.Length;
                while (name_length > 0 && 0 == name_raw[name_length-1])
                    --name_length;
                if (0 == name_length)
                    return null;
                uint offset = next_offset;
                dir_offset += 0x10;
                next_offset = file.View.ReadUInt32 (dir_offset+12);
                if (next_offset > file.MaxOffset || next_offset < offset)
                    return null;
                string name = encoding.GetString (name_raw, 0, name_length).ToLowerInvariant();
                var entry = FormatCatalog.Instance.CreateEntry (name);
                entry.Offset = offset;
                entry.Size = next_offset - offset;
                dir.Add (entry);
            }
            return new ArcFile (file, this, dir);
        }
    }

    internal class IsfOptions : ResourceOptions
    {
        public byte[] Secret;
    }

    internal class IsfArchive : ArcFile
    {
        public byte[] Secret;

        public IsfArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, byte[] secret = null)
            : base (arc, impl, dir)
        {
            Secret = secret;
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class MpxOpener : ArchiveFormat
    {
        public override string         Tag { get { return "IKURA/GDL"; } }
        public override string Description { get { return "IKURA GDL resource archive"; } }
        public override uint     Signature { get { return 0x4d324d53; } } // 'SM2M'
        public override bool  IsHierarchic { get { return false; } }
        public override bool     CanCreate { get { return false; } }

        public MpxOpener ()
        {
            Extensions = Enumerable.Empty<string>(); // DRS archives have no extensions
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (4, "PX10") || file.MaxOffset > uint.MaxValue)
                return null;
            int count = file.View.ReadInt32 (8);
            if (count <= 0 || count > 0xfffff)
                return null;
            uint index_size = file.View.ReadUInt32 (12);
            if (index_size > file.MaxOffset)
                return null;
            var encoding = Encodings.cp932.WithFatalFallback();
            byte[] name_raw = new byte[12];

            long dir_offset = 0x20;
            var dir = new List<Entry> (count);
            bool has_scripts = false;
            for (int i = 0; i < count; ++i)
            {
                file.View.Read (dir_offset, name_raw, 0, 12);
                int name_length = name_raw.Length;
                while (name_length > 0 && 0 == name_raw[name_length-1])
                    --name_length;
                if (0 == name_length)
                    return null;
                string name = encoding.GetString (name_raw, 0, name_length).ToLowerInvariant();
                Entry entry;
                if (name.EndsWith (".isf") || name.EndsWith (".snr"))
                {
                    entry = new Entry { Name = name, Type = "script" };
                    has_scripts = true;
                }
                else
                    entry = FormatCatalog.Instance.CreateEntry (name);
                entry.Offset = file.View.ReadUInt32 (dir_offset+12);
                entry.Size   = file.View.ReadUInt32 (dir_offset+16);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                dir_offset += 0x14;
            }
            if (has_scripts)
                return new IsfArchive (file, this, dir);
            else
                return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            if (!(arc is IsfArchive) || entry.Type != "script" || entry.Size < 0x10)
                return arc.File.CreateStream (entry.Offset, entry.Size);
            bool encoded = arc.File.View.AsciiEqual (entry.Offset+entry.Size-0x10, "SECRETFILTER100a");
            uint entry_size = entry.Size;
            var isf = arc as IsfArchive;
            if (encoded)
            {
                entry_size -= 0x10;
                if (null == isf.Secret)
                    isf.Secret = QuerySecret();
                if (null == isf.Secret || 0 == isf.Secret.Length)
                    return arc.File.CreateStream (entry.Offset, entry.Size);
            }
            var data = new byte[entry_size];
            arc.File.View.Read (entry.Offset, data, 0, entry_size);
            if (encoded)
            {
                var decoder = new IsfDecoder (isf.Secret);
                decoder.Decode (data);
            }
            int signature = LittleEndian.ToUInt16 (data, 4);
            if (0x9795 == signature)
            {
                ApplyTransformation (data, 8, x => x >> 2 | x << 6);
            }
            else if (0xd197 == signature)
            {
                ApplyTransformation (data, 8, x => ~x);
            }
            else if (0xce89 == signature && 0 != data[6])
            {
                byte key = data[6];
                ApplyTransformation (data, 8, x => x ^ key);
            }
            return new MemoryStream (data);
        }

        public override ResourceOptions GetDefaultOptions ()
        {
            return new IsfOptions {
                Secret = GetSecret (Settings.Default.ISFScheme),
            };
        }

        public override object GetAccessWidget ()
        {
            return new GUI.WidgetISF();
        }

        private byte[] QuerySecret ()
        {
            var options = Query<IsfOptions> (arcStrings.ArcEncryptedNotice);
            return options.Secret;
        }

        private static byte[] GetSecret (string scheme)
        {
            byte[] secret;
            if (KnownSecrets.TryGetValue (scheme, out secret))
                return secret;
            return null;
        }

        private static void ApplyTransformation (byte[] data, int offset, Func<byte, int> method)
        {
            for (int i = offset; i < data.Length; ++i)
                data[i] = (byte)method (data[i]);
        }

        public static readonly Dictionary<string, byte[]> KnownSecrets = new Dictionary<string, byte[]>
        {
            { arcStrings.ISFIgnoreEncryption, new byte[0] },
            { "Anata no Osanazuma", Encoding.ASCII.GetBytes("UOB0GMVM8K812GZP1VJCADNP2LX543R3BPOSMRU6BAKIVYB6W2JRRQFHN3YC5NSV5AITT6OXIDOW0YROTRT7MFJ45R1VYFF71MGI4XIOUZC511R0HAJC6WZU0EZEUUH0K7AT0U749KIOVLF5FA4C3BFXM2T4G8F57SW5EG64LU9GXRKZ2O0YGN7QA4QRUMALDFFJJ6NHLTVYYHKHRMWERQ8K782KITVCCH2G65C2WRQ85HINSMLHW06TIBADAGLEDK9YDARS47ET2A6JAF4I8WAW7K1FIXISKJKNNC0KP1MSO5Y6C2JBEFIA7G5NTECMXMNLYSTC0D9SF6VNXW7ALIGEBRJFDUGZE9FILWM4BMDY25N3YHKCAUBSBJBMMZX0T7XHAQHXUF3ZUROO5P6WGU2YU3RPICFVXGAA646TOTT2DEXK9RTXFQ8IE1BNC7ARDCW4TP2US01YN84ZV1FYWJ10HAJZUPKYQF7R5V654IEX39K6HC1Z32C3FZ6Q5NVNELWIJT9VG6QRK89FJLXLKUPFJL4GROK7TZQ7QHM6J3ZU9NU0B5GV8A9PXUFAO9KW80Q6WNABYMA1VVNC2J5GHHWNPQ9U7LK3TF0CMSYGAFYPGZLPXNPH3D41VL1A3NU6N6S3V9XXYFESEUCKYK0NAM1MBZ9BJ6AMQKKF25719V8RN54C1Q27LRI8SSK62RKSZ4VTYAE2J87LFAVI2U1V21YQ13ULOQW86PH9RDUEVA7UEJ6JTU7X1GJYNC1VR66F2EAC3TFGC4AKOVAZW137EW0V6AAU201X7ND3YTUNX2QBR24DXXC1BWTEZUXVTXNB63W5RD9GIQV9IP6CDDPAEW2IGADTKBW7DPANCAOBU18Z84PHLF6EQ2H7S7A2DHC75Q1AX4NSN7JQNGJGXHTAOBHI5DDMME5ZQPT856DEIMM6VM8XI8WE4E4JKYMY5W7ABEH82LSRX4GKFAYNHLUSBT4C0A215KZTVX1K49EHAO8O7YJCUS8YVVU2B3XG8QOBQMUJBJ3IH1XWH6FM29OLYS9DHMZ83PJVRM0M7JH7RAQDV9DS4FJ2IF27CCVZZBVX18AHEQDI8486Y3NETMBG3LBF41FTGMVXNC7R914HXOHFQYQ4EDUKJ4VJJWGIE63H12VG3361832LZO6KFAHMEFPZK6BAJ643VJWZT4Z4QS3JAUM5P5394MS2GDVP9IJ727D6831GDB9NYONACW2ITF51S2LTUQ05XJ18WJUSABOJ51W9QODRH9R1D1CZB175ZEWTAIZRLIGFSVFCIHNBUJB0Z5LWDTNGW5505H1K8CROH1YGNFC4CW760NK9TWIWX9M6QFLZZKFCSP7RCEJ5DT854MJ6T55ODQ98NQB5MKFQYXM44D5ZYX0RMHPWKTHJVUEFA9WBQVLLZT0DR7P4XXQPQTMU0CC9C3VFYI0L5390P887CXEFY5MJYBGUUWLK123ANOE220DLPK5B7SU6GMXR3UV6K931FJ0I6JVLWLBADZO6Z381MZXR8E2IFVS4AH0Z5N72DV3GV9QAMDB5EJYUQ6RVHTN9OY50W378H67U4SAY9AAXM9C67I7SKWLQW7W9MOSQIUMNVNCGEN0CRRIXMV62AGPRLNBU8VT41HYGMNXZTHQO6CU15X121MYGOTZAFR8WVT98M33KJVBOZXRI9FMG03HUAF0IQ4ZF6P5EZ4X6V45E14R4RDBA59DVWD2UED1ZB6SRJDKREO4F05CMOV1465RCANAZHLHR0FVHV86126OODW2MB67UNGS1GGD3UHBV7TL8GRWQZBTHCZHQBPUSUJPKO5LQ4HM95UOZGL0FL30LXL6CK2FOTEQIK6PTNMJVE5G7U3PONIA6WMSV7YZLHO4CZ8ZPOL7JRJOOD79U5T7RVKYJQB54GR5E23964QRYA1POKM19EEWC6MFIU5Y75J5Y9QXER3HQFQEURNB7XTYAZMWWJO25SPI7AAVX3BKERW6817IQMRKD533O6ANRDIL840HSGRAG9ZX9AX1XK1DRE6URAGE5NWT0") },
            { "Natsu no Hitoshizuku", Encoding.ASCII.GetBytes("UOB0GMVM8K812GZP1VJCADNP2LX543R3BPOSMRU6BAKIVYB6W2JRRQFHN3YC5NSV5AITT6OXIDOW0YROTRT7MFJ45R1VYFF71MGI4XIOUZC511R0HAJC6WZU0EZEUUH0K7AT0U749KIOVLF5FA4C3BFXM2T4G8F57SW5EG64LU9GXRKZ2O0YGN7QA4QRUMALDFFJJ6NHLTVYYHKHRMWERQ8K782KITVCCH2G65C2WRQ85HINSMLHW06TIBADAGLEDK9YDARS47ET2A6JAF4I8WAW7K1FIXISKJKNNC0KP1MSO5Y6C2JBEFIA7G5NTECMXMNLYSTC0D9SF6VNXW7ALIGEBRJFDUGZE9FILWM4BMDY25N3YHKCAUBSBJBMMZX0T7XHAQHXUF3ZUROO5P6WGU2YU3RPICFVXGAA646TOTT2DEXK9RTXFQ8IE1BNC7ARDCW4TP2US01YN84ZV1FYWJ10HAJZUPKYQF7R5V654IEX39K6HC1Z32C3FZ6Q5NVNELWIJT9VG6QRK89FJLXLKUPFJL4GROK7TZQ7QHM6J3ZU9NU0B5GV8A9PXUFAO9KW80Q6WNABYMA1VVNC2J5GHHWNPQ9U7LK3TF0CMSYGAFYPGZLPXNPH3D41VL1A3NU6N6S3V9XXYFESEUCKYK0NAM1MBZ9BJ6AMQKKF25719V8RN54C1Q27LRI8SSK62RKSZ4VTYAE2J87LFAVI2U1V21YQ13ULOQW86PH9RDUEVA7UEJ6JTU7X1GJYNC1VR66F2EAC3TFGC4AKOVAZW137EW0V6AAU201X7ND3YTUNX2QBR24DXXC1BWTEZUXVTXNB63W5RD9GIQV9IP6CDDPAEW2IGADTKBW7DPANCAOBU18Z84PHLF6EQ2H7S7A2DHC75Q1AX4NSN7JQNGJGXHTAOBHI5DDMME5ZQPT856DEIMM6VM8XI8WE4E4JKYMY5W7ABEH82LSRX4GKFAYNHLUSBT4C0A215KZTVX1K49EHAO8O7YJCUS8YVVU2B3XG8QOBM6XHYQDK2X06ZJTDP4IN5K6GU2NKNS0CK9C2A21LZ1288LBBSEEHQ45NO1585NGFB91OSXUZYE7IBBC6L014RMNBWDZH755CN1PJ3UG8UKZK1KJBCF5LHHMXCSC0TILZBP6F453SHE5DK0637FNELXL0S8I6DTV6VQFM9YO6KZMIJ27WA2LM5ZV1W00PPVRVUSLYA4SYQRH0YY95HQ8UOEBM7LF00AQZOVYSGDNOICWI9OLBQZX61SHB7VLYXFUIZEW8GYPZLBVW15T1VB7BCJC5DL1UR71H5FDXTORFG62M3GFEB9RAWJHNS23V1E7U4ASMGZRS8603KFRDXYFY8YPLT1S8JWWQPW6J1VYPRKZBL0YE5J8J1EGCJRX26HP8UOMNRXUW3EWLO4L3D3CS9BIMWH4WMHYHEADJ8HM661ZNOGW4WX1070E9VO3QXXON5OT9JQ4BX6HVUFO5YMS1NG9195O8JOLWQ2IE4JR42OV4UBHVN199QIWSSYZHDE0GR043ROOGBTTQR5G11D6JZRSD3G66OHYMO9K1SKP8810P1QGF20KDDL0QPALG451MXXTETJ9CC5HFMNYUJPFN94CS2DR2TDNTSC2WYLGR068XRA238G2WFC8HJVD99VOJF5NUML0GJGKP6RL2TJW50ZRUDA7TD9S50B1ZQ5DF5MUTY11MTHHYNHOWRD92ES84IZWNKMLQZKNTMTFQQ6IDA6COQ5OT4MYTUN0OT8FG1GPV3LLCXQV2C4UBAYRHMMLRW3CHKX8O8RUKPNXGOJM5M5VVAEWYGFBGCDZZCE0LG83361H26AXBBWTKKCRNJJ3FVS8L17JADWO5NM17JY5S0SOJRY2K76G8ZZHAYN31P0WZNZY0QSSBS041IOR78BNH6SCMVTP1Y79D5JAKOCH51IIAC9WLQRHTRFJLUJ1JMCELHHQ16D23L2TKMOPAA4KV1CXSHHEV2K5HFRNKZ0IIP6O7P8KEUP0D68MBO2TBIAGE542EOMX807FJSH1JTOP1") },
        };
    }

    internal class IsfDecoder
    {
        byte[]      m_secret;

        public IsfDecoder (byte[] secret)
        {
            m_secret = secret;
        }

        public void Decode (byte[] data)
        {
            var key_string = CreateKeyString();
            int n = 0;
            for (int i = 0; i < data.Length; )
            {
                DecodePrepare (n++, key_string);
                for (int j = 0; j < key_string.Length && i < data.Length; )
                {
                    data[i++] ^= key_string[j++];
                }
            }
        }

        private byte[] CreateKeyString ()
        {
            byte[] len_str = new byte[2];
            for (int i = 0; i < 2; i++)
                len_str[i] = EncodeHex ((byte)(Chr2HexCode (m_secret[0x500 + i]) - Chr2HexCode (m_secret[0x100 + i])));

            byte[] key_string = new byte[Str2Hex (len_str)];
            for (int i = 0; i < key_string.Length; i++)
                key_string[i] = EncodeHex ((byte)(Chr2HexCode (m_secret[0x510 + i]) - Chr2HexCode (m_secret[0x110 + i])));
            return key_string;
        }

        private void DecodePrepare (int index, byte[] key_string)
        {
            int p = (index & 0x3f) * 16; // index within SecretTable
            for (int i = 0; i < key_string.Length; i++)
                key_string[i] = EncodeHex ((byte)(Chr2HexCode (key_string[i]) + Chr2HexCode (m_secret[p+i])));
        }

        private static byte EncodeHex (byte symbol)
        {
            if (symbol < 0x80)
                return HexEncodeMap[symbol % 36];
            symbol = (byte)(-(sbyte)symbol % 36);
            if (0 == symbol)
                return HexEncodeMap[0];
            return HexEncodeMap[36 - symbol];
        }

        private static byte Chr2HexCode (byte chr)
        {
            return HexTable[Chr2Hex (chr)];
        }

        private static byte Chr2Hex (byte chr)
        {
            byte code;
            if (chr >= '0' && chr <= '9')
                code = (byte)(chr - '0');
            else if (chr >= 'a' && chr <= 'z')
                code = (byte)(chr - 'a' + 10);
            else if (chr >= 'A' && chr <= 'Z')
                code = (byte)(chr - 'A' + 10);
            else
                code = 0;
            return code;
        }

        private static int Str2Hex (byte[] shex)
        {
            int idec = 0;
            for (int i = 0; i < shex.Length; ++i)
            {
                int mid = Chr2Hex (shex[i]);
                mid <<= ((shex.Length - i - 1) << 2);
                idec |= mid;
            }
            return idec;
        }

        static readonly byte[] HexEncodeMap = Encoding.ASCII.GetBytes("G5FXIL094MPRKWCJ3OEBVA7HQ2SU8Y6TZ1ND");
        static readonly byte[] HexTable = new byte[] {
            0x06, 0x21, 0x19, 0x10, 0x08, 0x01, 0x1E, 0x16, 0x1C, 0x07, 0x15, 0x13, 0x0E, 0x23, 0x12, 0x02,
            0x00, 0x17, 0x04, 0x0F, 0x0C, 0x05, 0x09, 0x22, 0x11, 0x0A, 0x18, 0x0B, 0x1A, 0x1F, 0x1B, 0x14,
            0x0D, 0x03, 0x1D, 0x20,
        };
    }
}
