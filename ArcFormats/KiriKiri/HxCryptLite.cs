using System;
using System.Collections.Generic;
using System.IO;

namespace GameRes.Formats.KiriKiri
{
    /// <summary>
    /// Wamsoft HxCrypt无Hash青春版 (CxdecV1.9)
    /// </summary>
    [Serializable]
    public class HxCryptLite : CxEncryption
    {
        private byte[] mHeaderKey;
        private long mHeaderSplitPosition;

        private bool mFileCryptFlag;
        private int mRandomType;

        public HxCryptLite(CxScheme scheme) : base(scheme) { }

        /// <summary>
        /// 设置首次解密参数
        /// </summary>
        /// <param name="key">长度大于等于8时启用, 否则不启用</param>
        /// <param name="splitPos"></param>
        public void SetHeaderDecryptParam(byte[] key, long splitPos)
        {
            this.mHeaderKey = key;
            this.mHeaderSplitPosition = splitPos;
        }

        /// <summary>
        /// 设置文件解密中的单字节解密模式
        /// </summary>
        public void SetSingleByteCryptFlag(bool flag)
        {
            this.mFileCryptFlag = flag;
        }

        /// <summary>
        /// 设置随机数算法模式
        /// </summary>
        public void SetRandomType(int type)
        {
            this.mRandomType = type;
        }

        public override void Init(ArcFile arc)
        {
            return;
        }

        public override byte Decrypt(Xp3Entry entry, long offset, byte value)
        {
            var buf = new byte[1] { value };
            this.Decrypt(entry, offset, buf, 0, 1);
            return buf[0];
        }


        public override void Decrypt(Xp3Entry entry, long offset, byte[] buffer, int pos, int count)
        {
            if (this.mHeaderKey != null)
            {
                HxHeaderDecryptor headerDecryptor = new HxHeaderDecryptor(entry.Hash, this.mHeaderKey, this.mHeaderSplitPosition);
                headerDecryptor.Decrypt(buffer, offset, pos, count);
            }

            ulong key1, key2;
            {
                Tuple<uint, uint> ret1 = ExecuteXCode(entry.Hash);
                Tuple<uint, uint> ret2 = ExecuteXCode(entry.Hash ^ (entry.Hash >> 16));

                key1 = ((ulong)ret1.Item2 << 32) | ret1.Item1;
                key2 = ((ulong)ret2.Item2 << 32) | ret2.Item1;
            }

            long splitPosition = m_offset + (entry.Hash & m_mask);

            HxFileDecryptor fileDecryptor1 = new HxFileDecryptor(key1, this.mFileCryptFlag);
            HxFileDecryptor fileDecryptor2 = new HxFileDecryptor(key2, this.mFileCryptFlag);

            if (splitPosition > offset)
            {
                if (splitPosition < offset + count)
                {
                    long blockLen1 = splitPosition - offset;
                    long blockLen2 = offset + count - splitPosition;

                    fileDecryptor1.Decrypt(buffer, offset, pos, (int)blockLen1);
                    fileDecryptor2.Decrypt(buffer, offset + blockLen1, (int)(pos + blockLen1), (int)blockLen2);
                }
                else
                {
                    fileDecryptor1.Decrypt(buffer, offset, pos, count);
                }
            }
            else
            {
                fileDecryptor2.Decrypt(buffer, offset, pos, count);
            }
        }

        public override void Encrypt(Xp3Entry entry, long offset, byte[] values, int pos, int count)
        {
            throw new NotImplementedException();
        }

        internal override CxProgram NewProgram(uint seed)
        {
            return new HxProgramLite(seed, this.ControlBlock, this.mRandomType);
        }
    }

    internal class HxFileDecryptor
    {
        private long mSplitPosition1;
        private long mSplitPosition2;
        private uint mGlobalKey;
        private byte mKey1;
        private byte mKey2;

        public HxFileDecryptor(ulong key, bool fileKeyFlag)
        {
            byte[] keyPtr = BitConverter.GetBytes(key);

            this.mGlobalKey = keyPtr[0];
            this.mKey1 = keyPtr[1];
            this.mKey2 = keyPtr[2];

            this.mSplitPosition1 = BitConverter.ToUInt16(keyPtr, 6);
            this.mSplitPosition2 = BitConverter.ToUInt16(keyPtr, 4);

            if (this.mSplitPosition1 == this.mSplitPosition2)
            {
                this.mSplitPosition2 += 1;
            }

            if (this.mGlobalKey == 0)
            {
                this.mGlobalKey = 1;
            }

            this.mGlobalKey *= 0x01010101;

            if (fileKeyFlag)
            {
                this.mKey1 = 0;
                this.mKey2 = 0;
            }
        }


        public unsafe void Decrypt(byte[] data, long offset, int position, int count)
        {
            int dataLen = count;

            if (dataLen == 0)
            {
                return;
            }

            //全局解密
            {
                byte[] key = BitConverter.GetBytes(this.mGlobalKey);

                int keyIndex = (int)(offset & 3);

                for (int i = 0; i < dataLen; ++i)
                {
                    data[i + position] ^= key[keyIndex];

                    ++keyIndex;
                    keyIndex &= 3;
                }
            }

            //第一个解密点
            if (this.mSplitPosition1 >= offset && this.mSplitPosition1 < offset + dataLen)
            {
                data[(int)(this.mSplitPosition1 - offset) + position] ^= this.mKey1;
            }
            //第二个解密点
            if (this.mSplitPosition2 >= offset && this.mSplitPosition2 < offset + dataLen)
            {
                data[(int)(this.mSplitPosition2 - offset) + position] ^= this.mKey2;
            }
        }
    }


    internal class HxHeaderDecryptor
    {
        private byte[] mKey = null;  // 解密Key
        private long mSplitPosition = 0;     // 分界点

        public HxHeaderDecryptor(uint hash, byte[] key, long splitPos)
        {
            if (key != null && key.Length >= 8)
            {
                this.mSplitPosition = splitPos;

                uint[] keyPtr = new uint[2] { BitConverter.ToUInt32(key, 0), BitConverter.ToUInt32(key, 4) };

                uint s0 = hash ^ keyPtr[1];
                uint s1 = hash ^ (hash << 13);
                uint s2 = s1 ^ (s1 >> 17);
                uint s3 = s2 ^ (s2 << 5) ^ keyPtr[0];

                this.mKey = BitConverter.GetBytes(((ulong)s3 << 32) | s0);
            }
        }

        public void Decrypt(byte[] data, long offset, int position, int count)
        {
            if (this.mKey is null)
            {
                return;
            }

            int keyLen = this.mKey.Length;
            int dataLen = count;

            //起始解密位置
            long startPos = offset;
            if (startPos <= this.mSplitPosition)
            {
                startPos = this.mSplitPosition;
            }

            //结束解密位置
            long endPos = offset + dataLen;
            if (endPos >= this.mSplitPosition + keyLen)
            {
                endPos = this.mSplitPosition + keyLen;
            }

            if (startPos >= endPos)
            {
                return;
            }

            //解密长度与解密起始索引
            long decryptLen = endPos - startPos;
            long keyStartIndex = startPos - this.mSplitPosition;
            long dataStartIndex = startPos - offset + position;

            //解密
            for (int i = 0; i < decryptLen; ++i)
            {
                data[(int)(dataStartIndex + i)] ^= this.mKey[keyStartIndex + i];
            }
        }
    }

    internal class HxProgramLite : CxProgram
    {
        private readonly int mRandomType;   //随机数方法类型
        private readonly uint[] mRandomBlock;     //随机子表
        private int mBlockPosition;     //表当前位置

        public HxProgramLite(uint seed, uint[] control_block, int random_method) : base(seed, control_block)
        {
            this.mRandomType = random_method;

            //生成子表
            {
                this.mBlockPosition = 0x270;

                uint[] block = new uint[0x270];

                block[0] = seed;

                for (int i = 1; i < block.Length; ++i)
                {
                    block[i] = (block[i - 1] ^ (block[i - 1] >> 0x1E)) * 0x6C078965 + (uint)i;
                }

                this.mRandomBlock = block;
            }
        }

        public override uint GetRandom()
        {
            if (this.mRandomType == 0)
            {
                return base.GetRandom();
            }
            else
            {
                return this.GetRandomNew();
            }
        }

        private uint GetRandomNew()
        {
            if (this.mBlockPosition == this.mRandomBlock.Length)
            {
                this.TransformBlock();
            }

            uint s0 = this.mRandomBlock[this.mBlockPosition];
            uint s1 = (s0 >> 11) ^ s0;
            uint s2 = ((s1 & 0xFF3A58AD) << 7) ^ s1;
            uint s3 = ((s2 & 0xFFFFDF8C) << 15) ^ s2;
            uint s4 = (s3 >> 18) ^ s3;

            ++this.mBlockPosition;

            return s4;
        }

        private void TransformBlock()
        {
            this.mBlockPosition = 0;

            uint[] block = this.mRandomBlock;

            //0-0xE2
            for (int i = 0; i < 0xE3; ++i)
            {
                uint s0 = (block[i + 1] & 1) != 0 ? 0x9908B0DF : 0;
                uint s1 = (((block[i] ^ block[i + 1]) & 0x7FFFFFFE) ^ block[i]) >> 1;
                uint s2 = s0 ^ s1 ^ block[i + 0x18D];

                block[i] = s2;
            }

            //0xE3-0x26E
            for (int i = 0; i < 0x18C; ++i)
            {
                uint s0 = (block[i + 1 + 0xE3] & 1) != 0 ? 0x9908B0DF : 0;
                uint s1 = (((block[i + 0xE3] ^ block[i + 1 + 0xE3]) & 0x7FFFFFFE) ^ block[i + 0xE3]) >> 1;
                uint s2 = s0 ^ s1 ^ block[i];

                block[i + 0xE3] = s2;
            }

            //0x26F
            {
                uint s0 = (block[0] & 1) != 0 ? 0x9908B0DF : 0;
                uint s1 = (((block[0x26F] ^ block[0]) & 0x7FFFFFFE) ^ block[0x26F]) >> 1;
                uint s2 = s0 ^ s1 ^ block[0x18C];

                block[0x26F] = s2;
            }
        }
    }
}
