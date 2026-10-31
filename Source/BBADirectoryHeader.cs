using System;
using System.Runtime.InteropServices;

namespace S7Packer.Source
{
	[StructLayout(LayoutKind.Sequential, Pack = 4)]
	public struct BBADirectoryHeaderDefinition
	{
		public uint OffsetLow;
		public uint OffsetHigh;
		public uint Length;
		public uint CRC32;
		public long EncryptionIdentifier;
		[MarshalAs(UnmanagedType.ByValArray, SizeConst = 40)]
		public byte[] Reserved;
	}

	internal class BBADirectoryHeader
	{
		private BBADirectoryHeaderDefinition Definition;
		public ref BBADirectoryHeaderDefinition GetDefinition() => ref Definition;

		private static long GetEncryptionIdentifier(uint Lower, uint Upper)
		{
			ulong Combined = ((ulong)Lower << 32) | Upper;
			return unchecked((long)Combined);
		}

		public byte[] Serialize() => Utility.Serialize(Definition);

		public BBADirectoryHeader(ReadOnlySpan<uint> Data) // Data Length = 16
		{
			Definition = new BBADirectoryHeaderDefinition
			{
				OffsetLow = Data[0],
				OffsetHigh = Data[1],
				Length = Data[2] == 0 ? 16u : Data[2],
				CRC32 = Data[3],
				EncryptionIdentifier = (Data[4] == 0 || Data[5] == 0) ? -2151081698912143722L : GetEncryptionIdentifier(Data[4], Data[5]),
				Reserved = new byte[40],
			};
		}
	}
}
