//! \file       ArcODN.cs
//! \date       Thu Nov 24 05:30:12 2016
//! \brief      Valkyria resource archive.
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
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Media;
using GameRes.Formats.Strings;
using GameRes.Utility;

namespace GameRes.Formats.Valkyria
{
    internal class ValkScheme
    {
        public string   ExeName;
        public string   Title;
        public int      ImageDim;
    }

    internal class OdnArchive : ArcFile
    {
        public readonly ValkScheme Scheme;

        public OdnArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, ValkScheme scheme)
            : base (arc, impl, dir)
        {
            Scheme = scheme;
        }
    }

    [Export(typeof(ArchiveFormat))]
    sealed public class OdnOpener : ArchiveFormat
    {
        public override string         Tag { get { return "ODN"; } }
        public override string Description { get { return "Valkyria resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public OdnOpener ()
        {
            Settings = new[] { AudioSampleRate };
        }

        FixedSetSetting AudioSampleRate = new FixedSetSetting (Properties.Settings.Default) {
            Name = "ODNAudioSampleRate",
            Text = arcStrings.ODNAudioSampleRate,
            ValuesSet = new[] { 22050u, 44100u },
        };

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.HasExtension (".odn"))
                return null;
            var reader = new OdnIndexReader (file);
            var dir = reader.ReadIndex();
            if (null == dir)
                return null;
            /*
            var sys_ini = VFS.CombinePath (VFS.GetDirectoryName (file.Name), "system.ini");
            if (VFS.FileExists (sys_ini))
            {
                try
                {
                    using (var ini = new IniReader (sys_ini))
                    {
                        var scheme = ini.ReadScheme();
                        return new OdnArchive (file, this, dir, scheme);
                    }
                }
                catch { }
            }
            */
            return new ArcFile (file, this, dir);
        }

        internal static readonly Regex Image24NameRe = new Regex ("^(?:back|phii)");
        internal static readonly Regex Image32NameRe = new Regex ("^(?:data|codn|cccc)");
        internal static readonly Regex ScriptNameRe  = new Regex ("^(?:scrp|menu|sysm)");
        internal static readonly Regex AudioNameRe   = new Regex ("^hime");

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            if (ScriptNameRe.IsMatch (entry.Name))
            {
                byte key = (byte)~entry.Offset;
                var data = arc.File.View.ReadBytes (entry.Offset, entry.Size);
                Decrypt (data, data.Length, key);
                return new BinMemoryStream (data);
            }
            if (AudioNameRe.IsMatch (entry.Name))
            {
                using (var wav = new MemoryStream (0x2C))
                {
                    var format = new WaveFormat {
                        FormatTag = 1,
                        Channels = 1,
                        SamplesPerSecond = AudioSampleRate.Get<uint>(),
                        BlockAlign = 2,
                        BitsPerSample = 16,
                    };
                    format.SetBPS();
                    WaveAudio.WriteRiffHeader (wav, format, entry.Size);
                    var header = wav.ToArray();
                    var data = arc.File.CreateStream (entry.Offset, entry.Size);
                    return new PrefixStream (header, data);
                }
            }
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            if (0x5E6A6A42 == input.Signature)
            {
                return new XoredStream (input, 0xD);
            }
            return input;
        }

        public override IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            using (var input = arc.File.CreateStream (entry.Offset, entry.Size))
            {
                int pixel_size;
                if (Image24NameRe.IsMatch (entry.Name))
                    pixel_size = 3;
                else
                    pixel_size = 4;
                var output = new MemoryStream ((int)entry.Size);
                try
                {
                    UnpackImage (input, output, pixel_size);
                    return CreateImageDecoder (output, pixel_size);
                }
                catch { /* try different pixel size */ }
                try
                {
                    pixel_size ^= 7;
                    input.Position = 0;
                    output.SetLength (0);
                    UnpackImage (input, output, pixel_size);
                    return CreateImageDecoder (output, pixel_size);
                }
                catch
                {
                    output.Dispose();
                    throw;
                }
            }
        }

        internal static byte Decrypt (byte[] data, int size, byte key)
        {
            for (int i = 0; i < size; ++i)
                data[i] ^= key--;
            return key;
        }

        static readonly Size[] ImageDimensions = new Size[]
        {
            new Size (1024, 768),
            new Size (800, 600),
            new Size (640, 480),
            new Size (400, 200),
        };

        void UnpackImage (IBinaryStream input, Stream output, int pixel_size)
        {
            var max_output_size = ImageDimensions[0].Width * ImageDimensions[0].Height * pixel_size;
            var pixel = new byte[pixel_size];
            int ctl = 1;
            for (;;)
            {
                if (1 == ctl)
                {
                    ctl = input.ReadByte();
                    if (-1 == ctl)
                        break;
                    ctl |= 0x100;
                }
                if (pixel.Length != input.Read (pixel, 0, pixel.Length))
                    break;
                int count = 0;
                if (0 != (ctl & 1))
                    count = Binary.BigEndian (input.ReadUInt16());
                for (int i = 0; i <= count; ++i)
                    output.Write (pixel, 0, pixel.Length);
                if (output.Length > max_output_size)
                    throw new InvalidFormatException();
                ctl >>= 1;
            }
            output.Position = 0;
        }

        IImageDecoder CreateImageDecoder (Stream image, int pixel_size)
        {
            var size = ImageDimensions.First (s => s.Width * s.Height * pixel_size == image.Length);
            var info = new ImageMetaData {
                Width = (uint)size.Width,
                Height = (uint)size.Height,
                BPP = pixel_size * 8,
            };
            return new OdnImageDecoder (image, info);
        }
    }

    internal sealed class OdnIndexReader
    {
        ArcView     m_file;
        byte[]      m_entry_buf = new byte[0x20];
        List<Entry> m_dir = new List<Entry>();
        Encoding    m_enc;

        public OdnIndexReader (ArcView file)
        {
            m_file = file;
            m_enc = Encoding.ASCII.WithFatalFallback();
        }

        public List<Entry> ReadIndex ()
        {
            m_file.View.Read (0, m_entry_buf, 0, 0x1C);
            if (m_entry_buf.AsciiEqual (8, "00000000"))
            {
                ReadV1();
            }
            else if (IsAscii (m_entry_buf, 0x10))
            {
                var name = m_enc.GetString (m_entry_buf, 0, 4);
                if (m_entry_buf.AsciiEqual (0x10, name))
                    ReadV2 (0x10);
                else if (m_entry_buf.AsciiEqual (0x18, name))
                    ReadV2 (0x18);
            }
            else
            {
                var key = OdnOpener.Decrypt (m_entry_buf, 0x10, 0xFF);
                if (m_entry_buf.AsciiEqual (8, "00000000"))
                    ReadEncrypted (key);
            }
            if (0 == m_dir.Count)
                return null;
            FixupDir();
            return m_dir;
        }

        void ReadV1 ()
        {
            uint index_offset = 0;
            for (;;)
            {
                if (0x10 != m_file.View.Read (index_offset, m_entry_buf, 0, 0x10))
                    throw new InvalidFormatException();
                index_offset += 0x10;
                if (m_entry_buf.AsciiEqual (0, "END_ffffffffffff"))
                    break;
                else if (m_entry_buf.AsciiEqual (0, "HIME_END"))
                {
                    index_offset += 8;
                    break;
                }
                var name = m_enc.GetString (m_entry_buf, 0, 8);
                var offset = m_enc.GetString (m_entry_buf, 8, 8);
                var entry = new Entry { Name = name, Offset = Convert.ToUInt32 (offset, 16) };
                m_dir.Add (entry);
            }
            foreach (var entry in m_dir)
                entry.Offset += index_offset;
            if (m_dir.Any() && m_dir[m_dir.Count-1].Offset == m_file.MaxOffset)
                m_dir.RemoveAt (m_dir.Count-1);
        }

        void ReadV2 (uint record_size)
        {
            uint index_offset = 0;
            var first_offset_str = m_enc.GetString (m_entry_buf, 8, 8);
            uint first_offset = Convert.ToUInt32 (first_offset_str, 16);
            while (index_offset < first_offset)
            {
                var offset_str = m_enc.GetString (m_entry_buf, 8, 8);
                var offset = Convert.ToUInt32 (offset_str, 16);
                if (m_file.MaxOffset == offset)
                    break;
                var name = m_enc.GetString (m_entry_buf, 0, 8);
                var entry = new Entry { Name = name, Offset = offset };
                m_dir.Add (entry);
                index_offset += record_size;
                if (record_size != m_file.View.Read (index_offset, m_entry_buf, 0, record_size))
                    throw new InvalidFormatException();
            }
        }

        void ReadEncrypted (byte key)
        {
            uint index_offset = 0;
            for (;;)
            {
                index_offset += 0x10;
                if (m_entry_buf.AsciiEqual (0, "ffffffffffffffff"))
                    break;
                var name = m_enc.GetString (m_entry_buf, 0, 8);
                var offset = m_enc.GetString (m_entry_buf, 8, 8);
                var entry = new Entry { Name = name, Offset = Convert.ToUInt32 (offset, 16) };
                m_dir.Add (entry);
                if (0x10 != m_file.View.Read (index_offset, m_entry_buf, 0, 0x10))
                    throw new InvalidFormatException();
                key = OdnOpener.Decrypt (m_entry_buf, 0x10, key);
            }
            foreach (var entry in m_dir)
                entry.Offset += index_offset;
        }

        static bool IsAscii (byte[] data, int length)
        {
            for (int i = 0; i < length; ++i)
                if (data[i] < 0x20 || data[i] > 0x7E)
                    return false;
            return true;
        }

        void FixupDir ()
        {
            for (int i = 0; i < m_dir.Count; ++i)
            {
                var entry = m_dir[i];

                long next_offset = i+1 < m_dir.Count ? m_dir[i+1].Offset : m_file.MaxOffset;
                entry.Size = (uint)(next_offset - entry.Offset);

                if (OdnOpener.Image24NameRe.IsMatch (entry.Name))
                {
                    entry.Type = "image";
                }
                else if (OdnOpener.ScriptNameRe.IsMatch (entry.Name))
                {
                    entry.Type = "script";
                }
                else if (OdnOpener.AudioNameRe.IsMatch (entry.Name))
                {
                    entry.Type = "audio";
                }
                else if (entry.Size > 4)
                {
                    var signature = m_file.View.ReadUInt32 (entry.Offset);
                    IResource res = null;
                    if (0x5E6A6A42 == signature)
                        res = OggAudio.Instance;
                    else
                        res = AutoEntry.DetectFileType (signature);
                    if (res != null)
                        entry.ChangeType (res);
                    else if (OdnOpener.Image32NameRe.IsMatch (entry.Name))
                        entry.Type = "image";
                }
            }
        }
    }

    internal sealed class IniReader : IDisposable
    {
        IBinaryStream   m_ini;

        public IniReader (string ini_name)
        {
            m_ini = VFS.OpenBinaryStream (ini_name);
        }

        public ValkScheme ReadScheme ()
        {
            m_ini.Position = 0;
            var exe_name = ReadString();
            var title = ReadString();
            return new ValkScheme
            {
                ExeName = exe_name,
                Title = title,
                ImageDim = m_ini.ReadByte(),
            };
        }

        string ReadString ()
        {
            var len_str = m_ini.ReadCString (4);
            int length = Convert.ToInt32 (len_str);
            var buf = m_ini.ReadBytes (length);
            if (buf.Length != length)
                throw new InvalidFormatException();
            for (int i = 0; i < length; ++i)
                buf[i] ^= 0xD1;
            return Encodings.cp932.GetString (buf);
        }

        #region IDisposable Members
        bool _disposed = false;
        public void Dispose ()
        {
            if (!_disposed)
            {
                m_ini.Dispose();
                _disposed = true;
            }
        }
        #endregion
    }

    internal sealed class OdnImageDecoder : IImageDecoder
    {
        ImageData       m_data;

        public ImageFormat SourceFormat { get { return null; } }
        public Stream            Source { get; private set; }
        public ImageMetaData       Info { get; private set; }
        public PixelFormat       Format { get; private set; }
        public ImageData          Image
        {
            get
            {
                if (null == m_data)
                {
                    int stride = (int)Info.Width * Info.BPP / 8;
                    var pixels = new byte[stride * (int)Info.Height];
                    Source.Read (pixels, 0, pixels.Length);
                    m_data = ImageData.CreateFlipped (Info, Format, null, pixels, stride);
                }
                return m_data;
            }
        }

        public OdnImageDecoder (Stream input, ImageMetaData info)
        {
            Source = input;
            Info = info;
            if (32 == info.BPP)
                Format = PixelFormats.Bgra32;
            else
                Format = PixelFormats.Bgr24;
        }

        bool _disposed = false;
        public void Dispose ()
        {
            if (!_disposed)
            {
                Source.Dispose();
                _disposed = true;
            }
        }
    }
}
