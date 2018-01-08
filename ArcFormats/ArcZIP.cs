//! \file       ArcZIP.cs
//! \date       Sat Mar 05 14:47:42 2016
//! \brief      PKWARE ZIP archive format.
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
using System.IO.Compression;
using System.Linq;
using System.Text;
using GameRes.Formats.Strings;

namespace GameRes.Formats.PkWare
{
    internal class ZipEntry : PackedEntry
    {
        public readonly ZipArchiveEntry NativeEntry;

        public ZipEntry (ZipArchiveEntry zip_entry)
        {
            NativeEntry = zip_entry;
            Name = zip_entry.FullName;
            Type = FormatCatalog.Instance.GetTypeFromName (zip_entry.FullName);
            IsPacked = true;
            // design decision of having 32bit entry sizes was made early during GameRes
            // library development. nevertheless, large files will be extracted correctly
            // despite the fact that size is reported as uint.MaxValue, because extraction is
            // performed by .Net framework based on real size value.
            Size = (uint)Math.Min (zip_entry.CompressedLength, uint.MaxValue);
            UnpackedSize = (uint)Math.Min (zip_entry.Length, uint.MaxValue);
            // offset is unknown and reported as '0' for all files.
            Offset = 0;
        }
    }

    internal class PkZipArchive : ArcFile
    {
        readonly ZipArchive m_zip;

        public PkZipArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, ZipArchive native)
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
                    m_zip.Dispose();
                _zip_disposed = true;
            }
            base.Dispose (disposing);
        }
        #endregion
    }

    [Export(typeof(ArchiveFormat))]
    public class ZipOpener : ArchiveFormat
    {
        public override string         Tag { get { return "ZIP"; } }
        public override string Description { get { return "PKWARE archive format"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return true; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (-1 == SearchForSignature (file))
                return null;
            var input = file.CreateStream();
            try
            {
                var zip = new ZipArchive (input, ZipArchiveMode.Read, false, Encodings.cp932);
                try
                {
                    var dir = zip.Entries.Where (z => !z.FullName.EndsWith ("/"))
                        .Select (z => new ZipEntry (z) as Entry).ToList();
                    return new PkZipArchive (file, this, dir, zip);
                }
                catch
                {
                    zip.Dispose();
                    throw;
                }
            }
            catch
            {
                input.Dispose();
                throw;
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            return ((ZipEntry)entry).NativeEntry.Open();
        }

        /// <summary>
        /// Search for ZIP 'End of central directory record' near the end of file.
        /// Returns offset of 'PK' signature or -1 if no signature was found.
        /// </summary>
        private unsafe long SearchForSignature (ArcView file)
        {
            uint tail_size = (uint)Math.Min (file.MaxOffset, 0x10016L);
            if (tail_size < 0x16)
                return -1;
            var start_offset = file.MaxOffset - tail_size;
            using (var view = file.CreateViewAccessor (start_offset, tail_size))
            {
                byte* ptr_end = view.GetPointer (start_offset);
                byte* ptr = ptr_end + tail_size-0x16;
                try {
                    for (; ptr >= ptr_end; --ptr)
                    {
                        if (6 == ptr[3] && 5 == ptr[2] && 'K' == ptr[1] && 'P' == ptr[0])
                            return start_offset + (ptr-ptr_end);
                    }
                    return -1;
                } finally {
                    view.SafeMemoryMappedViewHandle.ReleasePointer();
                }
            }
        }

        public override ResourceOptions GetDefaultOptions ()
        {
            Encoding enc;
            try
            {
                enc = Encoding.GetEncoding (Properties.Settings.Default.ZIPEncodingCP);
            }
            catch
            {
                enc = Encodings.cp932;
            }
            return new ZipOptions {
                CompressionLevel = Properties.Settings.Default.ZIPCompression,
                FileNameEncoding = enc,
            };
        }

        // TODO: GUI widget for options

        public override void Create (Stream output, IEnumerable<Entry> list, ResourceOptions options,
                                     EntryCallback callback)
        {
            var zip_options = GetOptions<ZipOptions> (options);
            int callback_count = 0;
            using (var zip = new ZipArchive (output, ZipArchiveMode.Create, true, zip_options.FileNameEncoding))
            {
                foreach (var entry in list)
                {
                    var zip_entry = zip.CreateEntry (entry.Name, zip_options.CompressionLevel);
                    using (var input = File.OpenRead (entry.Name))
                    using (var zip_file = zip_entry.Open())
                    {
                        if (null != callback)
                            callback (++callback_count, entry, arcStrings.MsgAddingFile);
                        input.CopyTo (zip_file);
                    }
                }
            }
        }
    }

    public class ZipOptions : ResourceOptions
    {
        public CompressionLevel CompressionLevel { get; set; }
        public         Encoding FileNameEncoding { get; set; }
    }
}
