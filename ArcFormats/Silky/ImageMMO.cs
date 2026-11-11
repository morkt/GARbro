using System.ComponentModel.Composition;

// [070223][Silky's] Himekishi Angelica ~Anata tte, Hontou ni Saitei no Kuzu Da wa!~

namespace GameRes.Formats.Silky
{
    [Export(typeof(ImageFormat))]
    public class MmoFormat : AkbFormat
    {
        public override string Tag => "MMO";
        public override string Description => "Silky's MMO image format";
        public override uint Signature => 0x204F4D4D; // 'MMO '

        public MmoFormat()
        {
            Signatures = new[] { Signature };
        }

        public override ImageMetaData ReadMetaData(IBinaryStream file)
        {
            file.Position += 4;
            var metaData = new AkbMetaData();
            metaData.TopDownOrder = true;
            metaData.Flags = 0;
            metaData.OffsetX = file.ReadInt32();
            metaData.OffsetY = file.ReadInt32();
            metaData.InnerWidth = file.ReadInt32() - metaData.OffsetX;
            metaData.InnerHeight = file.ReadInt32() - metaData.OffsetY;
            // Not sure if there are any other pixel encoding
            metaData.BPP = 24;
            // Skip 8 byte unknown field
            file.Position += 8;
            metaData.Width = file.ReadUInt32();
            metaData.Height = file.ReadUInt32();
            // Skip another 4 byte unknown field
            file.Position += 4;
            metaData.DataOffset = (uint)file.Position;
            // FIXME: Implement reliable way to detect difference image and find the corresponding base image name Find base image
            // Unlike AKB format, MMO format has neither mark for difference image nor the name of its base image file embedded.
            // Also, there doesn't seem to have a constant naming pattern even in the same game.
            metaData.BaseFileName = null;
            metaData.Background = new byte[4];
            return metaData;
        }
    }
}