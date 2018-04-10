//! \file       EmbeddedResource.cs
//! \date       2018 Apr 10
//! \brief      Embedded resource loader.
//
// Copyright (C) 2018 by morkt
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
using System.IO;

namespace GameRes.Formats
{
    public static class EmbeddedResource
    {
        /// <summary>
        /// Open embedded resource as a stream.
        /// </summary>
        public static Stream Open (string name, Type owner)
        {
            var assembly = owner.Assembly;
            string qualified_name = owner.Namespace + '.' + name;
            return assembly.GetManifestResourceStream (qualified_name);
        }

        /// <summary>
        /// Load binary embedded resource as a byte array.
        /// </summary>
        public static byte[] Load (string name, Type owner)
        {
            using (var stream = Open (name, owner))
            {
                if (null == stream)
                    return null;
                var res = new byte[stream.Length];
                stream.Read (res, 0, res.Length);
                return res;
            }
        }
    }
}
