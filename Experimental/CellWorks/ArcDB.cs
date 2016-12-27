//! \file       ArcDB.cs
//! \date       Sun Dec 04 23:39:13 2016
//! \brief      ALL-TiME sqlite-backed resource archives.
//
// Copyright (C) 2016 by morkt
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
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace GameRes.Formats.CellWorks
{
    [Export(typeof(ArchiveFormat))]
    public class IgsDatOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DAT/IGS"; } }
        public override string Description { get { return "IGS engine resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        internal static readonly string[] KnownPasswords = { "igs sample", "igs samp1e" };

        public override ArcFile TryOpen (ArcView file)
        {
            if (VFS.IsVirtual || !file.Name.EndsWith (".dat", StringComparison.InvariantCultureIgnoreCase))
                return null;
            var db_files = VFS.GetFiles (VFS.CombinePath (VFS.GetDirectoryName (file.Name), "*.db"));
            if (!db_files.Any())
                return null;
            using (var igs = new IgsDbReader (file.Name))
            {
                foreach (var db_name in db_files.Select (e => e.Name))
                {
                    int arc_id;
                    if (igs.GetArchiveId (db_name, out arc_id))
                    {
                        var dir = igs.ReadIndex (arc_id);
                        if (0 == dir.Count)
                            return null;
                        return new ArcFile (file, this, dir);
                    }
                }
                return null;
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            using (var aes = Aes.Create())
            {
                var name_bytes = Encoding.UTF8.GetBytes (entry.Name);
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.Key = CreateKey (32, name_bytes);
                aes.IV = CreateKey (16, name_bytes);
                using (var decryptor = aes.CreateDecryptor())
                using (var enc = arc.File.CreateStream (entry.Offset, 0x110))
                using (var input = new CryptoStream (enc, decryptor, CryptoStreamMode.Read))
                {
                    var header = new byte[Math.Min (entry.Size, 0x100u)];
                    input.Read (header, 0, header.Length);
                    if (entry.Size <= 0x100)
                        return new BinMemoryStream (header);
                    var rest = arc.File.CreateStream (entry.Offset+0x110, entry.Size-0x100);
                    return new PrefixStream (header, rest);
                }
            }
        }

        internal static byte[] CreateKey (int length, byte[] src)
        {
            var key = new byte[length];
            Buffer.BlockCopy (src, 0, key, 0, Math.Min (src.Length, length));
            for (int i = length; i < src.Length; ++i)
                key[i % length] ^= src[i];
            return key;
        }
    }

    internal sealed class IgsDbReader : IDisposable
    {
        SQLiteConnection    m_conn;
        SQLiteCommand       m_arc_cmd;

        public IgsDbReader (string arc_name)
        {
            m_conn = new SQLiteConnection();
            m_arc_cmd = m_conn.CreateCommand();
            m_arc_cmd.CommandText = @"SELECT id FROM archives WHERE name=?";
            m_arc_cmd.Parameters.Add (m_arc_cmd.CreateParameter());
            m_arc_cmd.Parameters[0].Value = Path.GetFileNameWithoutExtension (arc_name);
        }

        public bool GetArchiveId (string db_name, out int arc_id)
        {
            m_conn.ConnectionString = string.Format ("Data Source={0};Read Only=true;", db_name);
            foreach (var password in IgsDatOpener.KnownPasswords)
            {
                m_conn.SetPassword (password);
                m_conn.Open();
                try
                {
                    using (var reader = m_arc_cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            arc_id = reader.GetInt32 (0);
                            return true;
                        }
                    }
                    // command executed successfully, but returned no rows
                    m_conn.Close();
                    break;
                }
                catch (SQLiteException)
                {
                    // ignore open errors, try another password
                }
                m_conn.Close();
            }
            arc_id = -1;
            return false;
        }

        public List<Entry> ReadIndex (int arc_id)
        {
            // tables: m_types file_infos images archives
            // m_types: id type -> [normal, image, voice]
            // archives: id name
            // file_infos: id name filepath size offset typeID archiveID
            using (var cmd = m_conn.CreateCommand())
            {
                cmd.CommandText = @"SELECT filepath,offset,size FROM file_infos WHERE archiveID=?";
                cmd.Parameters.Add (cmd.CreateParameter());
                cmd.Parameters[0].Value = arc_id;
                using (var reader = cmd.ExecuteReader())
                {
                    var dir = new List<Entry>();
                    while (reader.Read())
                    {
                        var name = reader.GetString (0);
                        var entry = FormatCatalog.Instance.Create<Entry> (name);
                        entry.Offset = reader.GetInt64 (1);
                        entry.Size = (uint)reader.GetInt32 (2);
                        dir.Add (entry);
                    }
                    return dir;
                }
            }
        }

        bool _disposed = false;
        public void Dispose ()
        {
            if (!_disposed)
            {
                m_arc_cmd.Dispose();
                m_conn.Dispose();
                _disposed = true;
            }
            GC.SuppressFinalize (this);
        }
    }
}
