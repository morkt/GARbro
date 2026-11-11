﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Remoting.Metadata.W3cXsd2001;
using System.Text;
using System.Threading.Tasks;

namespace SchemeTool
{
    class Program
    {
        static void Main(string[] args)
        {
            // Load database
            using (Stream stream = File.OpenRead(".\\GameData\\Formats.dat"))
            {
                GameRes.FormatCatalog.Instance.DeserializeScheme(stream);
            }
#if false
            using (Stream stream = File.Create(".\\GameData\\Formats.json"))
            {
                GameRes.FormatCatalog.Instance.SerializeSchemeJson(stream);
                return;
            }
#endif
            GameRes.Formats.KiriKiri.Xp3Opener format = GameRes.FormatCatalog.Instance.ArcFormats
                .FirstOrDefault(a => a is GameRes.Formats.KiriKiri.Xp3Opener) as GameRes.Formats.KiriKiri.Xp3Opener;

            if (format != null)
            {
                GameRes.Formats.KiriKiri.Xp3Scheme scheme = format.Scheme as GameRes.Formats.KiriKiri.Xp3Scheme;

                // Add scheme information here

#if true
                byte[] cb = File.ReadAllBytes(@"MEM_10014628_00001000.mem");
                var cb2 = MemoryMarshal.Cast<byte, uint>(cb);
                for (int i = 0; i < cb2.Length; i++)
                    cb2[i] = ~cb2[i];
                var cs = new GameRes.Formats.KiriKiri.CxScheme
                {
                    Mask = 0x000,
                    Offset = 0x000,
                    PrologOrder = new byte[] { 0, 1, 2 },
                    OddBranchOrder = new byte[] { 0, 1, 2, 3, 4, 5 },
                    EvenBranchOrder = new byte[] { 0, 1, 2, 3, 4, 5, 6, 7 },
                    ControlBlock = cb2.ToArray()
                };
                var crypt = new GameRes.Formats.KiriKiri.HxCrypt(cs);
                crypt.RandomType = 0;
                crypt.FilterKey = 0x0000000000000000;
                crypt.NamesFile = "HxNames.lst";
                var keyA1 = SoapHexBinary.Parse("0000000000000000000000000000000000000000000000000000000000000000").Value;
                var keyA2 = SoapHexBinary.Parse("00000000000000000000000000000000").Value;
                var keyB1 = SoapHexBinary.Parse("0000000000000000000000000000000000000000000000000000000000000000").Value;
                var keyB2 = SoapHexBinary.Parse("00000000000000000000000000000000").Value;
                crypt.IndexKeyDict = new Dictionary<string, GameRes.Formats.KiriKiri.HxIndexKey>()
                {
                    { "data.xp3", new GameRes.Formats.KiriKiri.HxIndexKey { Key1 = keyA1, Key2 = keyA2 } },
                    { "update.xp3", new GameRes.Formats.KiriKiri.HxIndexKey { Key1 = keyB1, Key2 = keyB2 } },
                };
#else
                GameRes.Formats.KiriKiri.ICrypt crypt = new GameRes.Formats.KiriKiri.XorCrypt(0x00);
#endif

                // scheme.KnownSchemes.Add("game title", crypt);
            }

            var gameMap = typeof(GameRes.FormatCatalog).GetField("m_game_map", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .GetValue(GameRes.FormatCatalog.Instance) as Dictionary<string, string>;

            if (gameMap != null)
            {
                // Add file name here
                // gameMap.Add("game.exe", "game title");
            }

            // Save database
            using (Stream stream = File.Create(".\\GameData\\Formats.dat"))
            {
                GameRes.FormatCatalog.Instance.SerializeScheme(stream);
            }
        }
    }
}