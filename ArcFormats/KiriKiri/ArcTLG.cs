//! \file       ArcTLG.cs
//! \date       Tue Mar 17 2026 10:35:55
//! \brief      KiriKiri TLG image implementation.
//---------------------------------------------------------------------------
// TLGqoi multi-layer image decoder
//
// C# port by crsky
//

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.KiriKiri
{
    internal class TlgLayerEntry : Entry
    {
        public int Index;
    }

    [Export(typeof(ArchiveFormat))]
    public class TlgOpener : ArchiveFormat
    {
        public override string         Tag { get { return "TLG"; } }
        public override string Description { get { return "KiriKiri game engine image format"; } }
        public override uint     Signature { get { return 0x71474C54; } } // 'TLGq'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public TlgOpener ()
        {
            Extensions = new string[] { "tlg" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (0, "TLGqoi") || !file.View.AsciiEqual (7, "raw"))
                return null;
            var qhdr = Array.Empty<byte> ();
            var offset = 0x14;
            while (true)
            {
                var entry_signature = file.View.ReadInt32 (offset);
                var entry_size = file.View.ReadInt32 (offset+4);
                offset += 8;
                if (0x52444851 == entry_signature) // 'QHDR'
                {
                    if (0x30 != entry_size)
                        return null;
                    qhdr = file.View.ReadBytes (offset, (uint)entry_size);
                    if (entry_size != qhdr.Length)
                        return null;
                    offset += entry_size;
                }
                else if (0 == entry_signature && 0 == entry_size)
                    break;
                else
                    return null;
            }
            if (0 == qhdr.Length)
                return null;
            var layer_count = qhdr.ToInt32 (4);
            if (layer_count < 1)
                return null;
            var block_count = qhdr.ToInt32 (12);
            if (0 == block_count)
                return null;
            var dir = new List<Entry> (layer_count);
            for (var i = 0; i < layer_count; i++)
            {
                dir.Add (new TlgLayerEntry
                {
                    Name = string.Format ("{0}#{1:D3}.tlg", Path.GetFileNameWithoutExtension (file.Name), i),
                    Offset = 0,
                    Size = (uint)file.MaxOffset,
                    Type = "image",
                    Index = i,
                });
            }
            return new ArcFile (file, this, dir);
        }

        static readonly ResourceInstance<ImageFormat> s_TlgFormat = new ResourceInstance<ImageFormat> ("TLG");

        public override IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            var layer_entry = entry as TlgLayerEntry;
            if (null == layer_entry)
                return base.OpenImage (arc, entry);
            var input = arc.File.CreateStream ();
            try
            {
                var info = s_TlgFormat.Value.ReadMetaData (input);
                if (null == info)
                    throw new InvalidFormatException ();
                if (info is TlgMetaData tlg)
                {
                    tlg.LayerIndex = layer_entry.Index;
                }
                return new ImageFormatDecoder (input, s_TlgFormat.Value, info);
            }
            catch
            {
                input.Dispose ();
                throw;
            }
        }
    }
}
