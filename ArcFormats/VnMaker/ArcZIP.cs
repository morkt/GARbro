using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;

using SharpZip = ICSharpCode.SharpZipLib.Zip;

namespace GameRes.Formats.VnMaker
{
    internal class ZipEntry : PackedEntry
    {
        public readonly SharpZip.ZipEntry NativeEntry;

        public ZipEntry (SharpZip.ZipEntry zip_entry)
        {
            NativeEntry = zip_entry;
            Name = zip_entry.Name;
            Type = FormatCatalog.Instance.GetTypeFromName (zip_entry.Name);
            IsPacked = true;
            // design decision of having 32bit entry sizes was made early during GameRes
            // library development. nevertheless, large files will be extracted correctly
            // despite the fact that size is reported as uint.MaxValue, because extraction is
            // performed by .Net framework based on real size value.
            Size = (uint)Math.Min (zip_entry.CompressedSize, uint.MaxValue);
            UnpackedSize = (uint)Math.Min (zip_entry.Size, uint.MaxValue);
            Offset = zip_entry.Offset;
        }
    }

    internal class PkZipArchive : ArcFile
    {
        readonly SharpZip.ZipFile m_zip;

        public SharpZip.ZipFile Native { get { return m_zip; } }

        public PkZipArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, SharpZip.ZipFile native)
            : base (arc, impl, dir)
        {
            m_zip = native;
        }

        #region IDisposable implementation
        bool _zip_disposed = false;
        protected override void Dispose (bool disposing)
        {
            if (!_zip_disposed)
            {
                if (disposing)
                    m_zip.Close();
                _zip_disposed = true;
            }
            base.Dispose (disposing);
        }
        #endregion
    }

    [Export(typeof(ArchiveFormat))]
    public class ZipOpener : ArchiveFormat
    {
        public override string         Tag { get { return "ZIP/VnMaker Encrypted ZIP Archive"; } }
        public override string Description { get { return "VnMaker Encrypted ZIP Archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public ZipOpener ()
        {
            Settings = new[] { ZipEncoding };
            Extensions = new string[] { "zip" };
        }

        readonly EncodingSetting ZipEncoding = new EncodingSetting ("ZIPEncodingCP", "DefaultEncoding");

        public override ArcFile TryOpen (ArcView file)
        {
            var input = file.CreateStream ();
            try
            {
                var zip = DeobfuscateStream (input, GuessEncryptionKey (input));
                if ((zip.Signature & 0xFFFF) != 0x4B50) // 'PK'
                    throw new InvalidFormatException ();
                return OpenZipArchive (file, zip.AsStream);
            }
            catch
            {
                input.Dispose ();
                throw;
            }
        }

        byte[] GuessEncryptionKey (IBinaryStream file)
        {
            return new byte[] { 0x0A, 0x2B, 0x36, 0x6F, 0x0B };
        }

        IBinaryStream DeobfuscateStream (IBinaryStream file, byte[] key)
        {
            var zip = new ByteStringEncryptedStream (file.AsStream, key, true);
            return new BinaryStream (zip, file.Name);
        }

        internal ArcFile OpenZipArchive (ArcView file, Stream input)
        {
            var sc = SharpZip.StringCodec.FromCodePage (Properties.Settings.Default.ZIPEncodingCP);
            var zip = new SharpZip.ZipFile (input, false, sc);
            try
            {
                var files = zip.Cast<SharpZip.ZipEntry>().Where (z => !z.IsDirectory);
                var dir = files.Select (z => new ZipEntry (z) as Entry).ToList();
                return new PkZipArchive (file, this, dir, zip);
            }
            catch
            {
                zip.Close();
                throw;
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var zarc = (PkZipArchive)arc;
            var zent = (ZipEntry)entry;
            return zarc.Native.GetInputStream (zent.NativeEntry);
        }
    }
}
