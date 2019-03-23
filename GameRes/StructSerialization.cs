//! \file       ReadStruct.cs
//! \date       2019 Mar 22
//! \brief      Struct deserialization from a binary stream.
//
// Copyright (C) 2019 by morkt
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
using System.Reflection;
using System.Text;

namespace GameRes.Utility.Serialization
{
    public static class BinaryStreamExtension
    {
        public static void ReadStruct<Type> (this IBinaryStream input, out Type data) where Type : new()
        {
            object tmp = new Type();
            ReadStruct (input, typeof(Type), ref tmp);
            data = (Type)tmp;
        }

        internal static void ReadStruct (this IBinaryStream input, Type type, ref object instance)
        {
            if (type.IsPrimitive || type.IsArray)
                throw new ArgumentException ("Invalid deserialization type.");

            var fields = type.GetFields (BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            foreach (var field in fields)
            {
                if (field.IsDefined (typeof(IgnoreAttribute)))
                    continue;

                if (field.FieldType.IsArray)
                    throw new NotSupportedException ("Array serialization is not supported.");

                var field_value = ReadField (input, field);
                field.SetValue (instance, field_value);
            }
        }

        internal static object ReadField (this IBinaryStream input, FieldInfo field)
        {
            var field_type = field.FieldType;
            var type_code = Type.GetTypeCode (field_type);
            switch (type_code)
            {
            case TypeCode.SByte:    return input.ReadInt8();
            case TypeCode.Byte:     return input.ReadUInt8();
            case TypeCode.Int16:    return input.ReadInt16();
            case TypeCode.UInt16:   return input.ReadUInt16();
            case TypeCode.Int32:    return input.ReadInt32();
            case TypeCode.UInt32:   return input.ReadUInt32();
            case TypeCode.Int64:    return input.ReadInt64();
            case TypeCode.UInt64:   return input.ReadUInt64();
            case TypeCode.Char:     return (char)input.ReadInt16();

            case TypeCode.String:
                var cstring_attr = field.GetCustomAttribute<CStringAttribute>();
                if (cstring_attr != null)
                {
                    var encoding = cstring_attr.Encoding ?? Encodings.cp932;
                    if (cstring_attr.IsLengthDefined)
                        return input.ReadCString (cstring_attr.Length, encoding);
                    else
                        return input.ReadCString (encoding);
                }
                throw new FormatException ("Serialization method for string field is not defined.");

            case TypeCode.Object:
                object val = Activator.CreateInstance (field_type);
                ReadStruct (input, field_type, ref val); // FIXME check object graph for cycles
                return val;

            default:
                throw new NotSupportedException ("Not supported serialization type.");
            }
        }
    }

    public class IgnoreAttribute : Attribute { }

    public class CStringAttribute : Attribute
    {
        public int      Length = -1; // by default string is limited by null byte
        public Encoding Encoding;

        public bool IsLengthDefined { get { return Length >= 0; } }
    }
}
