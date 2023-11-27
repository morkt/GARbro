//! \file       ScriptISD.cs
//! \date       2023 Aug 25
//! \brief      Ice Soft compressed binary script.
//
// Copyright (C) 2023 by morkt
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

using GameRes.Formats.Ankh;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.Ice
{
    [Export(typeof(ScriptFormat))]
    public class IsdScript : GenericScriptFormat
    {
        public override string         Tag { get => "ISD"; }
        public override string Description { get => "Ice Soft binary script"; }
        public override uint     Signature { get => 0x01575054; } // 'TPW'

        public override bool IsScript (IBinaryStream file)
        {
            return file.Signature == Signature;
        }

        public override Stream ConvertFrom (IBinaryStream file)
        {
            file.Position = 4;
            int unpacked_size = file.ReadInt32();
            var data = new byte[unpacked_size];
            GrpOpener.UnpackTpw (file, data);
            return new BinMemoryStream (data, file.Name);
        }

        public override Stream ConvertBack (IBinaryStream file)
        {
            throw new System.NotImplementedException();
        }
    }
}
