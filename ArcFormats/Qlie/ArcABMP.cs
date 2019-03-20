//! \file       ArcABMP.cs
//! \date       Wed Oct 28 02:51:46 2015
//! \brief      QLIE mulit-frame images.
//
// Copyright (C) 2015-2017 by morkt
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
using System.Text.RegularExpressions;
using GameRes.Utility;

namespace GameRes.Formats.Qlie
{
    [Export(typeof(ArchiveFormat))]
    public class AbmpOpener : ArchiveFormat
    {
        public override string         Tag { get { return "ABMP/QLIE"; } }
        public override string Description { get { return "QLIE engine multi-frame archive"; } }
        public override uint     Signature { get { return 0x706D6261; } } // 'abmp'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public AbmpOpener ()
        {
            Extensions = new string[] { "b" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int version = file.View.ReadByte (4) * 10 + file.View.ReadByte (5) - '0' * 11;
            if (file.View.ReadByte (6) != 0 || version < 10 || version > 12)
                return null;
            using (var reader = new AbmpReader (file, version))
            {
                var dir = reader.ReadIndex();
                if (null == dir || 0 == dir.Count)
                    return null;
                return new ArcFile (file, this, dir);
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            if (0xFF435031 != arc.File.View.ReadUInt32 (entry.Offset))
                return base.OpenEntry (arc, entry);
            var data = arc.File.View.ReadBytes (entry.Offset, entry.Size);
            data = PackOpener.Decompress (data) ?? data;
            return new BinMemoryStream (data, entry.Name);
        }
    }

    internal sealed class AbmpReader : IDisposable
    {
        ArcView             m_file;
        IBinaryStream       m_input;
        string              m_base_name;
        int                 m_version;
        List<Entry>         m_dir;

        public AbmpReader (ArcView file, int version)
        {
            m_file = file;
            m_input = file.CreateStream();
            m_base_name = Path.GetFileNameWithoutExtension (file.Name);
            m_version = version;
            m_dir = new List<Entry>();
        }

        public List<Entry> ReadIndex ()
        {
            m_input.Position = 0x10;
            int n = 0;
            var type_buf = new byte[0x10];
            while (0x10 == m_input.Read (type_buf, 0, 0x10))
            {
                if (Binary.AsciiEqual (type_buf, "abdata"))
                {
                    uint size = m_input.ReadUInt32(); 
                    var entry = new Entry {
                        Name = string.Format ("{0}#{1}.dat", m_base_name, n++),
                        Offset = m_input.Position,
                        Size = size,
                    };
                    m_dir.Add (entry);
                    Skip (size);
                }
                else if (Binary.AsciiEqual (type_buf, "abimage10\0") ||
                         Binary.AsciiEqual (type_buf, "absound10\0"))
                {
                    int count = m_input.ReadByte();
                    for (int i = 0; i < count; ++i)
                    {
                        if (0x10 != m_input.Read (type_buf, 0, 0x10))
                            break;
                        var tag = Binary.GetCString (type_buf, 0, 0x10, Encoding.ASCII);
                        string name = null;
                        if ("abimgdat15" == tag)
                        {
                            int version = m_input.ReadInt32();
                            int name_length = m_input.ReadUInt16();
                            if (name_length > 0)
                            {
                                var name_bytes = m_input.ReadBytes (name_length*2);
                                name = Encoding.Unicode.GetString (name_bytes);
                            }
                            name_length = m_input.ReadUInt16();
                            if (name_length > 0)
                            {
                                if (string.IsNullOrEmpty (name))
                                    name = m_input.ReadCString (name_length);
                                else
                                    Skip ((uint)name_length);
                            }
                            byte type = m_input.ReadUInt8();
                            /*
                            case 0:   ".bmp"
                            case 1:   ".jpg"
                            case 2:
                            case 3:   ".png"
                            case 4:   ".m"
                            case 5:   ".argb"
                            case 6:   ".b"
                            case 7:   ".ogv"
                            case 8:   ".mdl"
                            */
                            if (2 == version)
                                Skip (0x1D);
                            else
                                Skip (0x11);
                        }
                        else if ("absnddat12" == tag)
                        {
                            int version = m_input.ReadInt32();
                            int name_length = m_input.ReadUInt16();
                            if (name_length > 0)
                            {
                                var name_bytes = m_input.ReadBytes (name_length*2);
                                name = Encoding.Unicode.GetString (name_bytes);
                            }
                            if (m_input.Length - m_input.Position <= 7)
                                break;
                            Skip (7);
                        }
                        else
                        {
                            int name_length = m_input.ReadUInt16();
                            name = m_input.ReadCString (name_length);

                            if (tag != "abimgdat10" && tag != "absnddat10")
                            {
                                Skip (m_input.ReadUInt16());
                                if ("abimgdat13" == tag)
                                    Skip (0x0C);
                                else if ("abimgdat14" == tag)
                                    Skip (0x4C);
                            }
                            m_input.ReadByte();
                        }
                        var size = m_input.ReadUInt32();
                        if (0 != size)
                        {
                            if (string.IsNullOrEmpty (name))
                                name = string.Format ("{0}#{1}", m_base_name, n++);
                            else
                                name = s_InvalidChars.Replace (name, "_");
                            var entry = new Entry {
                                Name = name,
                                Type = tag.StartsWith ("abimg") ? "image" : tag.StartsWith ("absnd") ? "audio" : "",
                                Offset = m_input.Position,
                                Size = size,
                            };
                            if (entry.CheckPlacement (m_file.MaxOffset))
                            {
                                DetectFileType (m_file, entry);
                                m_dir.Add (entry);
                            }
                        }
                        Skip (size);
                    }
                }
                else
                {
                    var entry = new Entry {
                        Name = string.Format ("{0}#{1}#{2}", m_base_name, n++, GetTypeName (type_buf)),
                        Offset = m_input.Position,
                        Size = (uint)(m_file.MaxOffset - m_input.Position),
                    };
                    m_dir.Add (entry);
                    Skip (entry.Size);
                }
            }
            return m_dir;
        }

        void Skip (uint amount)
        {
            m_input.Seek (amount, SeekOrigin.Current);
        }

        static internal void DetectFileType (ArcView file, Entry entry)
        {
            uint signature = file.View.ReadUInt32 (entry.Offset);
            var res = AutoEntry.DetectFileType (signature);
            if (null != res)
                entry.ChangeType (res);
        }

        static string GetTypeName (byte[] type_buf)
        {
            int n = 0;
            while (n < type_buf.Length)
            {
                if (0 == type_buf[n])
                    break;
                if (type_buf[n] < 0x20 || type_buf[n] > 0x7E)
                    return "unknown";
            }
            if (0 == n)
                return "";
            return Encoding.ASCII.GetString (type_buf, 0, n).Trim();
        }

        static readonly Regex s_InvalidChars = new Regex (@"[:/\\*?]");

        #region IDisposable Members
        bool _disposed = false;
        public void Dispose ()
        {
            if (!_disposed)
            {
                m_input.Dispose();
                _disposed = true;
            }
        }
        #endregion
    }

    [Export(typeof(ArchiveFormat))]
    public class Abmp7Opener : AbmpOpener
    {
        public override string         Tag { get { return "ABMP7"; } }
        public override string Description { get { return "QLIE engine multi-frame image archive"; } }
        public override uint     Signature { get { return 0x504D4241; } } // 'ABMP'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (file.View.ReadUInt16 (4) != '7')
                return null;
            string base_name = Path.GetFileNameWithoutExtension (file.Name);
            uint offset = 0xC;
            var dir = new List<Entry>();
            uint size = file.View.ReadUInt32 (offset);
            offset += 4;
            var entry = new Entry {
                Name = string.Format ("{0}#0.dat", base_name),
                Offset = offset,
                Size = size,
            };
            if (!entry.CheckPlacement (file.MaxOffset))
                return null;
            dir.Add (entry);
            offset += size;
            int n = 1;
            while (offset < file.MaxOffset)
            {
                size = file.View.ReadUInt32 (offset);
                if (0 == size)
                    break;
                offset += 4;
                entry = new Entry {
                    Name = string.Format ("{0}#{1}", base_name, n++),
                    Type = "image",
                    Offset = offset,
                    Size = size,
                };
                if (!entry.CheckPlacement (file.MaxOffset))
                    break;
                AbmpReader.DetectFileType (file, entry);
                dir.Add (entry);
                offset += size;
            }
            return new ArcFile (file, this, dir);
        }
    }
}
