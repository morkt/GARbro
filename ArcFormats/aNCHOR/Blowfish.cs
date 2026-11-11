// vi: shiftwidth=8 noexpandtab
/****************************************************************************
 |
 | Copyright (c) 2007 Novell, Inc.
 | All Rights Reserved.
 |
 | This program is free software; you can redistribute it and/or
 | modify it under the terms of version 2 of the GNU General Public License as
 | published by the Free Software Foundation.
 |
 | This program is distributed in the hope that it will be useful,
 | but WITHOUT ANY WARRANTY; without even the implied warranty of
 | MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 | GNU General Public License for more details.
 |
 | You should have received a copy of the GNU General Public License
 | along with this program; if not, contact Novell, Inc.
 |
 | To contact Novell about this file by physical or electronic mail,
 | you may find current contact information at www.novell.com 
 |
 |  Author: Russ Young
 |	Thanks to: Bruce Schneier / Counterpane Labs 
 |	for the Blowfish encryption algorithm and
 |	reference implementation. http://www.schneier.com/blowfish.html
 |***************************************************************************/

//! \file       Blowfish.cs
//! \date       2026-02-02
//! \brief      Modified Blowfish encryption algorithm implementation.
//

using System;
using System.IO;
using System.Security.Cryptography;
using GameRes.Utility;

namespace GameRes.Formats.Anchor
{
	/// <summary>
	/// Class that provides blowfish encryption.
	/// </summary>
	public class Mk2Blowfish
	{
		const int	N = 16;

		uint[]		ctx;

		/// <summary>
		/// Constructs and initializes a blowfish instance with the supplied key.
		/// </summary>
		/// <param name="key">The key to cipher with.</param>
		public Mk2Blowfish(byte[] key, byte[] _ctx)
		{
			short			i;
			short			j;
			short			k;
			uint			data;
			uint			datal;
			uint			datar;

			ctx = new uint[N + 270];

			for (i = 0; i < N + 2; ++i)
			{
				ctx[i] = BigEndian.ToUInt32 (_ctx, 4 * i);
			}

			for (i = 0; i < 4; ++i) 
			{
				for (j = 0; j < 256; ++j) 
				{
					ctx[N + 2 + i * 4 + j] = BigEndian.ToUInt32 (_ctx, 4 * (N + 2 + i * 256 + j));
				}
			}

			j = 0;
			for (i = 0; i < N + 2; ++i) 
			{
				data = 0x00000000;
				for (k = 0; k < 4; ++k) 
				{
					data = (data << 8) | key[j];
					j++;
					if (j >= key.Length) 
					{
						j = 0;
					}
				}
				ctx[i] = ctx[i] ^ data;
			}

			datal = 0x00000000;
			datar = 0x00000000;

			for (i = 0; i < N + 2; i += 2) 
			{
				Encipher(ref datal, ref datar);
				ctx[i] = datal;
				ctx[i + 1] = datar;
			}

			for (i = 0; i < 4; ++i) 
			{
				for (j = 0; j < 256; j += 2) 
				{
					Encipher(ref datal, ref datar);

					ctx[N + 2 + i * 4 + j] = datal;
					ctx[N + 3 + i * 4 + j] = datar;
				}
			}
		}

		public ICryptoTransform CreateDecryptor ()
		{
			return new Mk2BlowfishDecryptor (this);
		}
		
		private uint F(uint x)
		{
			ushort a;
			ushort b;
			ushort c;
			ushort d;
			uint  y;

			d = (ushort)(x & 0x00FF);
			x >>= 8;
			c = (ushort)(x & 0x00FF);
			x >>= 8;
			b = (ushort)(x & 0x00FF);
			x >>= 8;
			a = (ushort)(x & 0x00FF);

			y = ctx[18 + a] + ctx[22 + b];
			y = y ^ ctx[26 + c];
			y = y ^ ctx[30 + d];

			return y;
		}
			
		/// <summary>
		/// Encrypts 8 bytes of data (1 block)
		/// </summary>
		/// <param name="xl">The left part of the 8 bytes.</param>
		/// <param name="xr">The right part of the 8 bytes.</param>
		private void Encipher(ref uint xl, ref uint xr)
		{
			uint	Xl;
			uint	Xr;
			uint	temp;
			short	i;

			Xl = xl;
			Xr = xr;

			for (i = 0; i < N; ++i) 
			{
				Xl = Xl ^ ctx[i];
				Xr = F(Xl) ^ Xr;

				temp = Xl;
				Xl = Xr;
				Xr = temp;
			}

			temp = Xl;
			Xl = Xr;
			Xr = temp;

			Xr = Xr ^ ctx[N];
			Xl = Xl ^ ctx[N + 1];

			xl = Xl;
			xr = Xr;
		}

		/// <summary>
		/// Decrypts 8 bytes of data (1 block)
		/// </summary>
		/// <param name="xl">The left part of the 8 bytes.</param>
		/// <param name="xr">The right part of the 8 bytes.</param>
		public void Decipher(ref uint xl, ref uint xr)
		{
			uint	Xl;
			uint	Xr;
			uint	temp;
			short   i;

			Xl = xl;
			Xr = xr;

			for (i = N + 1; i > 1; --i) 
			{
				Xl = Xl ^ ctx[i];
				Xr = F(Xl) ^ Xr;

				/* Exchange Xl and Xr */
				temp = Xl;
				Xl = Xr;
				Xr = temp;
			}

			/* Exchange Xl and Xr */
			temp = Xl;
			Xl = Xr;
			Xr = temp;

			Xr = Xr ^ ctx[1];
			Xl = Xl ^ ctx[0];

			xl = Xl;
			xr = Xr;
		}
	}

	/// <summary>
	/// ICryptoTransform implementation for use with CryptoStream.
	/// </summary>
	public sealed class Mk2BlowfishDecryptor : ICryptoTransform
	{
		Mk2Blowfish    m_bf;

		public const int BlockSize = 8;

		public bool CanTransformMultipleBlocks { get { return true; } }
		public bool          CanReuseTransform { get { return true; } }
		public int              InputBlockSize { get { return BlockSize; } }
		public int             OutputBlockSize { get { return BlockSize; } }

		public Mk2BlowfishDecryptor (Mk2Blowfish bf)
		{
			m_bf = bf;
		}

		public int TransformBlock (byte[] inBuffer, int offset, int count, byte[] outBuffer, int outOffset)
		{
			for (int i = 0; i < count; i += BlockSize)
			{
				uint xl = BigEndian.ToUInt32 (inBuffer, offset+i);
				uint xr = BigEndian.ToUInt32 (inBuffer, offset+i+4);
				m_bf.Decipher (ref xl, ref xr);
				BigEndian.Pack (xl, outBuffer, outOffset+i);
				BigEndian.Pack (xr, outBuffer, outOffset+i+4);
			}
			return count;
		}

		static readonly byte[] EmptyArray = new byte[0];

		public byte[] TransformFinalBlock (byte[] inBuffer, int offset, int count)
		{
			if (0 == count)
				return EmptyArray;

			var input = new byte[(count + BlockSize - 1) / BlockSize * BlockSize];
			Buffer.BlockCopy (inBuffer, 0, input, 0, inBuffer.Length);
			
			var output = new byte[input.Length];
			TransformBlock (input, offset, count, output, 0);

			Array.Resize (ref output, count);
			return output;
		}

		#region IDisposable implementation
		bool _disposed = false;
		public void Dispose ()
		{
			if (!_disposed)
			{
				_disposed = true;
			}
			GC.SuppressFinalize (this);
		}
		#endregion
	}
}
