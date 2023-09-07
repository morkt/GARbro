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

using GameRes.Utility;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GameRes.Formats.Macromedia
{
    [Export(typeof(ArchiveFormat))]
    public class DxrOpener : ArchiveFormat
    {
        public override string         Tag { get => "DXR"; }
        public override string Description { get => "Macromedia Director resource archive"; }
        public override uint     Signature { get => 0x52494658; } // 'XFIR'
        public override bool  IsHierarchic { get => false; }
        public override bool      CanWrite { get => false; }

        public DxrOpener ()
        {
            Extensions = new[] { "dxr", "cxt" };
            Signatures = new[] { 0x52494658u, 0x58464952u };
        }

        internal static readonly HashSet<string> RawChunks = new HashSet<string> {
            "RTE0", "RTE1", "FXmp", "VWFI", "VWSC", "Lscr", "STXT", "XMED", "snd "
        };

        internal bool ConvertText = true;

        public override ArcFile TryOpen (ArcView file)
        {
            using (var input = file.CreateStream())
            {
                ByteOrder ord = input.Signature == 0x52494658u ? ByteOrder.LittleEndian : ByteOrder.BigEndian;
                var reader = new Reader (input, ord);
                reader.Position = 4;
                uint length = reader.ReadU32();
                var context = new SerializationContext();
                var dir_file = new DirectorFile();
                if (!dir_file.Deserialize (context, reader))
                    return null;

                var dir = new List<Entry> ();
                ImportMedia (dir_file, dir);
                foreach (MemoryMapEntry entry in dir_file.MMap.Dir)
                {
                    if (entry.Size != 0 && RawChunks.Contains (entry.FourCC))
                    {
                        entry.Name = string.Format ("{0:D6}.{1}", entry.Id, entry.FourCC.Trim());
                        if ("snd " == entry.FourCC)
                            entry.Type = "audio";
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
            var ment = entry as MemoryMapEntry;
            if (!ConvertText || null == ment || ment.FourCC != "STXT")
                return base.OpenEntry (arc, entry);
            uint offset = Binary.BigEndian (arc.File.View.ReadUInt32 (entry.Offset));
            uint length = Binary.BigEndian (arc.File.View.ReadUInt32 (entry.Offset + 4));
            return arc.File.CreateStream (entry.Offset + offset, length);
        }

        internal Stream OpenSound (ArcFile arc, SoundEntry entry)
        {
            if (null == entry.Header)
                return base.OpenEntry (arc, entry);
            var header = arc.File.View.ReadBytes (entry.Header.Offset, entry.Header.Size);
            var format = entry.DeserializeHeader (header);
            var riff = new MemoryStream (0x2C);
            WaveAudio.WriteRiffHeader (riff, format, entry.Size);
            if (format.BitsPerSample < 16)
            {
                using (riff)
                {
                    var input = arc.File.CreateStream (entry.Offset, entry.Size);
                    return new PrefixStream (riff.ToArray(), input);
                }
            }
            // samples are stored in big-endian format
            var samples = arc.File.View.ReadBytes (entry.Offset, entry.Size);
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
            var mmap = dir_file.MMap;
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
                    if (entry != null)
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
                    var ediM = dir_file.MMap[elem.Id];
                    if (string.IsNullOrEmpty (name))
                        name = ediM.Id.ToString ("D6");
                    return new Entry
                    {
                        Name   = name + ".mp3",
                        Type   = "audio",
                        Offset = ediM.Offset,
                        Size   = ediM.Size,
                    };
                }
                if (null == sndHrec && "sndH" == elem.FourCC)
                    sndHrec = elem;
                else if (null == sndSrec && "sndS" == elem.FourCC)
                    sndSrec = elem;
            }
            if (sndHrec == null || sndSrec == null)
                return null;
            var sndH = dir_file.MMap[sndHrec.Id];
            var sndS = dir_file.MMap[sndSrec.Id];
            if (string.IsNullOrEmpty (name))
                name = sndSrec.Id.ToString ("D6");
            return new SoundEntry
            {
                Name   = name + ".snd",
                Type   = "audio",
                Offset = sndS.Offset,
                Size   = sndS.Size,
                Header = sndH,
            };
        }

        Entry ImportBitmap (CastMember bitmap, DirectorFile dir_file, Cast cast)
        {
//            var bitd = dir_file.KeyTable.FindByCast (bitmap.Id, "BITD");
            KeyTableEntry bitd = null, alfa = null;
            foreach (var elem in dir_file.KeyTable.Table.Where (e => e.CastId == bitmap.Id))
            {
                if (null == bitd && "BITD" == elem.FourCC)
                    bitd = elem;
                else if (null == alfa && "ALFA" == elem.FourCC)
                    alfa = elem;
            }
            if (bitd == null)
                return null;
            var entry = new BitmapEntry();
            entry.DeserializeHeader (bitmap.SpecificData);
            var name = bitmap.Info.Name;
            if (string.IsNullOrEmpty (name))
                name = bitd.Id.ToString ("D6");
            var chunk = dir_file.MMap[bitd.Id];
            entry.Name   = name + ".BITD";
            entry.Type   = "image";
            entry.Offset = chunk.Offset;
            entry.Size   = chunk.Size;
            if (entry.Palette > 0)
            {
                var cast_id = cast.Index[entry.Palette-1];
                var clut = dir_file.KeyTable.FindByCast (cast_id, "CLUT");
                if (clut != null)
                    entry.PaletteRef = dir_file.MMap[clut.Id];
            }
            if (alfa != null)
                entry.AlphaRef = dir_file.MMap[alfa.Id];
            return entry;
        }

        public override IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            var bent = entry as BitmapEntry;
            if (null == bent)
                return base.OpenImage (arc, entry);
            BitmapPalette palette = null;
            if (bent.PaletteRef != null)
            {
                var pal_bytes = arc.File.View.ReadBytes (bent.PaletteRef.Offset, bent.PaletteRef.Size);
                palette = ReadPalette (pal_bytes);
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
            var info = new ImageMetaData {
                Width = (uint)(bent.Right - bent.Left),
                Height = (uint)(bent.Bottom - bent.Top),
                BPP = bent.BitDepth
            };
            byte[] alpha_channel = null;
            if (bent.AlphaRef != null)
            {
                using (var alpha = arc.File.CreateStream (bent.AlphaRef.Offset, bent.AlphaRef.Size))
                {
                    var alpha_info = new ImageMetaData {
                        Width = info.Width,
                        Height = info.Height,
                        BPP = 8,
                    };
                    var decoder = new BitdDecoder (alpha, alpha_info, null);
                    alpha_channel = decoder.Unpack8bpp();
                }
            }
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            return new BitdDecoder (input.AsStream, info, palette) { AlphaChannel = alpha_channel };
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
    }

    internal class BitmapEntry : Entry
    {
        public byte Flags;
        public byte DepthType;
        public int  Top;
        public int  Left;
        public int  Bottom;
        public int  Right;
        public int  BitDepth;
        public int  Palette;
        public Entry PaletteRef;
        public Entry AlphaRef;

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
                reader.Skip (0x0C);
                BitDepth = reader.ReadU16() & 0xFF; // ???
                reader.Skip (2);
                Palette = reader.ReadI16();
            }
        }
    }

    internal class SoundEntry : Entry
    {
        public Entry    Header;

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
