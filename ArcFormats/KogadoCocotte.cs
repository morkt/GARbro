//! \file       KogadoCocotte.cs
//! \date       Mon Aug 25 13:35:37 2014
//! \brief      Kogado engine Cocotte compression/encryption implementation.
//
// 作者について：
//   あんたいとるどどきゅめんと < http://juicy.s53.xrea.com/program/ >
// Written by juicy.gt < juicy[atm@rk]s53.xrea.com >
//
// Original code licensed under GNU GPL Version 2
//
// C# port by mørkt
// 2014/08/25
//

using System;
using System.IO;
using System.Text;

namespace GameRes.Formats.Kogado
{
    public class CocotteEncoder
    {
        BWTEncode   m_cBWTEncode = new BWTEncode();
        MTFEncode   m_cMTFEncode = new MTFEncode();
        CRangeCoder m_cRangeCoder = new CRangeCoder();

        const int RANGECODER_BLOCKSIZE = 0x2000;

        /*
        // Encode
        BOOL Encode( DWORD dwCompressionLevel, BYTE *pDestBuffer, const BYTE *pSrcBuffer, DWORD dwDestLength, DWORD dwSrcLength, DWORD *pdwWritten, HPA_Callback callback, LPVOID pCallbackArg )
        {
            // BWTEncode でサイズが 2 増えるので + 2 する
            BYTE buffer[ RANGECODER_BLOCKSIZE + 2 ];
            DWORD dwSrcCursor = 0, dwDestCursor = 0;
            DWORD written;
            DWORD write_src_size;

            m_cMTFEncode.InitMTFOrder();
            m_cRangeCoder.InitQSModel();
            while ( dwSrcCursor < dwSrcLength ) {
                if ( callback != NULL ) {
                    if ( !callback( pCallbackArg, dwSrcCursor, dwSrcLength ) )
                        return FALSE;
                }
                if ( dwDestLength - dwDestCursor <= 4 )	// バッファが足りない
                    return FALSE;
                write_src_size = min( RANGECODER_BLOCKSIZE, dwSrcLength - dwSrcCursor );
                m_cBWTEncode.Encode( buffer, pSrcBuffer, write_src_size );
                // BWTEncode でサイズが 2 増えるので + 2 する
                m_cMTFEncode.Encode( buffer, buffer, write_src_size + 2 );
                switch ( dwCompressionLevel ) {
                case CMPL_STORE:
                loc_store_encode:
                    written = write_src_size + 2;
                    memcpy( pDestBuffer + 4, buffer, written );
                    break;
                case CMPL_MAXIMUM:
                    if ( !m_cRangeCoder.Encode( pDestBuffer + 4, buffer, dwDestLength - dwDestCursor - 4, write_src_size + 2, &written ) )
                        return FALSE;
                    // オーバーしたら STORE にする
                    if ( written >= write_src_size + 2 ) {
                        m_cRangeCoder.InitQSModel();
                        goto loc_store_encode;
                    }
                    break;
                default:
                    return FALSE;
                }
                written += 4;
                // 書きすぎ
                if ( written > 0xffff || written > dwDestLength - dwDestCursor )
                    return FALSE;
                reinterpret_cast< unsigned short * >( pDestBuffer )[0] = static_cast< unsigned short >( written );
                reinterpret_cast< unsigned short * >( pDestBuffer )[1] = static_cast< unsigned short >( write_src_size );

                pSrcBuffer += write_src_size;	dwSrcCursor += write_src_size;
                pDestBuffer += written;	dwDestCursor += written;
            }
            *pdwWritten = dwDestCursor;
            if ( callback != NULL )
                if ( !callback( pCallbackArg, dwSrcCursor, dwSrcLength ) )
                    return FALSE;

            return ( dwSrcCursor == dwSrcLength );
        }
        */

        // Decode
        public bool Decode (Stream input, Stream output)
        {
            uint dwSrcLength = (uint)input.Length;
            var buffer = new byte [RANGECODER_BLOCKSIZE*4+2];
            uint dwSrcCursor = 0;
            var input_buffer = new byte[RANGECODER_BLOCKSIZE];

            m_cRangeCoder.InitQSModel();
            m_cMTFEncode.InitMTFOrder();

            using (var reader = new BinaryReader (input, Encoding.ASCII, true))
            {
                while (dwSrcCursor < dwSrcLength)
                {
                    if (dwSrcCursor + 4 >= dwSrcLength)
                        return false;
                    ushort src_block_size  = reader.ReadUInt16();
                    ushort dest_block_size = reader.ReadUInt16();
                    ushort comp_block_size = (ushort)(src_block_size - 4);
                    ushort decomp_block_size = (ushort)(dest_block_size + 2);

                    if (dwSrcCursor + src_block_size > dwSrcLength)
                        return false;
                    if (src_block_size <= 4 || dest_block_size == 0)
                        return false;
                    if (comp_block_size == decomp_block_size)
                    {
                        int read = input.Read (buffer, 0, comp_block_size);
                        m_cRangeCoder.InitQSModel();
                    }
                    else
                    {
                        if (comp_block_size > input_buffer.Length)
                            input_buffer = new byte[comp_block_size];
                        int read = input.Read (input_buffer, 0, comp_block_size);
                        if (read != comp_block_size)
                            return false;
                        uint written = m_cRangeCoder.Decode (buffer, input_buffer, decomp_block_size, comp_block_size);
                        if (0 == written)
                            break;
                        if (written != decomp_block_size)
                            return false;
                    }
                    m_cMTFEncode.Decode (buffer, buffer, decomp_block_size);
                    m_cBWTEncode.Decode (output, buffer, decomp_block_size);

                    dwSrcCursor += src_block_size;
                }
            }
            return dwSrcCursor == dwSrcLength;
        }
    }

    internal class CRangeCoder
    {
        byte[]  m_pSrcBuffer;
        byte[]  m_pDestBuffer;
        uint    m_dwSrcLength;
        uint    m_dwDestLength;
        uint    m_dwSrcIndex;
        uint    m_dwDestIndex;
        RangeCoder m_rc = new RangeCoder();
        QSModel m_qsm;

        const int CODE_BITS = 32;
        const int SHIFT_BITS = CODE_BITS - 9;
        const int EXTRA_BITS = (CODE_BITS - 2) % 8 + 1;
        const uint Top_value = 1u << (CODE_BITS - 1);
        const uint Bottom_value = Top_value >> 8;

        static readonly int[] RANGECODER_INITFREQ = {
            1400, 640, 320, 240, 160, 120,  80,  64,
            48,  40,  32,  24,  20,  20,  20,  20,
            16,  16,  16,  16,  12,  12,  12,  12,
            12,  12,   8,   8,   8,   8,   8,   8,
            6,   6,   6,   6,   6,   6,   6,   6,
            6,   6,   6,   6,   6,   6,   6,   6,
            5,   5,   5,   5,   5,   5,   5,   5,
            5,   5,   5,   5,   5,   5,   5,   5,

            4,   4,   4,   4,   4,   4,   4,   4,
            4,   4,   4,   4,   4,   4,   4,   4,
            4,   4,   4,   4,   4,   4,   4,   4,
            4,   4,   4,   4,   4,   4,   4,   4,
            3,   3,   3,   3,   3,   3,   3,   3,
            3,   3,   3,   3,   3,   3,   3,   3,
            3,   3,   3,   3,   3,   3,   3,   3,
            3,   3,   3,   3,   3,   3,   3,   3,

            3,   3,   3,   3,   3,   3,   2,   2,
            2,   2,   2,   2,   2,   2,   2,   2,
            2,   2,   2,   2,   2,   2,   2,   2,
            2,   2,   2,   2,   2,   2,   2,   2,
            2,   2,   2,   2,   2,   2,   2,   2,
            2,   2,   2,   2,   2,   2,   2,   2,
            2,   2,   2,   2,   2,   2,   2,   2,
            2,   2,   2,   2,   2,   2,   2,   2,

            2,   2,   2,   2,   2,   2,   2,   2,
            2,   2,   2,   2,   2,   2,   2,   2,
            2,   2,   2,   2,   2,   2,   2,   2,
            2,   2,   2,   2,   2,   2,   2,   2,
            2,   2,   2,   2,   2,   2,   2,   2,
            2,   2,   2,   2,   2,   2,   2,   2,
            2,   2,   2,   2,   2,   2,   2,   2,
            2,   2,   2,   2,   2,   2,   2,   2,

            2,
        };

        public void InitQSModel()
        {
            InitQSModel (257, 12, 2000, RANGECODER_INITFREQ, false);
        }

        public void InitQSModel (int n, int lg_totf, int rescale, int[] init, bool compress)
        {
            m_qsm = new QSModel (n, lg_totf, rescale, init, compress);
        }

        /*
        public bool Encode (byte[] dest, const BYTE *src, DWORD destsize, DWORD srcsize, DWORD *pwritten )
        {
            BYTE ch_byte;
            int syfreq, ltfreq;

            m_dwSrcIndex = m_dwDestIndex = 0;
            m_pSrcBuffer = src;
            m_pDestBuffer = dest;
            m_dwSrcLength = srcsize;
            m_dwDestLength = destsize;

            //this->InitQSModel();
            this->StartEncoding();

            while ( m_dwSrcIndex < m_dwSrcLength ) {
                if ( !this->GetSrcByteImpl( &ch_byte ) )
                    return false;
                qsgetfreq( &m_qsm, ch_byte, &syfreq, &ltfreq );
                this->EncodeShift( syfreq, ltfreq, 12 );
                qsupdate( &m_qsm, ch_byte );
            }
            qsgetfreq( &m_qsm, 256, &syfreq, &ltfreq );
            this->EncodeShift( syfreq, ltfreq, 12 );

            this->DoneEncoding();
            *pwritten = m_dwDestIndex;	// written-size

            return true;
        }
        */

        public uint Decode (byte[] dest, byte[] src, uint destsize, uint srcsize)
        {
            return Decode (dest, 0, destsize, src, 0, srcsize);
        }

        public uint Decode (byte[] dst, uint dst_index, uint dst_size,
                            byte[] src, uint src_index, uint src_size)
        {
            int ch, ltfreq, syfreq;

            m_dwSrcIndex = src_index;
            m_dwDestIndex = dst_index;
            m_pSrcBuffer = src;
            m_pDestBuffer = dst;
            m_dwSrcLength = src_size;
            m_dwDestLength = dst_size;

            StartDecoding();

            while (m_dwSrcIndex < m_dwSrcLength)
            {
                ltfreq = (int)DecodeCulshift (12);
                ch = m_qsm.GetSym (ltfreq);
                if (256 == ch)	// check for end-of-file
                    break;
                if (m_dwDestIndex >= m_dwDestLength)
                    return 0;
                SetDestByteImpl ((byte)ch);
                m_qsm.GetFreq (ch, out syfreq, out ltfreq);
                DecodeUpdate (syfreq, ltfreq, 1 << 12);
                m_qsm.Update (ch);
            }
            m_qsm.GetFreq (256, out syfreq, out ltfreq);
            DecodeUpdate (syfreq, ltfreq, 1 << 12);
            DoneDecoding();
            return m_dwDestIndex;
        }
/*
        // Encode --------------------------------------------------------
        void StartEncoding( char c = 0, int initlength = 0 )
        {
            m_rc.low = 0;	// Full code range
            m_rc.range = Top_value;
            m_rc.buffer = c;
            m_rc.help = 0;	// No bytes to follow
            m_rc.bytecount = initlength;
        }
        void EncNormalize()
        {
            while ( m_rc.range <= Bottom_value ) {	// do we need renormalisation?
                if ( m_rc.low < (uint)0xff << SHIFT_BITS ) {	// no carry possible --> output
                    this->SetDestByteImpl( m_rc.buffer );
                    for ( ; m_rc.help; m_rc.help -- )
                        this->SetDestByteImpl( 0xff );
                    m_rc.buffer = (unsigned char)( m_rc.low >> SHIFT_BITS );
                } else if ( m_rc.low & Top_value ) {	// carry now, no future carry
                    this->SetDestByteImpl( m_rc.buffer+1 );
                    for ( ; m_rc.help; m_rc.help -- )
                        this->SetDestByteImpl( 0 );
                    m_rc.buffer = (unsigned char)( m_rc.low >> SHIFT_BITS );
                } else	// passes on a potential carry
                    m_rc.help ++;
                m_rc.range <<= 8;
                m_rc.low = ( m_rc.low << 8 ) & ( Top_value - 1 );
                m_rc.bytecount ++;
            }
        }
        void EncodeFreq (uint sy_f, uint lt_f, uint tot_f)
        {
            uint r, tmp;

            this->EncNormalize();
            r = m_rc.range / tot_f;
            tmp = r * lt_f;
            m_rc.low += tmp;
            if ( lt_f + sy_f < tot_f )
                m_rc.range = r * sy_f;
            else
                m_rc.range -= tmp;
        }
        void EncodeShift (uint sy_f, uint lt_f, uint shift)
        {
            uint r, tmp;

            this->EncNormalize();
            r = m_rc.range >> shift;
            tmp = r * lt_f;
            m_rc.low += tmp;
            if ( ( lt_f + sy_f ) >> shift )
                m_rc.range -= tmp;
            else  
                m_rc.range = r * sy_f;
        }
        uint4 DoneEncoding()
        {
            uint tmp;

            this->EncNormalize();	// now we have a normalized state
            m_rc.bytecount += 5;
            if ( ( m_rc.low & ( Bottom_value - 1 ) ) < ( ( m_rc.bytecount & 0xffffffL ) >> 1 ) )
                tmp = m_rc.low >> SHIFT_BITS;
            else
                tmp = ( m_rc.low >> SHIFT_BITS ) + 1;
            if ( tmp > 0xff ) {	// we have a carry
                this->SetDestByteImpl( m_rc.buffer + 1 );
                for ( ; m_rc.help; m_rc.help -- )
                    this->SetDestByteImpl( 0 );
            } else {	// no carry
                this->SetDestByteImpl( m_rc.buffer );
                for ( ; m_rc.help; m_rc.help -- )
                    this->SetDestByteImpl( 0xff );
            }
            this->SetDestByteImpl( static_cast< BYTE >( tmp ) );
            this->SetDestByteImpl( static_cast< BYTE >( m_rc.bytecount >> 16 ) );
            this->SetDestByteImpl( static_cast< BYTE >( m_rc.bytecount >> 8 ) );
            this->SetDestByteImpl( static_cast< BYTE >( m_rc.bytecount ) );

            return m_rc.bytecount;
        }
*/

        // Decode --------------------------------------------------------
        int StartDecoding ()
        {
            byte c;

            if (!GetSrcByteImpl (out c))
                return -1;
            if (!GetSrcByteImpl (out m_rc.buffer))
                return -1;
            m_rc.low = (uint)(m_rc.buffer >> (8 - EXTRA_BITS));
            m_rc.range = (uint)1 << EXTRA_BITS;

            return c;
        }

        bool DecNormalize()
        {
            while ( m_rc.range <= Bottom_value )
            {
                m_rc.low = ( m_rc.low << 8 ) | (byte)(m_rc.buffer << EXTRA_BITS);
                if (!GetSrcByteImpl (out m_rc.buffer))
                    return false;
                m_rc.low |= (uint)m_rc.buffer >> ( 8 - EXTRA_BITS );
                m_rc.range <<= 8;
            }
            return true;
        }

        uint DecodeCulshift (int shift)
        {
            uint tmp;

            DecNormalize();
            m_rc.help = m_rc.range >> shift;
            tmp = m_rc.low / m_rc.help;
            return (0 != (tmp >> shift) ? (1u << shift) - 1u : tmp);
        }

        void DecodeUpdate (int sy_f, int lt_f, int tot_f)
        {
            uint tmp = m_rc.help * (uint)lt_f;

            m_rc.low -= tmp;
            if ( lt_f + sy_f < tot_f )
                m_rc.range = m_rc.help * (uint)sy_f;
            else
                m_rc.range -= tmp;
        }

        void DoneDecoding()
        {
            DecNormalize();	// normalize to use up all bytes
        }

        // I/O -----------------------------------------------------------
        bool GetSrcByteImpl (out byte pData)
        {
            if (m_dwSrcIndex >= m_dwSrcLength)
            {
                pData = 0;
                return false;
            }
            pData = m_pSrcBuffer[m_dwSrcIndex++];
            return true;
        }

        bool SetDestByteImpl (byte byData)
        {
            if (m_dwDestIndex >= m_dwDestLength)
                return false;
            m_pDestBuffer[m_dwDestIndex++] = byData;
            return true;
        }
    }

    internal class RangeCoder
    {
        public uint low;       /* low end of interval */
        public uint range;     /* length of interval */
        public uint help;      /* bytes_to_follow resp. intermediate value */
        public byte buffer;    /* buffer for input/output */
        /* the following is used only when encoding */
        public uint bytecount; /* counter for outputed bytes  */
    }

    // とりあえずこれで可逆性を概ね確認 (数タイトルの song.txt で確認)
    internal class BWTEncode
    {
        public const ulong BWT_SORTTABLESIZE = 0x00010000;

/*
        byte[] m_pWorkTable = new byte[BWT_SORTTABLESIZE / 2];

        void Encode( BYTE *dest, const BYTE *src, int size )
        {
            int top = 0;	// 初期値は不要だが、警告回避のため
            BYTE *ptr;
            int count[256] = { 0, };
            int count_sum[256+1];
            BYTE *sort_buffer = new BYTE[size*2];
            LPBYTE *sort_table = new LPBYTE[BWT_SORTTABLESIZE];

            // 作業領域にコピー
            memcpy( sort_buffer, src, size );
            memcpy( sort_buffer + size, sort_buffer, size );

            // 分布数え上げソート
            for ( int i = 0; i < size; i ++ )
                count[ sort_buffer[i] ]++;
            count_sum[0] = 0;
            for ( int i = 1; i <= 256; i ++ )
                count_sum[i] = count[i-1] + count_sum[i-1];
            for ( int i = 1; i < 256; i ++ )
                count[i] += count[i-1];

            for ( int i = size - 1; i >= 0; i -- ) {
                ptr = sort_buffer + i;
                sort_table[ -- count[*ptr] ] = ptr;
            }

            // 2 段階ソート
            for ( int i = 1; i < 256; i ++ ) {
                int j, k;
                int high = count_sum[i+1];

                for ( j = k = count_sum[i]; j < high; j ++ ) {
                    ptr = sort_table[j];
                    if ( *ptr > *(ptr + 1) ) {
                        sort_table[j] = sort_table[k];
                        sort_table[k ++] = ptr;
                    }
                }
                if ( high - k > 1 )
                    this->MergeSort( sort_table, k, high - 1, size );
            }
            // 0 は全てソート
            if ( count_sum[1] > 1 )
                this->MergeSort( sort_table, 0, count_sum[1] - 1, size );
            // ソート不要部分
            for ( int i = 0; i < size; i ++ ) {
                ptr = sort_table[i];
                if ( ptr == sort_buffer )
                    ptr += size;
                if ( *(ptr - 1) > *ptr )
                    sort_table[ count_sum[*(ptr - 1)] ++ ] = ptr - 1;
            }
            // 出力
            for ( int i = 0; i < size; i ++ ) {
                ptr = sort_table[i];
                if ( ptr == sort_buffer )
                    top = i;
                dest[i+2] = *(ptr + size - 1);
            }
            *reinterpret_cast< unsigned short * >( dest ) = static_cast< unsigned short >( top );
            // 解放
            delete[] sort_buffer;
            delete[] sort_table;
        }
*/
        int[] sort_table = new int[BWT_SORTTABLESIZE];

        public void Decode (Stream dest, byte[] src, int size)
        {
            int[] count = new int[256];
            int top = src[0] | src[1] << 8;

            int pos = 2;
            size -= 2;
            // 分布数え上げソート
            for (int i = 0; i < size; i++)
                count[ src[pos+i] ]++;
            for (short i = 1; i < 256; i++)
                count[i] += count[i-1];
            for (int i = size - 1; i >= 0; i --)
            {
                sort_table[--count[src[pos+i]]] = i;
            }
            // 出力
            int ptr = sort_table[top]; 
            for (int i = 0; i < size; i++)
            {
                dest.WriteByte (src[pos+ptr]);
                ptr = sort_table[ptr];
            }
        }
/*
        void MergeSort( BYTE *sort_table[], int low, int high, int size )
        {
            int len = size - 1;

            if ( high - low <= 10 ) {
                this->InsertSort( sort_table, low, high, size );
            } else {
                int middle = (low + high) / 2;
                int i, p, j, k;

                this->MergeSort( sort_table, low, middle, size );
                this->MergeSort( sort_table, middle + 1, high, size );
                p = 0;
                i = low;
                while ( i <= middle )
                    m_pWorkTable[p ++] = sort_table[i ++];
                i = middle + 1;
                j = 0;
                k = low;
                while ( i <= high && j < p ) {
                    if ( memcmp( m_pWorkTable[j] + 1, sort_table[i] + 1, len ) <= 0 )
                        sort_table[k ++] = m_pWorkTable[j ++];
                    else
                        sort_table[k ++] = sort_table[i ++];
                }
                while ( j < p )
                    sort_table[k ++] = m_pWorkTable[j ++];
            }
        }
        void InsertSort( BYTE *sort_table[], int low, int high, int size )
        {
            int j, len = size - 1;

            for ( int i = low + 1; i <= high ; i ++ ) {
                BYTE *tmp = sort_table[i];

                for ( j = i - 1; j >= low && memcmp( tmp + 1, sort_table[j] + 1, len ) < 0; j -- )
                    sort_table[j + 1] = sort_table[j];
                sort_table[j + 1] = tmp;
            }
        }
*/
    }

    internal class MTFEncode
    {
        byte[] m_MTFTable = new byte[256];

        public void InitMTFOrder()
        {
            for (int i = 0; i < 256; i++)
                m_MTFTable[i] = (byte)i;
        }

        // MTF は当然、destsize == srcsize
        public void Encode (byte[] dest, byte[] src, int size)
        {
            for (int i = 0; i < size; i++)
            {
                byte c = src[i];
                byte n = 0;

                while (m_MTFTable[n] != c)
                    n++;
                if (n > 0)
                {
                    Buffer.BlockCopy (m_MTFTable, 0, m_MTFTable, 1, n);
                    m_MTFTable[0] = c;
                }
                dest[i] = n;
            }
        }

        // MTF は当然、destsize == srcsize
        public void Decode (byte[] dest, byte[] src, int size)
        {
            for ( int i = 0; i < size; i++ )
            {
                byte n = src[i];
                byte c = m_MTFTable[n];
                if (n > 0)
                {
                    Buffer.BlockCopy (m_MTFTable, 0, m_MTFTable, 1, n);
                    m_MTFTable[0] = c;
                }
                dest[i] = c;
            }
        }
    }

    /*
    Quasistatic probability model

    // 若干改変 by juicy.gt at 2008/03/27 00:00

    (c) Michael Schindler
    1997, 1998, 2000
    http://www.compressconsult.com/
    michael@compressconsult.com

    This program is free software; you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation; either version 2 of the License, or
    (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.  It may be that this
    program violates local patents in your country, however it is
    belived (NO WARRANTY!) to be patent-free here in Austria.

    You should have received a copy of the GNU General Public License
    along with this program; if not, write to the Free Software
    Foundation, Inc., 59 Temple Place - Suite 330, Boston,
    MA 02111-1307, USA.

    Qsmodel is a quasistatic probability model that periodically
    (at chooseable intervals) updates probabilities of symbols;
    it also allows to initialize probabilities. Updating is done more
    frequent in the beginning, so it adapts very fast even without
    initialisation.

    it provides function for creation, deletion, query for probabilities
    and symbols and model updating.
    */

    internal class QSModel
    {
        public int m_n;             /* number of symbols */
        public int m_left;          /* symbols to next rescale */
        public int m_nextleft;      /* symbols with other increment */
        public int m_rescale;       /* intervals between rescales */
        public int m_targetrescale; /* should be interval between rescales */
        public int m_incr;          /* increment per update */
        public int m_searchshift;   /* shift for lt_freq before using as index */
        public ushort[] m_cf;       /* array of cumulative frequencies */
        public ushort[] m_newf;     /* array for collecting ststistics */
        public ushort[] m_search;   /* structure for searching on decompression */

        public const int TBLSHIFT = 7;

        /// <summary>
        /// initialisation of qsmodel
        /// </summary>
        /// <param name="n">number of symbols in that model</param>
        /// <param name="lg_totf">base2 log of total frequency count</param>
        /// <param name="rescale">desired rescaling interval, should be &lt; 1&lt;&lt;(lg_totf+1)</param>
        /// <param name="init">array of int's to be used for initialisation (NULL ok)</param>
        /// <param name="compress">true on compression, false on decompression</param>
        public QSModel (int n, int lg_totf, int rescale, int[] init, bool compress)
        {
            m_n = n;
            m_targetrescale = rescale;
            m_searchshift = lg_totf - TBLSHIFT;
            if (m_searchshift < 0)
                m_searchshift = 0;
            m_cf = new ushort[n+1];
            m_newf = new ushort[n+1];
            m_cf[n] = (ushort)(1 << lg_totf);
            m_cf[0] = 0;
            if (compress)
            {
                m_search = null;
            }
            else
            {
                m_search = new ushort[(1<<TBLSHIFT)+1];
                m_search[1<<TBLSHIFT] = (ushort)(n-1);
            }
            Reset (init);
        }

        /// <summary>
        /// reinitialisation of qsmodel
        /// </summary>
        /// <param name="init">array of int's to be used for initialisation (NULL ok)</param>
        public void Reset (int[] init)
        {
            int i;
            m_rescale = m_n>>4 | 2;
            m_nextleft = 0;
            if (init == null)
            {
                int initval = m_cf[m_n] / m_n;
                int end = m_cf[m_n] % m_n;
                for (i = 0; i < end; i++)
                    m_newf[i] = (ushort)(initval+1);
                for (; i < m_n; i++)
                    m_newf[i] = (ushort)initval;
            }
            else
            {
                for (i = 0; i < m_n; i++)
                    m_newf[i] = (ushort)init[i];
            }
            DoRescale();
        }

        void DoRescale ()
        {
            if (0 != m_nextleft)  /* we have some more before actual rescaling */
            {
                m_incr++;
                m_left = m_nextleft;
                m_nextleft = 0;
                return;
            }
            if (m_rescale < m_targetrescale)  /* double rescale interval if needed */
            {
                m_rescale <<= 1;
                if (m_rescale > m_targetrescale)
                    m_rescale = m_targetrescale;
            }
            int i, cf, missing;
            cf = missing = m_cf[m_n];  /* do actual rescaling */
            for (i = m_n-1; i != 0; i--)
            {
                int tmp = m_newf[i];
                cf -= tmp;
                m_cf[i] = (ushort)cf;
                tmp = tmp>>1 | 1;
                missing -= tmp;
                m_newf[i] = (ushort)tmp;
            }
            if (cf != m_newf[0])
                throw new ApplicationException ("Run-time error in QSModel.DoRescale");

            m_newf[0] = (ushort)(m_newf[0]>>1 | 1);
            missing -= m_newf[0];
            m_incr = missing / m_rescale;
            m_nextleft = missing % m_rescale;
            m_left = m_rescale - m_nextleft;
            if (m_search != null)
            {
                i = m_n;
                while (i != 0)
                {
                    int end = (m_cf[i]-1) >> m_searchshift;
                    i--;
                    int start = m_cf[i] >> m_searchshift;
                    while (start <= end)
                    {
                        m_search[start] = (ushort)i;
                        start++;
                    }
                }
            }
        }

        /// <summary>
        /// retrieval of estimated frequencies for a symbol
        /// </summary>
        /// <param name="sym">symbol for which data is desired; must be &lt;n</param>
        /// <param name="sy_f">frequency of that symbol</param>
        /// <param name="lt_f">frequency of all smaller symbols together</param>
        /// the total frequency is 1&lt;&lt;lg_totf
        public void GetFreq (int sym, out int sy_f, out int lt_f)
        {
            lt_f = m_cf[sym];
            sy_f = m_cf[sym+1] - lt_f;
        }	

        /// <summary>
        /// find out symbol for a given cumulative frequency.
        /// </summary>
        /// <param name="lt_f">cumulative frequency</param>
        public int GetSym (int lt_f)
        {
            int lo, hi;
            int tmp = lt_f >> m_searchshift;
            lo = m_search[tmp];
            hi = m_search[tmp+1] + 1;
            while (lo+1 < hi)
            {
                int mid = (lo + hi) >> 1;
                if (lt_f < m_cf[mid])
                    hi = mid;
                else
                    lo = mid;
            }
            return lo;
        }

        /// <summary>
        /// update model
        /// </summary>
        /// <param name="sym">symbol that occurred (must be &lt;n from init)</param>
        public void Update (int sym)
        {
            if (m_left <= 0)
                DoRescale();
            m_left--;
            m_newf[sym] += (ushort)m_incr;
        }
    }
}
