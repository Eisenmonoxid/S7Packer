using System;
using System.Runtime.InteropServices;

namespace S7Packer.Source
{
	public class Crypt
    {
		private static uint FEISTEL(uint y, uint z, uint Sum, uint Key) => 
			((y >> 5 ^ z << 2) + (y << 4 ^ z >> 3)) ^ ((Sum ^ z) + (Key ^ y));

        public void DecryptCompressedFile(Span<byte> Data, uint FileNameLength, uint[] XOR)
		{
			Span<uint> Input = MemoryMarshal.Cast<byte, uint>(Data);
			Input[3] ^= Input[2] ^ FileNameLength ^ XOR[3];
			Input[2] ^= Input[1] ^ FileNameLength ^ XOR[2];
			Input[1] ^= Input[0] ^ FileNameLength ^ XOR[1];
			Input[0] ^= Input[3] ^ FileNameLength ^ XOR[0];
        }

		public void EncryptCompressedFile(Span<byte> Data, uint FileNameLength, uint[] XOR)
		{
			Span<uint> Input = MemoryMarshal.Cast<byte, uint>(Data);
			Input[0] ^= Input[3] ^ FileNameLength ^ XOR[0];
			Input[1] ^= Input[0] ^ FileNameLength ^ XOR[1];
			Input[2] ^= Input[1] ^ FileNameLength ^ XOR[2];
			Input[3] ^= Input[2] ^ FileNameLength ^ XOR[3];
		}

		public void Decrypt(Span<byte> Data, ReadOnlySpan<uint> Keys, uint Delta, int LoopCount)
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
				Values[0] -= ((y0 >> 5 ^ z << 2) + (y0 << 4 ^ z >> 3)) ^ ((Sum ^ z) + (Keys[e] ^ y0));
				Sum -= Delta;
			}
		}

		public void Encrypt(Span<byte> Data, ReadOnlySpan<uint> Keys, uint Delta, int LoopCount)
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
				Values[Size - 1] += ((y >> 5 ^ z0 << 2) + (y << 4 ^ z0 >> 3)) ^ ((Sum ^ z0) + (Keys[((Size - 1) & 3) ^ e] ^ y));
			}
		}

		public void HandleTEAFile(byte[] Data, uint FileNameLength, int BlockSize, uint Delta, int LoopCount, bool Encryption)
		{
			ReadOnlySpan<uint> Keys =
            [
                892421091u  ^ FileNameLength,
                3702197895u ^ FileNameLength,
				3064602941u ^ FileNameLength,
				189782668u  ^ FileNameLength
			];

			for (int Offset = 0; Offset < Data.Length; Offset += BlockSize)
			{
				int Remaining = Data.Length - Offset;
				int CopyLength = Math.Min(BlockSize, Remaining);
				int Padding = CopyLength == BlockSize ? BlockSize : (Remaining + 7) & ~7;
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
