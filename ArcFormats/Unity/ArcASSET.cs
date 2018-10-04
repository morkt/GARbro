//! \file       ArcASSET.cs
//! \date       2017 Nov 22
//! \brief      Unity assets archive.
//
// Copyright (C) 2017-2018 by morkt
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

using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Utility;

namespace GameRes.Formats.Unity
{
    [Export(typeof(ArchiveFormat))]
    public class UnityAssetOpener : ArchiveFormat
    {
        public override string         Tag { get { return "ASSETS/UNITY"; } }
        public override string Description { get { return "Unity game engine assets archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            uint header_size = Binary.BigEndian (file.View.ReadUInt32 (0));
            uint file_size = Binary.BigEndian (file.View.ReadUInt32 (4));
            if (file_size != file.MaxOffset || header_size > file_size || 0 == header_size)
                return null;
            int format = Binary.BigEndian (file.View.ReadInt32 (8));
            uint data_offset = Binary.BigEndian (file.View.ReadUInt32 (12));
            if (format <= 0 || format > 0x100 || data_offset >= file_size || data_offset < header_size)
                return null;
            using (var stream = file.CreateStream())
            using (var input = new AssetReader (stream))
            {
                var index = new ResourcesAssetsDeserializer (file.Name);
                var dir = index.Parse (input);
                if (null == dir || 0 == dir.Count)
                    return null;
                var res_map = index.GenerateResourceMap (dir);
                return new UnityResourcesAsset (file, this, dir, res_map);
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var uarc = (UnityResourcesAsset)arc;
            var uent = (AssetEntry)entry;
            if (null == uent.Bundle || !uarc.ResourceMap.ContainsKey (uent.Bundle.Name))
                return arc.File.CreateStream (entry.Offset, entry.Size);
            var bundle = uarc.ResourceMap[uent.Bundle.Name];
            return bundle.CreateStream (entry.Offset, entry.Size);
        }

        public override IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            var aent = entry as AssetEntry;
            if (null == aent || aent.AssetObject.TypeId != 28)
                return base.OpenImage (arc, entry);

            var obj = aent.AssetObject;
            var stream = arc.File.CreateStream (obj.Offset, obj.Size);
            var reader = new AssetReader (stream);
            try
            {
                reader.SetupReaders (obj.Asset);
                var tex = new Texture2D();
                tex.Load (reader, obj.Asset.Tree.Version);
                if (0 == tex.m_DataLength)
                {
                    reader.Dispose();
                    var input = OpenEntry (arc, entry);
                    reader = new AssetReader (input, entry.Name);
                    reader.SetupReaders (obj.Asset);
                    tex.m_DataLength = (int)entry.Size;
                }
                var decoder = new Texture2DDecoder (tex, reader);
                reader = null;
                return decoder;
            }
            finally
            {
                if (reader != null)
                    reader.Dispose();
            }
        }
    }

    internal class UnityResourcesAsset : ArcFile
    {
        public readonly IDictionary<string, ArcView> ResourceMap;

        public UnityResourcesAsset (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, IDictionary<string, ArcView> res_map)
            : base (arc, impl, dir)
        {
            ResourceMap = res_map;
        }

        #region IDisposable Members
        bool m_disposed = false;

        protected override void Dispose (bool disposing)
        {
            if (!m_disposed)
            {
                if (disposing)
                {
                    foreach (var res in ResourceMap.Values)
                    {
                        res.Dispose();
                    }
                }
                m_disposed = true;
            }
            base.Dispose (disposing);
        }
        #endregion
    }
}
