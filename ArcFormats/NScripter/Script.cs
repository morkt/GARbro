//! \file       Script.cs
//! \date       2023 Aug 23
//! \brief      
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

namespace GameRes.Formats.NScripter
{
    [Export(typeof(ScriptFormat))]
    public class NSOpener : GenericScriptFormat
    {
        public override string         Tag { get => "NScripter"; }
        public override string Description { get => "NScripter engine script file"; }
        public override uint     Signature { get => 0; }

        public override bool IsScript (IBinaryStream file)
        {
            return VFS.IsPathEqualsToFileName (file.Name, "nscript.dat");
        }

        public override Stream ConvertFrom (IBinaryStream file)
        {
            return new XoredStream (file.AsStream, 0x84);
        }
        
        public override Stream ConvertBack (IBinaryStream file)
        {
            return new XoredStream (file.AsStream, 0x84);
        }
    }
}
