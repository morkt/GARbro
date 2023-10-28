//! \file       ArcDXR.cs
//! \date       2023 Aug 17
//! \brief      Macromedia Director presentation container.
//
// Copyright (C) 2023 by morkt
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

using GameRes.Compression;
using GameRes.Utility;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GameRes.Formats.Macromedia
{
    [Export(typeof(ArchiveFormat))]
    public class DxrOpener : ArchiveFormat
    {
        public override string         Tag => "DXR";
        public override string Description => "Macromedia Director resource archive";
        public override uint     Signature => SignatureXFIR; // 'XFIR'
        public override bool  IsHierarchic => false;
        public override bool      CanWrite => false;

        public const uint SignatureXFIR = 0x52494658u;
        public const uint SignatureRIFX = 0x58464952u;

        public DxrOpener ()
        {
            Extensions = new[] { "dxr", "cxt", "cct", "dcr", "dir", "exe" };
            Signatures = new[] { SignatureXFIR, SignatureRIFX, 0x00905A4Du, 0u };
        }

        internal static readonly HashSet<string> RawChunks = new HashSet<string> {
            "RTE0", "RTE1", "FXmp", "VWFI", "VWSC", "Lscr", "STXT", "XMED", "File"
        };

        internal bool ConvertText = true;

        public override ArcFile TryOpen (ArcView file)
        {
            long base_offset = 0;
            if (file.View.AsciiEqual (0, "MZ"))
                base_offset = LookForXfir (file);
            uint signature = file.View.ReadUInt32 (base_offset);
            if (signature != SignatureXFIR && signature != SignatureRIFX)
                return null;
            using (var input = file.CreateStream())
            {
                ByteOrder ord = signature == SignatureXFIR ? ByteOrder.LittleEndian : ByteOrder.BigEndian;
                var reader = new Reader (input, ord);
                reader.Position = base_offset;
                var context = new SerializationContext();
                var dir_file = new DirectorFile();
                if (!dir_file.Deserialize (context, reader))
                    return null;

                var dir = new List<Entry> ();
                if (dir_file.Codec != "APPL")
                    ImportMedia (dir_file, dir);
                foreach (DirectorEntry entry in dir_file.Directory)
                {
                    if (entry.Size != 0 && entry.Offset != -1 && RawChunks.Contains (entry.FourCC))
                    {
                        entry.Name = string.Format ("{0:D6}.{1}", entry.Id, entry.FourCC.Trim());
                        if ("File" == entry.FourCC)
                        {
                            entry.Offset -= 8;
                            entry.Size   += 8;
                        }
                        dir.Add (entry);
                    }
                }
                return new ArcFile (file, this, dir);
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var snd = entry as SoundEntry;
            if (snd != null)
                return OpenSound (arc, snd);
            var pent = entry as PackedEntry;
            if (null == pent)
                return base.OpenEntry (arc, entry);
            var input = OpenChunkStream (arc.File, pent);
            var ment = entry as DirectorEntry;
            if (null == ment || !ConvertText || ment.FourCC != "STXT")
                return input.AsStream;
            using (input)
            {
                uint offset = Binary.BigEndian (input.ReadUInt32());
                uint length = Binary.BigEndian (input.ReadUInt32());
                input.Position = offset;
                var text = input.ReadBytes ((int)length);
                return new BinMemoryStream (text, entry.Name);
            }
        }

        internal Stream OpenSound (ArcFile arc, SoundEntry entry)
        {
            if (null == entry.Header)
                return base.OpenEntry (arc, entry);
            var header = new byte[entry.Header.UnpackedSize];
            using (var input = OpenChunkStream (arc.File, entry.Header))
                input.Read (header, 0, header.Length);
            var format = entry.DeserializeHeader (header);
            var riff = new MemoryStream (0x2C);
            WaveAudio.WriteRiffHeader (riff, format, entry.Size);
            if (format.BitsPerSample < 16)
            {
                using (riff)
                {
                    var input = OpenChunkStream (arc.File, entry).AsStream;
                    return new PrefixStream (riff.ToArray(), input);
                }
            }
            // samples are stored in big-endian format
            var samples = new byte[entry.UnpackedSize];
            using (var input = OpenChunkStream (arc.File, entry))
                input.Read (samples, 0, samples.Length);
            for (int i = 1; i < samples.Length; i += 2)
            {
                byte s = samples[i-1];
                samples[i-1] = samples[i];
                samples[i] = s;
            }
            riff.Write (samples, 0, samples.Length);
            riff.Position = 0;
            return riff;
        }

        void ImportMedia (DirectorFile dir_file, List<Entry> dir)
        {
            var seen_ids = new HashSet<int>();
            foreach (var cast in dir_file.Casts)
            {
                foreach (var piece in cast.Members.Values)
                {
                    if (seen_ids.Contains (piece.Id))
                        continue;
                    seen_ids.Add (piece.Id);
                    Entry entry = null;
                    if (piece.Type == DataType.Bitmap)
                        entry = ImportBitmap (piece, dir_file, cast);
                    else if (piece.Type == DataType.Sound)
                        entry = ImportSound (piece, dir_file);
                    if (entry != null && entry.Size > 0)
                        dir.Add (entry);
                }
            }
        }

        Entry ImportSound (CastMember sound, DirectorFile dir_file)
        {
            var name = sound.Info.Name;
            KeyTableEntry sndHrec = null, sndSrec = null;
            foreach (var elem in dir_file.KeyTable.Table.Where (e => e.CastId == sound.Id))
            {
                if ("ediM" == elem.FourCC)
                {
                    var ediM = dir_file.Index[elem.Id];
                    name = SanitizeName(name, ediM.Id);
                    return new PackedEntry
                    {
                        Name = name + ".ediM",
                        Type = "audio",
                        Offset       = ediM.Offset,
                        Size         = ediM.Size,
                        UnpackedSize = ediM.UnpackedSize,
                        IsPacked     = ediM.IsPacked
                    };
                }
                else if ("snd " == elem.FourCC)
                {
                    var snd = dir_file.Index[elem.Id];
                    if (snd.Size != 0)
                    {
                        name = SanitizeName (name, snd.Id);
                        return new PackedEntry
                        {
                            Name = name + ".snd",
                            Type = "audio",
                            Offset = snd.Offset,
                            Size = snd.Size,
                            UnpackedSize = snd.Size,
                            IsPacked = false,
                        };
                    }
                }
                if (null == sndHrec && "sndH" == elem.FourCC)
                    sndHrec = elem;
                else if (null == sndSrec && "sndS" == elem.FourCC)
                    sndSrec = elem;
            }
            if (sndHrec == null || sndSrec == null)
                return null;
            var sndH = dir_file.Index[sndHrec.Id];
            var sndS = dir_file.Index[sndSrec.Id];
            name = SanitizeName (name, sndSrec.Id);
            return new SoundEntry
            {
                Name   = name + ".snd",
                Type   = "audio",
                Offset = sndS.Offset,
                Size   = sndS.Size,
                UnpackedSize = sndS.UnpackedSize,
                IsPacked = sndS.IsPacked,
                Header = sndH,
            };
        }

        Entry ImportBitmap (CastMember bitmap, DirectorFile dir_file, Cast cast)
        {
            KeyTableEntry bitd = null, edim = null, alfa = null;
            foreach (var elem in dir_file.KeyTable.Table.Where (e => e.CastId == bitmap.Id))
            {
                if (null == bitd && "BITD" == elem.FourCC)
                    bitd = elem;
                else if (null == edim && "ediM" == elem.FourCC)
                    edim = elem;
                else if (null == alfa && "ALFA" == elem.FourCC)
                    alfa = elem;
            }
            if (bitd == null && edim == null)
                return null;
            var entry = new BitmapEntry();
            if (bitd != null)
            {
                entry.DeserializeHeader (bitmap.SpecificData);
                var name = SanitizeName (bitmap.Info.Name, bitd.Id);
                var chunk = dir_file.Index[bitd.Id];
                entry.Name   = name + ".BITD";
                entry.Type   = "image";
                entry.Offset = chunk.Offset;
                entry.Size   = chunk.Size;
                entry.IsPacked = chunk.IsPacked;
                entry.UnpackedSize = chunk.UnpackedSize;
                if (entry.Palette > 0)
                {
                    var cast_id = cast.Index[entry.Palette-1];
                    var clut = dir_file.KeyTable.FindByCast (cast_id, "CLUT");
                    if (clut != null)
                        entry.PaletteRef = dir_file.Index[clut.Id];
                }
            }
            else // if (edim != null)
            {
                var name = SanitizeName (bitmap.Info.Name, edim.Id);
                var chunk = dir_file.Index[edim.Id];
                entry.Name   = name + ".jpg";
                entry.Type   = "image";
                entry.Offset = chunk.Offset;
                entry.Size   = chunk.Size;
                entry.IsPacked = false;
                entry.UnpackedSize = entry.Size;
            }
            if (alfa != null)
                entry.AlphaRef = dir_file.Index[alfa.Id];
            return entry;
        }

        static readonly Regex ForbiddenCharsRe = new Regex (@"[:?*<>/\\]");

        string SanitizeName (string name, int id)
        {
            name = name?.Trim();
            if (string.IsNullOrEmpty (name))
                name = id.ToString ("D6");
            else
                name = ForbiddenCharsRe.Replace (name, "_");
            return name;
        }

        public override IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            var bent = entry as BitmapEntry;
            if (null == bent)
                return base.OpenImage(arc, entry);
            if (entry.Name.HasExtension (".jpg"))
                return OpenJpeg (arc, bent);

            BitmapPalette palette = null;
            if (bent.PaletteRef != null)
            {
                using (var pal = OpenChunkStream (arc.File, bent.PaletteRef))
                {
                    var pal_bytes = pal.ReadBytes ((int)bent.PaletteRef.UnpackedSize);
                    palette = ReadPalette (pal_bytes);
                }
            }
            else if (bent.BitDepth <= 8)
            {
                switch (bent.Palette)
                {
                case 0:     palette = Palettes.SystemMac; break;
                case -1:    palette = Palettes.Rainbow; break;
                case -2:    palette = Palettes.Grayscale; break;
                case -100:  palette = Palettes.WindowsDirector4; break;
                default:
                case -101:  palette = Palettes.SystemWindows; break;
                }
            }
            var info = new BitdMetaData {
                Width = (uint)(bent.Right - bent.Left),
                Height = (uint)(bent.Bottom - bent.Top),
                BPP = bent.BitDepth,
                DepthType = bent.DepthType,
            };
            byte[] alpha_channel = null;
            if (bent.AlphaRef != null)
                alpha_channel = ReadAlphaChannel (arc.File, bent.AlphaRef, info);
            var input = OpenChunkStream (arc.File, bent).AsStream;
            return new BitdDecoder (input, info, palette) { AlphaChannel = alpha_channel };
        }

        IImageDecoder OpenJpeg (ArcFile arc, BitmapEntry entry)
        {
            if (null == entry.AlphaRef)
                return base.OpenImage (arc, entry);
            // jpeg with alpha-channel
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            try
            {
                var info = ImageFormat.Jpeg.ReadMetaData (input);
                if (null == info)
                    throw new InvalidFormatException ("Invalid 'ediM' chunk.");
                var alpha_channel = ReadAlphaChannel (arc.File, entry.AlphaRef, info);
                return BitdDecoder.FromJpeg (input, info, alpha_channel);
            }
            catch
            {
                input.Dispose();
                throw;
            }
        }

        byte[] ReadAlphaChannel (ArcView file, DirectorEntry entry, ImageMetaData info)
        {
            using (var alpha = OpenChunkStream (file, entry))
            {
                var alpha_info = new BitdMetaData {
                    Width = info.Width,
                    Height = info.Height,
                    BPP = 8,
                    DepthType = 0x80,
                };
                var decoder = new BitdDecoder (alpha.AsStream, alpha_info, null);
                return decoder.Unpack8bpp();
            }
        }

        BitmapPalette ReadPalette (byte[] data)
        {
            int num_colors = data.Length / 6;
            var colors = new Color[num_colors];
            for (int i = 0; i < data.Length; i += 6)
            {
                colors[i/6] = Color.FromRgb (data[i], data[i+2], data[i+4]);
            }
            return new BitmapPalette (colors);
        }

        IBinaryStream OpenChunkStream (ArcView file, PackedEntry entry)
        {
            var input = file.CreateStream (entry.Offset, entry.Size);
            if (!entry.IsPacked)
                return input;
            var data = new byte[entry.UnpackedSize];
            using (var zstream = new ZLibStream (input, CompressionMode.Decompress))
                zstream.Read (data, 0, data.Length);
            return new BinMemoryStream (data, entry.Name);
        }

        static readonly byte[] s_xfir = { (byte)'X', (byte)'F', (byte)'I', (byte)'R' };

        long LookForXfir (ArcView file)
        {
            var exe = new ExeFile (file);
            long pos;
            if (exe.IsWin16)
            {
                pos = exe.FindString (exe.Overlay, s_xfir);
                if (pos < 0)
                    return 0;
            }
            else
            {
                pos = exe.Overlay.Offset;
                if (pos >= file.MaxOffset)
                    return 0;
                if (file.View.AsciiEqual (pos, "10JP") || file.View.AsciiEqual (pos, "59JP"))
                {
                    pos = file.View.ReadUInt32 (pos+4);
                }
            }
            if (pos >= file.MaxOffset || !file.View.AsciiEqual (pos, "XFIR"))
                return 0;
            // TODO threat 'LPPA' archives the normal way, like archives that contain entries.
            // the problem is, DXR archives contained within 'LPPA' have their offsets relative to executable file,
            // so have to figure out way to handle it.
            if (!file.View.AsciiEqual (pos+8, "LPPA"))
                return pos;
            var appl = new DirectorFile();
            var context = new SerializationContext();
            using (var input = file.CreateStream())
            {
                var reader = new Reader (input, ByteOrder.LittleEndian);
                input.Position = pos + 12;
                if (!appl.ReadMMap (context, reader))
                    return 0;
                foreach (var entry in appl.Directory)
                {
                    // only the first XFIR entry is matched here, but archive may contain multiple sub-archives.
                    if (entry.FourCC == "File")
                    {
                        if (file.View.AsciiEqual (entry.Offset-8, "XFIR")
                            && !file.View.AsciiEqual (entry.Offset, "artX"))
                            return entry.Offset-8;
                    }
                }
                return 0;
            }
        }
    }

    internal class BitmapEntry : PackedEntry
    {
        public byte Flags;
        public byte DepthType;
        public int  Top;
        public int  Left;
        public int  Bottom;
        public int  Right;
        public int  BitDepth;
        public int  Palette;
        public DirectorEntry PaletteRef;
        public DirectorEntry AlphaRef;

        public void DeserializeHeader (byte[] data)
        {
            using (var input = new MemoryStream (data, false))
            {
                var reader = new Reader (input, ByteOrder.BigEndian);
                DepthType = reader.ReadU8();
                Flags  = reader.ReadU8();
                Top    = reader.ReadI16();
                Left   = reader.ReadI16();
                Bottom = reader.ReadI16();
                Right  = reader.ReadI16();
                if (data.Length > 0x16)
                {
                    reader.Skip (0x0C);
                    BitDepth = reader.ReadU16() & 0xFF; // ???
                    if (data.Length >= 0x1C)
                    {
                        reader.Skip (2);
                        Palette = reader.ReadI16();
                    }
                }
            }
        }
    }

    internal class SoundEntry : PackedEntry
    {
        public DirectorEntry    Header;

        public WaveFormat DeserializeHeader (byte[] header)
        {
            // pure guesswork
            return new WaveFormat {
                FormatTag             = 1,
                Channels              = (ushort)BigEndian.ToUInt32 (header, 0x4C),
                SamplesPerSecond      = BigEndian.ToUInt32 (header, 0x2C),
                AverageBytesPerSecond = BigEndian.ToUInt32 (header, 0x30),
                BlockAlign            = (ushort)BigEndian.ToUInt32 (header, 0x50),
                BitsPerSample         = (ushort)BigEndian.ToUInt32 (header, 0x44),
            };
        }
    }
}
