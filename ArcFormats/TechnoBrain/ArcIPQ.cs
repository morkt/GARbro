//! \file       ArcIPQ.cs
//! \date       2022 May 02
//! \brief      TechnoBrain's animation resource.
//
// Copyright (C) 2022 by morkt
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

namespace GameRes.Formats.TechnoBrain
{
    internal class IpqArchive : ArcFile
    {
        public readonly IpfMetaData     Info;

        public IpqArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, IpfMetaData info)
            : base (arc, impl, dir)
        {
            Info = info;
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class IpqOpener : ArchiveFormat
    {
        public override string         Tag { get { return "IPQ"; } }
        public override string Description { get { return "TechnoBrain's animation resource"; } }
        public override uint     Signature { get { return 0; } } // 'RIFF'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        static readonly ResourceInstance<IpfFormat> Ipf = new ResourceInstance<IpfFormat>("IPF");

        public override ArcFile TryOpen (ArcView file)
        {
            if (file.View.ReadUInt32 (0) != 0x46464952 // 'RIFF'
                || !file.View.AsciiEqual (8, "IPQ fmt "))
                return null;
            IpfMetaData ipq_info;
            using (var ipq = file.CreateStream())
            {
                ipq_info = Ipf.Value.ReadIpfHeader (ipq);
                if (null == ipq_info || ipq_info.FormatString != "IPQ fmt ")
                    return null;
                ipq.Position = ipq_info.DataOffset;
                if (ipq.ReadUInt32() != 0x6D696E61) // "anim"
                    return null;
                uint index_size = ipq.ReadUInt32();
                int count = ipq.ReadInt32();
                if (!IsSaneCount (count))
                    return null;

                var base_name = Path.GetFileNameWithoutExtension (file.Name);
                var dir = new List<Entry> (count);
                for (int i = 0; i < count; ++i)
                {
                    var entry = new Entry {
                        Name = string.Format ("{0}#{1:D3}", base_name, i),
                        Type = "image",
                        Offset = ipq.ReadUInt32(),
                    };
                    dir.Add (entry);
                }
                long last_offset = file.MaxOffset;
                for (int i = count-1; i >= 0; --i)
                {
                    dir[i].Size = (uint)(last_offset - dir[i].Offset);
                    last_offset = dir[i].Offset;
                }
                return new IpqArchive (file, this, dir, ipq_info);
            }
        }

        public override IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            var ipq = arc as IpqArchive;
            if (null == ipq)
                return base.OpenImage (arc, entry);
            var info = ipq.Info.Clone() as IpfMetaData;
            var file = arc.File.CreateStream();
            try
            {
                file.Position = entry.Offset;
                if (!Ipf.Value.ReadBmpInfo (file, info))
                    throw new InvalidFormatException ("Invalid 'bmp' section.");
                return new IpfReader (file, info, Ipf.Value);
            }
            catch
            {
                file.Dispose();
                throw;
            }
        }
    }
}
