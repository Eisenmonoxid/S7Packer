using System;
using System.Runtime.InteropServices;

namespace S7Packer.Source
{
	[StructLayout(LayoutKind.Sequential, Pack = 4)]
	public unsafe struct BBADirectoryHeaderDefinition
	{
		public uint OffsetLow;
		public uint OffsetHigh;
		public uint Length;
		public uint CRC32;
		public long EncryptionID;
		public fixed byte Reserved[40];
	}

	internal class BBADirectoryHeader
	{
		private BBADirectoryHeaderDefinition Definition;
		public ref BBADirectoryHeaderDefinition GetDefinition() => ref Definition;
		public byte[] Serialize() => Utility.Serialize(Definition);
		public static uint Size => (uint)Marshal.SizeOf<BBADirectoryHeaderDefinition>(); // 64
		public static readonly uint[] Keys = [623092400, 3578011951, 428883204, 211416760];

		private static long GetEncryptionIdentifier(uint Lower, uint Upper)
		{
			ulong Combined = ((ulong)Lower << 32) | Upper;
			return unchecked((long)Combined);
		}

		public BBADirectoryHeader(ReadOnlySpan<byte> Data)
		{
			Definition = Utility.Parse<BBADirectoryHeaderDefinition>(Data);
		}

		public BBADirectoryHeader(BBAHeader Header, uint FileLength, uint HeaderLength, uint CRC32)
		{
			Definition = new BBADirectoryHeaderDefinition
			{
				OffsetLow = BBAHeader.Size + Header.GetDefinition().Size + FileLength,
				OffsetHigh = 0,
				Length = HeaderLength,
				CRC32 = CRC32,
				EncryptionID = Crypt.S7_DIRECTORY_CRYPTID,
			};
		}

		public byte[] Encrypt(uint Delta, int LoopCount)
		{
			byte[] Data = Serialize();
			Crypt.Encrypt(Data, Keys, Delta, LoopCount);
			return Data;
		}
	}
}
