//! \file       LeafCompression.cs
//! \date       2026 Jul 25
//! \brief      Leaf LZSS compression implementation (by gopicolo).
//
// Copyright (C) 2018-2019 by morkt
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

using System.IO;

namespace GameRes.Formats.Leaf
{
    // --- LEAF LZSS COMPRESSION ---
    internal static class LeafLzss
    {
        const int N = 4096;
        const int F = 18;
        const int THR = 2;
        const int NIL = N;

        public static byte[] Compress (byte[] input, byte frameFill)
        {
            if (input.Length == 0) return new byte[0];

            using (var outStream = new MemoryStream (input.Length))
            {
                // Arrays size = N + 257 to handle Root Nodes (N+1..N+256) safely
                int[] lson = new int[N + 257];
                int[] rson = new int[N + 257];
                int[] dad  = new int[N + 257];
                byte[] text_buf = new byte[N + F - 1];

                for (int j = N + 1; j <= N + 256; j++) rson[j] = NIL;
                for (int j = 0; j < N; j++) dad[j] = NIL;

                int match_position = 0, match_length = 0;

                void InsertNode (int r_node)
                {
                    int i, p, cmp;
                    int key_pos = r_node;
                    
                    // Root for this character
                    p = N + 1 + text_buf[key_pos];
                    
                    rson[r_node] = lson[r_node] = NIL;
                    match_length = 0;
                    
                    cmp = 1; // Initial state

                    for (; ; )
                    {
                        if (cmp >= 0)
                        {
                            if (rson[p] != NIL) p = rson[p];
                            else { rson[p] = r_node; dad[r_node] = p; return; }
                        }
                        else
                        {
                            if (lson[p] != NIL) p = lson[p];
                            else { lson[p] = r_node; dad[r_node] = p; return; }
                        }
                        
                        // Compare bytes only after finding a valid child node 'p' (buffer index)
                        for (i = 1; i < F; i++)
                            if ((cmp = text_buf[key_pos + i] - text_buf[p + i]) != 0) break;
                        
                        if (i > match_length)
                        {
                            match_position = p;
                            match_length = i;
                            if (match_length >= F) break;
                        }
                    }
                    
                    // Replace node logic
                    dad[r_node] = dad[p]; lson[r_node] = lson[p]; rson[r_node] = rson[p];
                    dad[lson[p]] = r_node; dad[rson[p]] = r_node;
                    if (rson[dad[p]] == p) rson[dad[p]] = r_node;
                    else lson[dad[p]] = r_node;
                    dad[p] = NIL;
                }

                void DeleteNode (int p)
                {
                    int q;
                    if (dad[p] == NIL) return;
                    if (rson[p] == NIL) q = lson[p];
                    else if (lson[p] == NIL) q = rson[p];
                    else
                    {
                        q = lson[p];
                        if (rson[q] != NIL)
                        {
                            do { q = rson[q]; } while (rson[q] != NIL);
                            rson[dad[q]] = lson[q]; dad[lson[q]] = dad[q];
                            lson[q] = lson[p]; dad[lson[p]] = q;
                        }
                        rson[q] = rson[p]; dad[rson[p]] = q;
                    }
                    dad[q] = dad[p];
                    if (rson[dad[p]] == p) rson[dad[p]] = q;
                    else lson[dad[p]] = q;
                    dad[p] = NIL;
                }

                int code_buf_ptr = 1;
                byte[] code_buf = new byte[17];
                byte mask = 1;
                int s = 0, r = N - F;
                int len = 0;

                for (int j = 0; j < r; j++) text_buf[j] = frameFill;

                int bytes_read = 0;
                for (len = 0; len < F && bytes_read < input.Length; len++)
                    text_buf[r + len] = input[bytes_read++];

                if (len == 0) return new byte[0];

                for (int j = 1; j <= F; j++) InsertNode (r - j);
                InsertNode (r);

                do
                {
                    if (match_length > len) match_length = len;
                    if (match_length <= THR)
                    {
                        match_length = 1;
                        code_buf[0] |= mask;
                        code_buf[code_buf_ptr++] = text_buf[r];
                    }
                    else
                    {
                        code_buf[code_buf_ptr++] = (byte)(match_position & 0xFF);
                        code_buf[code_buf_ptr++] = (byte)(((match_position >> 4) & 0xF0) | (match_length - (THR + 1)));
                    }

                    if ((mask <<= 1) == 0)
                    {
                        outStream.Write (code_buf, 0, code_buf_ptr);
                        code_buf[0] = 0; code_buf_ptr = 1; mask = 1;
                    }

                    int last_match_length = match_length;
                    
                    int i;
                    for (i = 0; i < last_match_length && bytes_read < input.Length; i++)
                    {
                        DeleteNode (s);
                        byte c = input[bytes_read++];
                        text_buf[s] = c;
                        if (s < F - 1) text_buf[s + N] = c;
                        s = (s + 1) & (N - 1);
                        r = (r + 1) & (N - 1);
                        InsertNode (r);
                    }
                    
                    while (bytes_read == input.Length && i++ < last_match_length)
                    {
                        DeleteNode (s);
                        s = (s + 1) & (N - 1);
                        r = (r + 1) & (N - 1);
                        if (--len != 0) InsertNode (r);
                    }

                } while (len > 0);

                if (code_buf_ptr > 1)
                    outStream.Write (code_buf, 0, code_buf_ptr);

                return outStream.ToArray();
            }
        }
    }
}
