//! PAZ archive implementation.
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
using System.Linq;
using System.Security.Cryptography;
using GameRes.Compression;
using GameRes.Cryptography;
using GameRes.Formats.Properties;
using GameRes.Formats.Strings;
using GameRes.Utility;

namespace GameRes.Formats.Musica
{
    internal class PazEntry : PackedEntry
    {
        public uint     AlignedSize;
        public byte[]   Key;
    }

    internal class PazArchiveBase : ArcFile
    {
        public readonly int         Version;
        public readonly byte        XorKey;

        public PazArchiveBase (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, int version, byte key, IReadOnlyList<ArcView> parts = null)
            : base (arc, impl, dir)
        {
            Version = version;
            XorKey = key;
            m_parts = parts;
        }

        IReadOnlyList<ArcView>  m_parts;

        public IEnumerable<ArcView> Parts
        {
            get
            {
                yield return File;
                if (m_parts != null)
                    foreach (var part in m_parts)
                        yield return part;
            }
        }

        bool _paz_disposed = false;
        protected override void Dispose (bool disposing)
        {
            if (_paz_disposed)
                return;

            if (disposing && m_parts != null)
            {
                foreach (var arc in m_parts)
                    arc.Dispose();
            }
            _paz_disposed = true;
            base.Dispose (disposing);
        }
    }

    internal class PazArchive : PazArchiveBase
    {
        public readonly Blowfish    Encryption;

        public PazArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, int version, byte key, byte[] data_key, IReadOnlyList<ArcView> parts = null)
            : base (arc, impl, dir, version, key, parts)
        {
            Encryption = new Blowfish (data_key);
        }
    }

    internal class MovPazArchive : PazArchiveBase
    {
        public readonly byte[]  MovKey;

        public MovPazArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, int version, byte key, byte[] mov_key, IReadOnlyList<ArcView> parts = null)
            : base (arc, impl, dir, version, key, parts)
        {
            MovKey = mov_key;
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class PazOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PAZ"; } }
        public override string Description { get { return "Musica engine resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public PazOpener ()
        {
            Signatures = new uint[] { 0x858F8493, 0 };
        }

        static readonly ISet<string> AudioPazNames = new HashSet<string> {
            "bgm", "se", "voice", "pmbgm", "pmse", "pmvoice"
        };
        static readonly ISet<string> VideoPazNames = new HashSet<string> { "mov" };

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.EndsWith (".paz", StringComparison.InvariantCultureIgnoreCase))
                return null;
            uint signature = file.View.ReadUInt32 (0);
            uint index_size = file.View.ReadUInt32 (0x20);
            byte xor_key = (byte)(index_size >> 24);
            index_size ^= (uint)(xor_key << 24 | xor_key << 16 | xor_key << 8 | xor_key);
            if (index_size + 0x24 >= file.MaxOffset)
                return null;
            var arc_name = Path.GetFileNameWithoutExtension (file.Name).ToLowerInvariant();
            var scheme = QueryEncryption (arc_name, signature);

            var arc_list = new List<Entry>();
            var arc_dir = VFS.GetDirectoryName (file.Name);
            long max_offset = file.MaxOffset;
            for (char suffix = 'A'; suffix <= 'Z'; ++suffix)
            {
                var part_name = VFS.CombinePath (arc_dir, file.Name + suffix);
                if (!VFS.FileExists (part_name))
                    break;
                var part = VFS.FindFile (part_name);
                arc_list.Add (part);
                max_offset += part.Size;
            }
            bool is_audio = AudioPazNames.Contains (arc_name);
            bool is_video = VideoPazNames.Contains (arc_name);
            Stream input = file.CreateStream (0x24, index_size);
            byte[] video_key = null;
            List<Entry> dir;
            try
            {
                input = new XoredStream (input, xor_key);
                var enc = new Blowfish (scheme.ArcKeys[arc_name].IndexKey);
                input = new CryptoStream (input, enc.CreateDecryptor(), CryptoStreamMode.Read);
                using (var index = new ArcView.Reader (input))
                {
                    int count = index.ReadInt32();
                    if (!IsSaneCount (count))
                        return null;
                    if (is_video)
                        video_key = index.ReadBytes (0x100);

                    dir = new List<Entry> (count);
                    for (int i = 0; i < count; ++i)
                    {
                        var name = index.BaseStream.ReadCString();
                        var entry = FormatCatalog.Instance.Create<PazEntry> (name);
                        entry.Offset    = index.ReadInt64();
                        entry.Size      = index.ReadUInt32();
                        if (!entry.CheckPlacement (max_offset))
                            return null;
                        entry.UnpackedSize = index.ReadUInt32();
                        entry.AlignedSize = index.ReadUInt32();
                        entry.IsPacked = index.ReadInt32() != 0;
                        if (string.IsNullOrEmpty (entry.Type) && is_audio)
                        {
                            entry.Type = "audio";
                        }
                        string password = "";
                        if (!entry.IsPacked && scheme.TypeKeys != null)
                        {
                            password = scheme.GetTypePassword (name, is_audio);
                        }
                        if (!string.IsNullOrEmpty (password) || is_video)
                        {
                            password = string.Format ("{0} {1:X08} {2}", name.ToLowerInvariant(), entry.UnpackedSize, password);
                            entry.Key = Encodings.cp932.GetBytes (password);
                        }
                        dir.Add (entry);
                    }
                }
            }
            finally
            {
                input.Dispose();
            }
            List<ArcView> parts = null;
            if (arc_list.Count > 0)
            {
                parts = new List<ArcView> (arc_list.Count);
                try
                {
                    foreach (var arc_entry in arc_list)
                    {
                        var arc_file = VFS.OpenView (arc_entry);
                        parts.Add (arc_file);
                    }
                }
                catch
                {
                    foreach (var part in parts)
                        part.Dispose();
                    throw;
                }
            }
            if (is_video)
                return new MovPazArchive (file, this, dir, scheme.Version, xor_key, video_key, parts);
            return new PazArchive (file, this, dir, scheme.Version, xor_key, scheme.ArcKeys[arc_name].DataKey, parts);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var parc = arc as PazArchiveBase;
            var pent = entry as PazEntry;
            if (null == parc || null == pent)
                return base.OpenEntry (arc, entry);

            Stream input = null;
            try
            {
                long part_offset = 0;
                long entry_start = pent.Offset;
                long entry_end   = pent.Offset + pent.AlignedSize;
                foreach (var part in parc.Parts)
                {
                    long part_end_offset = part_offset + part.MaxOffset;
                    if (entry_start < part_end_offset)
                    {
                        uint part_size = (uint)Math.Min (entry_end - entry_start, part_end_offset - entry_start);
                        var entry_part = part.CreateStream (entry_start - part_offset, part_size);
                        if (input != null)
                            input = new ConcatStream (input, entry_part);
                        else
                            input = entry_part;
                        entry_start += part_size;
                        if (entry_start >= entry_end)
                            break;
                    }
                    part_offset = part_end_offset;
                }
                if (null == input)
                    return Stream.Null;

                input = new XoredStream (input, parc.XorKey);
                if (parc is MovPazArchive)
                    input = DecryptVideo (input, (MovPazArchive)parc, pent);
                else
                    input = DecryptEntry (input, (PazArchive)parc, pent);

                if (pent.Size < pent.AlignedSize)
                    input = new LimitStream (input, pent.Size);
                if (pent.IsPacked)
                    input = new ZLibStream (input, CompressionMode.Decompress);
                return input;
            }
            catch
            {
                if (input != null)
                    input.Dispose();
                throw;
            }
        }

        Stream DecryptVideo (Stream input, MovPazArchive arc, PazEntry entry)
        {
            var key = new byte[0x100];
            for (int i = 0; i < 0x100; ++i)
                key[i] = (byte)(arc.MovKey[i] ^ entry.Key[i % entry.Key.Length]);

            var rc4 = new Rc4Transform (key);
            var block = rc4.GenerateBlock ((int)Math.Min (0x10000, input.Length));
            return new ByteStringEncryptedStream (input, block);
        }

        Stream DecryptEntry (Stream input, PazArchive arc, PazEntry entry)
        {
            input = new CryptoStream (input, arc.Encryption.CreateDecryptor(), CryptoStreamMode.Read);
            var key = entry.Key;
            if (null == key)
                return input;
            var rc4 = new Rc4Transform (key);
            if (arc.Version >= 2)
            {
                uint crc = Crc32.Compute (key, 0, key.Length);
                int skip_rounds = (int)(crc >> 12) & 0xFF; 
                for (int i = 0; i < skip_rounds; ++i)
                {
                    rc4.NextByte();
                }
            }
            return new CryptoStream (input, rc4, CryptoStreamMode.Read);
        }

        PazScheme QueryEncryption (string arc_name, uint signature)
        {
            PazScheme scheme = null;
            if (!KnownSchemes.TryGetValue (signature, out scheme) && KnownTitles.Count > 1)
            {
                var options = Query<PazOptions> (arcStrings.ArcEncryptedNotice);
                scheme = options.Scheme;
            }
            if (null == scheme || !scheme.ArcKeys.ContainsKey (arc_name))
                throw new UnknownEncryptionScheme();
            return scheme;
        }

        public override ResourceOptions GetDefaultOptions ()
        {
            return new PazOptions { Scheme = GetScheme (Settings.Default.PAZTitle) };
        }

        public override object GetAccessWidget ()
        {
            return new GUI.WidgetPAZ (this);
        }

        PazScheme GetScheme (string title)
        {
            PazScheme scheme;
            if (string.IsNullOrEmpty (title) || !KnownTitles.TryGetValue (title, out scheme))
                return null;
            return scheme;
        }

        public override ResourceScheme Scheme
        {
            get { return m_scheme; }
            set { m_scheme = (MusicaScheme)value; }
        }

        MusicaScheme m_scheme = new MusicaScheme
        {
            KnownSchemes = new Dictionary<uint, PazScheme>(),
            KnownTitles = new Dictionary<string, PazScheme>()
        };

        public IDictionary<uint, PazScheme>  KnownSchemes { get { return m_scheme.KnownSchemes; } }
        public IDictionary<string, PazScheme> KnownTitles { get { return m_scheme.KnownTitles; } }
    }

    [Serializable]
    public class PazKey
    {
        public byte[]   IndexKey;
        public byte[]   DataKey;
    }

    [Serializable]
    public class PazScheme
    {
        public int                          Version;
        public IDictionary<string, PazKey>  ArcKeys;
        public IDictionary<string, string>  TypeKeys;

        public string GetTypePassword (string name, bool is_audio)
        {
            string password = null;
            if (name.Contains ('.'))
            {
                if (name.EndsWith (".png"))
                    TypeKeys.TryGetValue ("png", out password);
                else if (name.EndsWith (".ogg") || is_audio)
                    TypeKeys.TryGetValue ("ogg", out password);
                else if (name.EndsWith (".sc"))
                    TypeKeys.TryGetValue ("sc", out password);
                else if (name.EndsWith (".avi") || name.EndsWith (".mpg") || name.EndsWith (".mpeg"))
                    TypeKeys.TryGetValue ("avi", out password);
            }
            else if (is_audio)
                TypeKeys.TryGetValue ("ogg", out password);
            return password ?? "";
        }
    }

    [Serializable]
    public class MusicaScheme : ResourceScheme
    {
        public IDictionary<uint, PazScheme>     KnownSchemes;
        public IDictionary<string, PazScheme>   KnownTitles;
    }

    public class PazOptions : ResourceOptions
    {
        public PazScheme    Scheme;
    }
}
