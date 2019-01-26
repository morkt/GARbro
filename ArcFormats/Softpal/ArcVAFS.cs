//! \file       ArcVAFS.cs
//! \date       Sun Jan 10 04:19:47 2016
//! \brief      Softpal engine resource archive.
//
// Copyright (C) 2016 by morkt
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
using System.ComponentModel.Composition;
using System.IO;
using System.Text;
using GameRes.Utility;

namespace GameRes.Formats.Softpal
{
    [Export(typeof(ArchiveFormat))]
    public class VafsOpener : ArchiveFormat
    {
        public override string         Tag { get { return "VAFS"; } }
        public override string Description { get { return "Softpal engine resource archive"; } }
        public override uint     Signature { get { return 0x53464156; } } // 'VAFS'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public VafsOpener ()
        {
            Extensions = new string[] { "052", "054", "055", "056", "058" };
        }

        static readonly ResourceInstance<ImageFormat> s_PicFormat = new ResourceInstance<ImageFormat> ("PIC/SOFTPAL");

        public override ArcFile TryOpen (ArcView file)
        {
            if ('H' != file.View.ReadByte (4))
                return null;
            uint index_offset = 0x10;
            uint data_offset = file.View.ReadUInt32 (index_offset);
            var base_name = Path.GetFileNameWithoutExtension (file.Name).ToUpperInvariant();
            if (0 == data_offset && "TP" == base_name)
            {
                var ext = Path.GetExtension (file.Name).TrimStart ('.');
                int version;
                if (int.TryParse (ext, out version) && version >= 54)
                    return OpenTp055Arc (file);
                else
                    return OpenTpArc (file);
            }
            if (data_offset < index_offset || data_offset >= file.MaxOffset)
                return null;
            int count = (int)(data_offset - index_offset) / 4;
            if (!IsSaneCount (count))
                return null;

            uint next_offset = data_offset;
            bool is_bgm = "BGM" == base_name;
            bool is_pic = "PIC" == base_name;
            var dir = new List<Entry> (count);
            for (int i = 0; next_offset != file.MaxOffset && i < count; ++i)
            {
                index_offset += 4;
                var name = string.Format("{0}#{1:D5}", base_name, i);
                var offset = next_offset;
                next_offset = index_offset == data_offset ? 0 : file.View.ReadUInt32 (index_offset);
                if (uint.MaxValue == next_offset || next_offset < offset)
                    break;
                uint size = next_offset - offset;
                if (size < 4)
                    continue;
                Entry entry;
                if (is_pic)
                    entry = new Entry { Name = name, Type = "image" };
                else if (is_bgm)
                    entry = new Entry { Name = name + ".wav", Type = "audio" };
                else
                    entry = new AutoEntry (name, () => {
                        uint signature = file.View.ReadUInt32 (offset);
                        uint s16 = signature & 0xFFFF;
                        if (1 == s16 || 3 == s16 || 4 == s16)
                            return s_PicFormat.Value;
                        if (size > 0x200 && (size >> 9) == (signature >> 9))
                            return AudioFormat.Wav;
                        return AutoEntry.DetectFileType (signature);
                    });

                entry.Offset = offset;
                entry.Size = size;
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
            }
            if (0 == dir.Count)
                return null;
            return new ArcFile (file, this, dir);
        }

        ArcFile OpenTpArc (ArcView file)
        {
            uint index_offset = 0x20;
            uint data_offset;
            for (;;)
            {
                data_offset = file.View.ReadUInt32 (index_offset);
                if (0 != data_offset)
                    break;
                index_offset += 0x10;
                if (0xA010 == index_offset)
                    return null;
            }
            if (data_offset >= file.MaxOffset)
                return null;
            var dir = new List<Entry>();
            while (index_offset < data_offset)
            {
                var offset = file.View.ReadUInt32 (index_offset);
                if (0 != offset)
                {
                    var name = string.Format("TP#{0:D5}.wav", index_offset/0x10 - 1);
                    var entry = new Entry {
                        Name = name,
                        Type = "audio",
                        Offset = offset,
                        Size = 0x402 * file.View.ReadUInt32 (index_offset+4),
                    };
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);
                }
                index_offset += 0x10;
            }
            return new TpArchive (file, this, dir);
        }

        ArcFile OpenTp055Arc (ArcView file)
        {
            uint index_offset = 0x20;
            uint data_offset;
            for (;;)
            {
                data_offset = file.View.ReadUInt32 (index_offset);
                if (0 != data_offset)
                    break;
                index_offset += 0x10;
                if (0xA010 == index_offset)
                    return null;
            }
            if (data_offset >= file.MaxOffset)
                return null;
            var dir = new List<Entry>();
            while (index_offset < data_offset)
            {
                var offset = file.View.ReadUInt32 (index_offset);
                if (0 != offset)
                {
                    if (offset >= file.MaxOffset)
                        return null;
                    var name = string.Format("TP#{0:D6}.wav", index_offset/0x10 - 1);
                    var entry = new Tp055Entry {
                        Name = name,
                        Offset = offset,
                        ChunkCount = file.View.ReadInt32 (index_offset+4),
                    };
                    dir.Add (entry);
                }
                index_offset += 0x10;
            }
            for (int i = 0; i < dir.Count-1; ++i)
            {
                dir[i].Size = (uint)(dir[i+1].Offset - dir[i].Offset);
            }
            dir[dir.Count-1].Size = (uint)(file.MaxOffset - dir[dir.Count-1].Offset);
            return new TpArchive (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            if (entry is Tp055Entry)
                return OpenVoice055Entry (arc, entry as Tp055Entry);
            else if (arc is TpArchive)
                return OpenVoiceEntry (arc, entry);
            else if ("audio" == entry.Type
                     && AudioFormat.Wav.Signature != arc.File.View.ReadUInt32 (entry.Offset))
                return OpenAudioEntry (arc, entry);
            else
                return base.OpenEntry (arc, entry);
        }

        Stream OpenAudioEntry (ArcFile arc, Entry entry)
        {
            var offset = entry.Offset;
            int size = arc.File.View.ReadInt32 (offset);
            offset += 4;
            int chunk_count = size >> 9;
            int pcm_size = chunk_count * 0x400;
            var output = new MemoryStream (0x24 + pcm_size);
            using (var wav = new BinaryWriter (output, Encoding.ASCII, true))
            {
                WriteWavHeader (wav, 2, 22050, pcm_size);
                var buffer = new byte[0x204];
                for (int chunk = 0; chunk < chunk_count; ++chunk)
                {
                    arc.File.View.Read (offset, buffer, 0, 0x204);
                    offset += 0x204;
                    int src = 0;
                    var data_offset = wav.BaseStream.Position;
                    for (int channel = 0; channel < 2; ++channel)
                    {
                        int addend = buffer[src++] << 8;
                        int pcm = LittleEndian.ToInt16 (buffer, src);
                        src += 2;
                        wav.BaseStream.Position = data_offset + channel * 2;
                        wav.Write ((short)pcm);
                        for (int i = 0; i < 255; ++i)
                        {
                            byte v = buffer[src++];
                            int diff = v + addend;
                            pcm += WaveTable1.Value[diff]; 
                            if (pcm > 32767)
                                pcm = 32767;
                            else if (pcm < -32767)
                                pcm = -32767;
                            wav.BaseStream.Seek (2, SeekOrigin.Current);
                            wav.Write ((short)pcm);
                            addend += WaveTable2.Value[v];
                            if (addend < 0)
                                addend = 0;
                            else if (addend >= 16384)
                                addend = 16128;
                        }
                    }
                }
            }
            output.Position = 0;
            return output;
        }

        Stream OpenVoiceEntry (ArcFile arc, Entry entry)
        {
            int remaining = (int)entry.Size;
            int chunk_count = remaining / 0x402;
            int pcm_size = chunk_count * 0x800;
            var output = new MemoryStream (0x24 + pcm_size);
            var offset = entry.Offset;
            using (var wav = new BinaryWriter (output, Encoding.ASCII, true))
            {
                WriteWavHeader (wav, 1, 22050, pcm_size);
                var buffer = new byte[0x402];
                while (remaining > 0)
                {
                    arc.File.View.Read (offset, buffer, 0, 0x402);
                    offset += 0x402;
                    remaining -= 0x402;
                    int pcm = LittleEndian.ToInt16 (buffer, 1);
                    int addend = buffer[0] << 8;
                    wav.Write ((short)pcm);
                    for (int src = 3; src < buffer.Length; ++src)
                    {
                        byte v = buffer[src];
                        int diff = v + addend;
                        pcm += WaveTable1.Value[diff];
                        if (pcm > 32767)
                            pcm = 32767;
                        else if (pcm < -32767)
                            pcm = -32767;
                        wav.Write ((short)pcm);
                        addend += WaveTable2.Value[v];
                        if (addend < 0)
                            addend = 0;
                        else if (addend >= 16384)
                            addend = 16128;
                    }
                }
            }
            output.Position = 0;
            return output;
        }

        Stream OpenVoice055Entry (ArcFile arc, Tp055Entry entry)
        {
            int pcm_size = entry.ChunkCount * 0x800;
            var offset = entry.Offset;
            var wav_mem = new MemoryStream (0x24 + pcm_size);
            using (var wav = new BinaryWriter (wav_mem, Encoding.ASCII, true))
            {
                WriteWavHeader (wav, 1, 22050, pcm_size);
                var buffer = new byte[0x402];
                var output = new int[0x400];
                for (int i = 0; i < entry.ChunkCount; ++i)
                {
                    int ctl = arc.File.View.ReadByte (offset++) & 0x3F;
                    for (int j = 0; j < output.Length; ++j)
                        output[j] = 0;
                    for (; ctl != 0; ctl >>= 1)
                    {
                        if (0 == (ctl & 1))
                            continue;
                        arc.File.View.Read (offset, buffer, 0, 0x402);
                        offset += 0x402;
                        int pcm = LittleEndian.ToInt16 (buffer, 1);
                        int dst = 0;
                        int addend = buffer[0] << 8;
                        output[dst++] += pcm;
                        for (int src = 3; src < buffer.Length; ++src)
                        {
                            byte v = buffer[src];
                            int diff = v + addend;
                            pcm += WaveTable1.Value[diff];
                            output[dst++] += (short)pcm;
                            addend += WaveTable2.Value[v];
                            if (addend < 0)
                                addend = 0;
                            else if (addend >= 16384)
                                addend = 16128;
                        }
                    }
                    for (int j = 0; j < output.Length; ++j)
                    {
                        int pcm = output[j];
                        if (pcm > 32767)
                            pcm = 32767;
                        else if (pcm < -32767)
                            pcm = -32767;
                        wav.Write ((short)pcm);
                    }
                }
            }
            wav_mem.Position = 0;
            return wav_mem;
        }

        void WriteWavHeader (BinaryWriter wav, short channels, int freq, int pcm_size)
        {
            wav.Write ("RIFF".ToCharArray());
            wav.Write (0x24 + pcm_size);
            wav.Write ("WAVE".ToCharArray());
            wav.Write ("fmt ".ToCharArray());
            wav.Write (0x10);
            wav.Write ((ushort)1);
            wav.Write ((ushort)channels);
            wav.Write (freq);
            wav.Write (freq*channels*2);
            wav.Write ((ushort)(channels*2));
            wav.Write ((ushort)16);
            wav.Write ("data".ToCharArray());
            wav.Write (pcm_size);
        }

        static readonly Lazy<short[]> WaveTable1 = new Lazy<short[]> (() => LoadWaveTable ("WaveTable1"));
        static readonly Lazy<short[]> WaveTable2 = new Lazy<short[]> (() => LoadWaveTable ("WaveTable2"));

        static short[] LoadWaveTable (string name)
        {
            var src = EmbeddedResource.Load (name, typeof(VafsOpener));
            if (null == src)
                return null;
            var array = new short[src.Length/2];
            Buffer.BlockCopy (src, 0, array, 0, src.Length);
            return array;
        }
    }

    internal class TpArchive : ArcFile
    {
        public TpArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir)
            : base (arc, impl, dir)
        {
        }
    }

    internal class Tp055Entry : Entry
    {
        public int  ChunkCount;

        public override string Type { get { return "audio"; } }
    }
}
