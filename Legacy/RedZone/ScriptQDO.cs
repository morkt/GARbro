//! \file       ScriptQDO.cs
//! \date       2023 Sep 21
//! \brief      RED-ZONE binary script.
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

using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.RedZone
{
    [Export(typeof(ScriptFormat))]
    public class QdoOpener : GenericScriptFormat
    {
        public override string         Tag => "QDO";
        public override string Description => "Red-Zone script file";
        public override uint     Signature => 0x5F4F4451; // 'QDO_SHO'

        public override bool IsScript (IBinaryStream file)
        {
            var header = file.ReadHeader (8);
            return header.AsciiEqual ("QDO_SHO");
        }

        const int ScriptDataPos = 0x0E;

        public override Stream ConvertFrom (IBinaryStream file)
        {
            var data = file.ReadBytes ((int)file.Length);
            if (data[0xC] != 0)
            {
                for (int i = ScriptDataPos; i < data.Length; ++i)
                {
                    data[i] = (byte)~(data[i] - 13);
                }
                data[0xC] = 0;
            }
            return new BinMemoryStream (data, file.Name);
        }
        
        public override Stream ConvertBack (IBinaryStream file)
        {
            var data = file.ReadBytes ((int)file.Length);
            if (data[0xC] == 0)
            {
                for (int i = ScriptDataPos; i < data.Length; ++i)
                {
                    data[i] = (byte)(~data[i] + 13);
                }
                data[0xC] = 1;
            }
            return new BinMemoryStream (data, file.Name);
        }
    }
}
