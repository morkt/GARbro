//! \file       ArcEAGLS.cs
//! \date       Fri May 15 02:52:04 2015
//! \brief      EAGLS system resource archives.
//
// Copyright (C) 2015-2016 by morkt
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
using GameRes.Formats.Strings;
using GameRes.Utility;

namespace GameRes.Formats.Eagls
{
    [Export(typeof(ArchiveFormat))]
    public class PakOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PAK/EAGLS"; } }
        public override string Description { get { return "EAGLS engine resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public PakOpener ()
        {
            Extensions = new string[] { "pak" };
        }

        internal static readonly string IndexKey = "1qaz2wsx3edc4rfv5tgb6yhn7ujm8ik,9ol.0p;/-@:^[]";
        internal static readonly byte[] EaglsKey  = Encoding.ASCII.GetBytes ("EAGLS_SYSTEM");
        internal static readonly byte[] AdvSysKey = Encoding.ASCII.GetBytes ("ADVSYS");

        public override ArcFile TryOpen (ArcView file)
        {
            if (file.Name.HasExtension (".idx"))
                return null;
            string idx_name = Path.ChangeExtension (file.Name, ".idx");
            if (!VFS.FileExists (idx_name))
                return null;
            var idx_entry = VFS.FindFile (idx_name);
            if (idx_entry.Size > 0xfffff || idx_entry.Size < 10000)
                return null;

            byte[] index;
            using (var idx = VFS.OpenView (idx_entry))
                index = DecryptIndex (idx);
            int index_offset = 0;
            int entry_size = index.Length / 10000;
            if (entry_size > 40)
                entry_size = 40;
            bool long_offsets = 40 == entry_size;
            int name_size = long_offsets ? 0x18 : 0x14;
            long first_offset = LittleEndian.ToUInt32 (index, name_size);
            bool has_scripts = false;
            var dir = new List<Entry>();
            while (index_offset < index.Length)
            {
                if (0 == index[index_offset])
                    break;
                var name = Binary.GetCString (index, index_offset, name_size);
                index_offset += name_size;
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                if (name.HasExtension ("dat"))
                {
                    entry.Type = "script";
                    has_scripts = true;
                }
                if (long_offsets)
                {
                    entry.Offset = LittleEndian.ToInt64 (index, index_offset) - first_offset;
                    entry.Size   = LittleEndian.ToUInt32 (index, index_offset+8);
                    index_offset += 0x10;
                }
                else
                {
                    entry.Offset = LittleEndian.ToUInt32 (index, index_offset) - first_offset;
                    entry.Size   = LittleEndian.ToUInt32 (index, index_offset+4);
                    index_offset += 8;
                }
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
            }
            if (0 == dir.Count)
                return null;
            if (dir[0].Name.HasExtension ("gr")) // CG archive
            {
                var rng = DetectEncryptionScheme (file, dir[0]);
                if (rng != null)
                    return new EaglsArchive (file, this, dir, new CgEncryption (rng));
            }
            else if (has_scripts)
            {
                var enc = QueryEncryption();
                if (enc != null)
                    return new EaglsArchive (file, this, dir, enc);
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var earc = arc as EaglsArchive;
            if (null == earc || !entry.Name.HasAnyOfExtensions ("dat", "gr"))
                return arc.File.CreateStream (entry.Offset, entry.Size);

            return earc.DecryptEntry (entry);
        }

        byte[] DecryptIndex (ArcView idx)
        {
            int idx_size = (int)idx.MaxOffset-4;
            byte[] output = new byte[idx_size];
            using (var view = idx.CreateViewAccessor (0, (uint)idx.MaxOffset))
            unsafe
            {
                var rng = new CRuntimeRandomGenerator();
                rng.SRand (view.ReadInt32 (idx_size));
                byte* ptr = view.GetPointer (0);
                try
                {
                    for (int i = 0; i < idx_size; ++i)
                    {
                        output[i] = (byte)(ptr[i] ^ IndexKey[rng.Rand() % IndexKey.Length]);
                    }
                    return output;
                }
                finally
                {
                    view.SafeMemoryMappedViewHandle.ReleasePointer();
                }
            }
        }

        IRandomGenerator DetectEncryptionScheme (ArcView file, Entry first_entry)
        {
            int signature = (file.View.ReadInt32 (first_entry.Offset) >> 8) & 0xFFFF;
            if (0x4D42 == signature) // 'BM'
                return null;
            byte seed = file.View.ReadByte (first_entry.Offset+first_entry.Size-1);
            IRandomGenerator[] rng_list = {
                new LehmerRandomGenerator(),
                new CRuntimeRandomGenerator(),
            };
            foreach (var rng in rng_list)
            {
                rng.SRand (seed);
                rng.Rand(); // skip LZSS control byte
                int test = signature;
                test ^= EaglsKey[rng.Rand() % EaglsKey.Length];
                test ^= EaglsKey[rng.Rand() % EaglsKey.Length] << 8;
                // FIXME
                // as key is rather short, this detection could produce false results sometimes
                if (0x4D42 == test) // 'BM'
                    return rng;
            }
            throw new UnknownEncryptionScheme();
        }

        public override ResourceOptions GetDefaultOptions ()
        {
            IEntryEncryption enc = null;
            if (!string.IsNullOrEmpty (Properties.Settings.Default.EAGLSEncryption))
                KnownSchemes.TryGetValue (Properties.Settings.Default.EAGLSEncryption, out enc);
            return new EaglsOptions { Encryption = enc };
        }

        public override object GetAccessWidget ()
        {
            return new GUI.WidgetEAGLS();
        }

        IEntryEncryption QueryEncryption ()
        {
            var options = Query<EaglsOptions> (arcStrings.ArcEncryptedNotice);
            return options.Encryption;
        }

        internal static Dictionary<string, IEntryEncryption> KnownSchemes = new Dictionary<string, IEntryEncryption>
        {
            { "EAGLS",      new EaglsEncryption() },
            { "AdvSys",     new AdvSysEncryption() },
        };
    }

    public class EaglsOptions : ResourceOptions
    {
        public IEntryEncryption Encryption;
    }

    public interface IRandomGenerator
    {
        void SRand (int seed);
        int Rand ();
    }

    [Serializable]
    public class CRuntimeRandomGenerator : IRandomGenerator
    {
        uint m_seed;

        public void SRand (int seed)
        {
            m_seed = (uint)seed;
        }

        public int Rand ()
        {
            m_seed = m_seed * 214013u + 2531011u;
            return (int)(m_seed >> 16) & 0x7FFF;
        }
    }

    [Serializable]
    public class LehmerRandomGenerator : IRandomGenerator
    {
        int m_seed;

        const int A = 48271;
        const int Q = 44488;
        const int R = 3399;
        const int M = 2147483647;

        public void SRand (int seed)
        {
            m_seed = seed ^ 123459876;
        }

        public int Rand ()
        {
            m_seed = A * (m_seed % Q) - R * (m_seed / Q);
            if (m_seed < 0)
                m_seed += M;
            return (int)(m_seed * 4.656612875245797e-10 * 256);
        }
    }

    public interface IEntryEncryption
    {
        void Decrypt (byte[] data);
    }

    [Serializable]
    public class CgEncryption : IEntryEncryption
    {
        readonly byte[] Key = PakOpener.EaglsKey;
        readonly IRandomGenerator m_rng;

        public CgEncryption (IRandomGenerator rng)
        {
            m_rng = rng;
        }

        public void Decrypt (byte[] data)
        {
            m_rng.SRand (data[data.Length-1]);
            int limit = Math.Min (data.Length-1, 0x174b);
            for (int i = 0; i < limit; ++i)
            {
                data[i] ^= (byte)Key[m_rng.Rand() % Key.Length];
            }
        }
    }

    [Serializable]
    public class EaglsEncryption : IEntryEncryption
    {
        readonly byte[] Key = PakOpener.EaglsKey;
        readonly IRandomGenerator m_rng;

        public EaglsEncryption ()
        {
            m_rng = new CRuntimeRandomGenerator();
        }

        public void Decrypt (byte[] data)
        {
            int text_offset = 3600;
            int text_length = data.Length - text_offset - 2;
            m_rng.SRand ((sbyte)data[data.Length-1]);
            for (int i = 0; i < text_length; i += 2)
            {
                data[text_offset + i] ^= Key[m_rng.Rand() % Key.Length];
            }
        }
    }

    [Serializable]
    public class AdvSysEncryption : IEntryEncryption
    {
        readonly byte[] Key = PakOpener.AdvSysKey;

        public void Decrypt (byte[] data)
        {
            int text_offset = 136000;
            int text_length = data.Length - text_offset;
            for (int i = 0; i < text_length; ++i)
            {
                data[text_offset + i] ^= Key[i % Key.Length];
            }
        }
    }

    internal class EaglsArchive : ArcFile
    {
        readonly IEntryEncryption Encryption;

        public EaglsArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, IEntryEncryption enc)
            : base (arc, impl, dir)
        {
            Encryption = enc;
        }

        public Stream DecryptEntry (Entry entry)
        {
            byte[] input = File.View.ReadBytes (entry.Offset, entry.Size);
            Encryption.Decrypt (input);
            return new BinMemoryStream (input, entry.Name);
        }
    }
}
