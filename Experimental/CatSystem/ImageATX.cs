//! \file       ImageATX.cs
//! \date       2026-04-04
//! \brief      CatSystem for Unity image format.
//
// Copyright (C) 2026 by morkt
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
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Newtonsoft.Json;

using SharpZip = ICSharpCode.SharpZipLib.Zip;

namespace GameRes.Formats.CatSystem {
    [Serializable]
    public class LayoutInfo {
        public LayoutInfo.CanvasInfo Canvas;
        public List<LayoutInfo.BlockInfo> Block;

        [Serializable]
        public class CanvasInfo {
            public uint Width;
            public uint Height;
        }

        [Serializable]
        public class MeshInfo {
            public int texNo;
            public float offsetX;
            public float offsetY;
            public float srcOffsetX;
            public float srcOffsetY;
            public float texU1;
            public float texV1;
            public float texU2;
            public float texV2;
            public float viewX;
            public float viewY;
            public float width;
            public float height;
        }

        [Serializable]
        public class AttributeInfo {
            public int id;
            public int x;
            public int y;
            public int width;
            public int height;
            public int color;
        }

        [Serializable]
        public class BlockInfo {
            public string filename;
            public string filenameOld;
            public string blend;
            public int id;
            public float anchorX;
            public float anchorY;
            public float width;
            public float height;
            public float offsetX;
            public float offsetY;
            public int priority;
            public List<LayoutInfo.MeshInfo> Mesh;
            public List<LayoutInfo.AttributeInfo> Attribute;
        }
    }

    internal class AtlasMetaData : ImageMetaData {
        public List<LayoutInfo.BlockInfo> Blocks;
    }

    [Export(typeof(ImageFormat))]
    public class AtxFormat : ImageFormat {
        public override string         Tag { get { return "ATX"; } }
        public override string Description { get { return "CatSystem for Unity image format"; } }
        public override uint     Signature { get { return 0; } }

        public AtxFormat() {
            Extensions = new string[] { "atx" };
        }

        public override ImageMetaData ReadMetaData(IBinaryStream stream) {
            if (0x04034b50 != stream.Signature) // 'PK\3\4'
                return null;

            var zf = new SharpZip.ZipFile(stream.AsStream);
            SharpZip.ZipEntry info_entry;

            if ((info_entry = zf.GetEntry("atlas.json")) == null)
                return null; // protobuf decoding is not supported yet.
            using (var sr = new StreamReader(zf.GetInputStream(info_entry))) {
                var info = JsonConvert.DeserializeObject<LayoutInfo>(sr.ReadToEnd());
                return new AtlasMetaData {
                    Height = info.Canvas.Height,
                    Width = info.Canvas.Width,
                    Blocks = info.Block,
                    BPP = 32
                };
            }
        }

        public override ImageData Read(IBinaryStream stream, ImageMetaData info) {
            var amd = (AtlasMetaData)info;
            var zf = new SharpZip.ZipFile(stream.AsStream);
            var textures = new Dictionary<int, byte[]>();
            var bitmap = new WriteableBitmap(amd.iWidth, amd.iHeight,
                    ImageData.DefaultDpiX, ImageData.DefaultDpiY, PixelFormats.Bgra32, null);
            foreach (var mesh in amd.Blocks[0].Mesh) { // only process the first image
                if (!textures.ContainsKey(mesh.texNo)) {
                    SharpZip.ZipEntry tex_entry = null;
                    // https://storage.googleapis.com/downloads.webmproject.org/releases/webp/WebpCodecSetup.exe
                    // install this for webp codec support
                    var availableFormats = new[] { "png", "jpg", "webp" };
                    foreach (var format in availableFormats) {
                        if ((tex_entry = zf.GetEntry($"tex{mesh.texNo}.{format}")) != null)
                            break;
                    }
                    if (tex_entry == null)
                        return null;
                    using (var input = zf.GetInputStream(tex_entry))
                    using (var mem = new MemoryStream()) {
                        input.CopyTo(mem);
                        textures[mesh.texNo] = mem.ToArray();
                    }
                }
                using (var mem = new MemoryStream(textures[mesh.texNo])) {
                    var decoder = BitmapDecoder.Create(mem,
                            BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                    BitmapSource frame = decoder.Frames[0];
                    if (frame.Format.BitsPerPixel != 32)
                        frame = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
                    int width = (int)mesh.width, height = (int)mesh.height;
                    var rect = new Int32Rect((int)mesh.viewX, (int)mesh.viewY, width, height);
                    int stride = width * 4;
                    var pixels = new byte[stride * height];
                    frame.CopyPixels(rect, pixels, stride, 0);
                    rect = new Int32Rect(0, 0, width, height);
                    bitmap.WritePixels(rect, pixels, stride, (int)mesh.srcOffsetX, (int)mesh.srcOffsetY);
                }
            }
            bitmap.Freeze();
            return new ImageData(bitmap, info);
        }

        public override void Write(Stream file, ImageData image) {
            throw new NotImplementedException("AtxFormat.Write not implemented");
        }
    }
}
