//! \file       PackageFile.cs
//! \date       2018 Aug 30
//! \brief      Unity GX4Lib deserializer.
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
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;

namespace GameRes.Gx4Lib
{
    public class PackageFile
    {
        public PFHeaders Deserialize (Stream input)
        {
            var bin = new BinaryFormatter { Binder = new Gx4TypeBinder() };
            return bin.Deserialize (input) as PFHeaders;
        }
    }

    [Serializable]
    public class PFHeader
    {
        public string   FileName;
        public long     readStartBytePos;
        public long     ByteLength;
    }

    [Serializable]
    public class PFHeaders
    {
        public PFHeader[] headers;
    }

    [Serializable]
    public class PFAudioHeaders : PFHeaders
    {
    }

    [Serializable]
    public class PFImageHeaders : PFHeaders
    {
    }

    internal class Gx4TypeBinder : SerializationBinder
    {
        public override Type BindToType (string assemblyName, string typeName)
        {
            if ("GX4Lib" == assemblyName)
            {
                if (typeName.StartsWith ("GX4.PackageFile`1+PFHeaders[["))
                {
                    if (0 == string.Compare (typeName, 29, "UnityEngine.AudioClip", 0, 21))
                        return typeof(PFAudioHeaders);
                    if (0 == string.Compare (typeName, 29, "UnityEngine.Texture2D", 0, 21))
                        return typeof(PFImageHeaders);
                    return typeof(PFHeaders);
                }
                else if (typeName.StartsWith ("GX4.PackageFile`1+PFHeader[["))
                {
                    return typeof(PFHeader);
                }
            }
            return null;
        }
    }
}
