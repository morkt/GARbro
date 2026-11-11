using System;

namespace GameRes.Formats.Entergram
{
    internal static class QLZCompressor
    {
        public struct QLZHeader
        {
            public QLZHeader(byte[] src)
            {
                byte b = src[0];
                this.Compressible = (b & CONTAINER_Compressible) == CONTAINER_Compressible;
                if (this.Compressible)
                {
                    b -= CONTAINER_Compressible;
                }
                if (b != STATIC_HEADER_FIRSTBYTE)
                {
                    throw new Exception("Invalid QLZ Header: " + b.ToString());
                }
                this.CompressedSize = BitConverter.ToInt32(src, 1);
                this.RawSize = BitConverter.ToInt32(src, 5);
            }

            public const int HEADER_LENGTH = 9;
            public const byte STATIC_HEADER_FIRSTBYTE = 0x5E;
            public const byte CONTAINER_Compressible = 1;
            public const int Level = 3;

            public bool Compressible;
            public int CompressedSize;
            public int RawSize;
        }

        public static byte[] Decompress(byte[] compressed)
        {
            QLZCompressor.QLZHeader qlzheader = new QLZCompressor.QLZHeader(compressed);
            if (qlzheader.RawSize == 0)
            {
                return new byte[0];
            }
            byte[] array = new byte[qlzheader.RawSize];
            if (!qlzheader.Compressible)
            {
                Array.Copy(compressed, QLZHeader.HEADER_LENGTH, array, 0, qlzheader.RawSize);
            }
            else
            {
                QLZCompressor.Decompress_Unsafe(compressed, array, qlzheader.RawSize);
            }
            return array;
        }

        private unsafe static void Decompress_Unsafe(byte[] compressed, byte[] decompressed, int rawSize)
        {
            if (rawSize < decompressed.Length)
            {
                throw new Exception("Decompressed Array is not enough size");
            }

            fixed (byte* ptr = compressed)
            {
                fixed (byte* ptr2 = decompressed)
                {
                    int src = QLZHeader.HEADER_LENGTH;
                    int dst = 0;
                    uint cword_val = 1U;
                    int last_matchstart = rawSize - UNCONDITIONAL_MATCHLEN - UNCOMPRESSED_END - 1;
                    uint fetch = 0U;
                    for (; ; )
                    {
                        if (cword_val == 1U)
                        {
                            cword_val = ReadUInt32(ptr + src);
                            src += 4;
                            if (dst <= last_matchstart)
                            {
                                fetch = ReadUInt32(ptr + src);
                            }
                        }
                        if ((cword_val & 1U) == 1U)
                        {
                            cword_val >>= 1;
                            uint offset;
                            uint matchlen;
                            if ((fetch & 3U) == 0U)
                            {
                                offset = (fetch & 0xFFU) >> 2;
                                matchlen = 3U;
                                src++;
                            }
                            else if ((fetch & 2U) == 0U)
                            {
                                offset = (fetch & 0xFFFFU) >> 2;
                                matchlen = 3U;
                                src += 2;
                            }
                            else if ((fetch & 1U) == 0U)
                            {
                                offset = (fetch & 0xFFFFU) >> 6;
                                matchlen = ((fetch >> 2) & 0xFU) + 3U;
                                src += 2;
                            }
                            else if ((fetch & 0x7FU) != 3U)
                            {
                                offset = (fetch >> 7) & 0x1FFFFU;
                                matchlen = ((fetch >> 2) & 0x1FU) + 2U;
                                src += 3;
                            }
                            else
                            {
                                offset = fetch >> 0xF;
                                matchlen = ((fetch >> 7) & 0xFFU) + 3U;
                                src += 4;
                            }
                            uint num7 = (uint)((long)dst - (long)((ulong)offset));
                            ptr2[dst] = ptr2[num7];
                            (ptr2 + dst)[1] = (ptr2 + num7)[1];
                            (ptr2 + dst)[2] = (ptr2 + num7)[2];
                            int num8 = 3;
                            while ((long)num8 < (long)((ulong)matchlen))
                            {
                                (ptr2 + dst)[num8] = (ptr2 + num7)[num8];
                                num8++;
                            }
                            dst += (int)matchlen;
                            fetch = ReadUInt32(ptr + src);
                        }
                        else
                        {
                            if (dst > last_matchstart)
                            {
                                break;
                            }
                            ptr2[dst] = ptr[src];
                            dst++;
                            src++;
                            cword_val >>= 1;
                            fetch = (uint)((((int)fetch >> 8) & 0xFFFF) | ((int)(ptr + src)[2] << 0x10) | ((int)(ptr + src)[3] << 0x18));
                        }
                    }
                    while (dst <= rawSize - 1)
                    {
                        if (cword_val == 1U)
                        {
                            src += 4;
                            cword_val = 0x80000000U;
                        }
                        ptr2[dst] = ptr[src];
                        dst++;
                        src++;
                        cword_val >>= 1;
                    }
                }
            }
        }

        private unsafe static uint ReadUInt32(byte* p)
        {
            return *(uint*)p;
        }

        private const int HASH_VALUES = 0x1000;
        private const int MINOFFSET = 2;
        private const int UNCONDITIONAL_MATCHLEN = 6;
        private const int UNCOMPRESSED_END = 4;
        private const int CWORD_LEN = 4;
        private const int QLZ_POINTERS_3 = 0x10;
    }
}
