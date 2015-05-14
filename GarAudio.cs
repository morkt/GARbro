//! \file       GarAudio.cs
//! \date       Thu May 14 13:58:03 2015
//! \brief      GARbro audio formats conversions.
//
// Copyright (C) 2015 by morkt
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

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using GameRes;
using GARbro.GUI.Strings;

namespace GARbro.GUI
{
    public partial class MainWindow : Window
    {
        void ExtractAudio (ArcFile arc, Entry entry)
        {
            using (var file = arc.OpenEntry (entry))
            using (var sound = AudioFormat.Read (file))
            {
                if (null == sound)
                    throw new InvalidFormatException (string.Format ("{1}: {0}", guiStrings.MsgUnableInterpret, entry.Name));
                ConvertAudio (entry.Name, sound);
            }
        }

        public static readonly HashSet<string> CommonAudioFormats = new HashSet<string> { "wav", "mp3", "ogg" };

        void ConvertAudio (string entry_name, SoundInput input)
        {
            string source_format = input.SourceFormat;
            if (CommonAudioFormats.Contains (source_format))
            {
                string output_name = Path.ChangeExtension (entry_name, source_format);
                using (var output = ArchiveFormat.CreateFile (output_name))
                {
                    input.Source.Position = 0;
                    input.Source.CopyTo (output);
                }
            }
            else
            {
                var wav_format = FormatCatalog.Instance.AudioFormats.Where (f => f.Tag == "WAV").First();
                string output_name = Path.ChangeExtension (entry_name, "wav");
                using (var output = ArchiveFormat.CreateFile (output_name))
                    wav_format.Write (input, output);
            }
        }
    }
}
