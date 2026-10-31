using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace S7Packer.Source
{
	public static class Crypt
    {
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static uint FEISTEL(uint y, uint z, uint Sum, uint Key)
		{
			uint lhs = (y >> 5) ^ (z << 2);
			uint rhs = (y << 4) ^ (z >> 3);
			return (lhs + rhs) ^ ((Sum ^ z) + (Key ^ y));
		}

		[MethodImpl(MethodImplOptions.AggressiveOptimization)]
		public static void DecryptCompressedFile(Span<byte> Data, uint FileNameLength, uint[] XOR)
		{
			Span<uint> Input = MemoryMarshal.Cast<byte, uint>(Data);
			Input[3] ^= Input[2] ^ FileNameLength ^ XOR[3];
			Input[2] ^= Input[1] ^ FileNameLength ^ XOR[2];
			Input[1] ^= Input[0] ^ FileNameLength ^ XOR[1];
			Input[0] ^= Input[3] ^ FileNameLength ^ XOR[0];
		}

		[MethodImpl(MethodImplOptions.AggressiveOptimization)]
		public static void EncryptCompressedFile(Span<byte> Data, uint FileNameLength, uint[] XOR)
		{
			Span<uint> Input = MemoryMarshal.Cast<byte, uint>(Data);
			Input[0] ^= Input[3] ^ FileNameLength ^ XOR[0];
			Input[1] ^= Input[0] ^ FileNameLength ^ XOR[1];
			Input[2] ^= Input[1] ^ FileNameLength ^ XOR[2];
			Input[3] ^= Input[2] ^ FileNameLength ^ XOR[3];
		}

		[MethodImpl(MethodImplOptions.AggressiveOptimization)]
		public static void Decrypt(Span<byte> Data, ReadOnlySpan<uint> Keys, uint Delta, int LoopCount)
		{
			Span<uint> Values = MemoryMarshal.Cast<byte, uint>(Data);
			int Size = Values.Length;
			uint Sum = (uint)LoopCount * Delta;

			while (Sum != 0)
			{
				int e = (int)(Sum >> 2) & 3;
				uint z = Values[0];

				for (int p = Size - 1; p > 0; p--)
				{
					uint y = Values[p - 1];
					Values[p] -= FEISTEL(y, z, Sum, Keys[(p & 3) ^ e]);
					z = Values[p];
				}

				uint y0 = Values[Size - 1];
				Values[0] -= FEISTEL(y0, z, Sum, Keys[e]);
				Sum -= Delta;
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveOptimization)]
		public static void Encrypt(Span<byte> Data, ReadOnlySpan<uint> Keys, uint Delta, int LoopCount)
		{
			Span<uint> Values = MemoryMarshal.Cast<byte, uint>(Data);
			int Size = Values.Length;

			uint Sum = 0;
			uint End = (uint)LoopCount * Delta;

			while (Sum != End)
			{
				Sum += Delta;
				int e = (int)((Sum >> 2) & 3);
				uint y = Values[Size - 1];

				for (int p = 0; p < Size - 1; p++)
				{
					uint z = Values[p + 1];
					Values[p] += FEISTEL(y, z, Sum, Keys[(p & 3) ^ e]);
					y = Values[p];
				}

				uint z0 = Values[0];
				Values[Size - 1] += FEISTEL(y, z0, Sum, Keys[((Size - 1) & 3) ^ e]);
			}
		}

		public static void HandleTEAFile(byte[] Data, uint FileNameLength, uint BlockSize, uint Delta, int LoopCount, bool Encryption)
		{
			ReadOnlySpan<uint> Keys =
            [
                892421091u  ^ FileNameLength,
                3702197895u ^ FileNameLength,
				3064602941u ^ FileNameLength,
				189782668u  ^ FileNameLength
			];

			for (uint Offset = 0; Offset < Data.Length; Offset += BlockSize)
			{
				long Remaining = Data.Length - Offset;
				long CopyLength = Math.Min(BlockSize, Remaining);
				bool FullBlock = CopyLength == BlockSize;
				long Padding = FullBlock ? BlockSize : ((Remaining + 3) & ~3);

				if (FullBlock)
				{
					Span<byte> Block = Data.AsSpan((int)Offset, (int)CopyLength);
					if (Encryption)
					{
						Encrypt(Block, Keys, Delta, LoopCount);
					}
					else
					{
						Decrypt(Block, Keys, Delta, LoopCount);
					}
				}
				else
				{
					byte[] Buffer = new byte[Padding];
					Array.Copy(Data, Offset, Buffer, 0, CopyLength);
					if (Encryption)
					{
						Encrypt(Buffer.AsSpan(), Keys, Delta, LoopCount);
					}
					else
					{
						Decrypt(Buffer.AsSpan(), Keys, Delta, LoopCount);
					}
					Array.Copy(Buffer, 0, Data, Offset, CopyLength);
				}
			}
		}
    }
}
