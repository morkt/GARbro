//! \file       AudioClip.cs
//! \date       2018 Aug 31
//! \brief      Unity engine audio clip deserializer
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

namespace GameRes.Formats.Unity
{
    enum AudioFormat : int
    {
        Unknown = 0,
        Acc = 1,
        Aiff = 2,
        It = 10,
        Mod = 12,
        Mpeg = 13,
        OggVorbis = 14,
        S3M = 17,
        Wav = 20,
        Xm = 21,
        Xma = 22,
        Vag = 23,
        AudioQueue = 24,
    }

    internal class AudioClip
    {
        public string   m_Name;
        public int      m_LoadType;
        public int      m_Channels;
        public int      m_Frequency;
        public int      m_BitsPerSample;
        public float    m_Length;
        public bool     m_IsTrackerFormat;
        public int      m_SubsoundIndex;
        public bool     m_PreloadAudioData;
        public bool     m_LoadInBackground;
        public bool     m_Legacy3D;
        public string   m_Source;
        public long     m_Offset;
        public long     m_Size;
        public int      m_CompressionFormat;

        public void Load (AssetReader reader)
        {
            m_Name = reader.ReadString();
            reader.Align();
            if (reader.Format > 9)
            {
                m_LoadType = reader.ReadInt32();
                m_Channels = reader.ReadInt32();
                m_Frequency = reader.ReadInt32();
                m_BitsPerSample = reader.ReadInt32();
                m_Length = reader.ReadFloat();
                m_IsTrackerFormat = reader.ReadBool();
                reader.Align();
                m_SubsoundIndex = reader.ReadInt32();
                m_PreloadAudioData = reader.ReadBool();
                m_LoadInBackground = reader.ReadBool();
                m_Legacy3D = reader.ReadBool();
                reader.Align();
                m_Source = reader.ReadString();
                reader.Align();
                m_Offset = reader.ReadInt64();
                m_Size = reader.ReadInt64();
                m_CompressionFormat = reader.ReadInt32();
            }
            else
            {
                m_LoadType = reader.ReadInt32();
                m_CompressionFormat = reader.ReadInt32();
                reader.ReadInt32();
                reader.ReadInt32();
                m_Size = reader.ReadUInt32();
            }
        }
    }

    internal class StreamingInfo
    {
        public uint     Offset;
        public uint     Size;
        public string   Path;

        public void Load (AssetReader reader)
        {
            Offset = reader.ReadUInt32();
            Size = reader.ReadUInt32();
            Path = reader.ReadString();
        }
    }
}
