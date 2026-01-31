using System;
using System.Linq;
using System.Runtime.InteropServices;

namespace S7Packer.Source
{
	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	public struct BBAHeaderDefinition
	{
		[MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
		public byte[] ArchiveHeader;
		public byte Version;
		public uint Reserved;
		public uint HeaderSize;
		public uint HeaderEncryptionIdentifier;
	}

	internal class BBAHeader
	{
		private BBAHeaderDefinition Definition;
		public ref BBAHeaderDefinition GetDefinition() => ref Definition;

		public BBAHeader(byte[] Data) // Data.Length = 16
		{
			if (Data == default)
			{
				Definition = new BBAHeaderDefinition
				{
					ArchiveHeader = [.. "BAF".Select(C => (byte)C)],
					Version = 6,
					Reserved = 7,
					HeaderSize = 64,
					HeaderEncryptionIdentifier = 1830454153
				};
			}
			else
			{
				Definition = new BBAHeaderDefinition
				{
					ArchiveHeader = [.. Data.Take(3)],
					Version = Data[3],
					Reserved = 7,
					HeaderSize = BitConverter.ToUInt32(Data, 8),
					HeaderEncryptionIdentifier = BitConverter.ToUInt32(Data, 12)
				};
			}
		}

		public byte[] Serialize() => Utility.Serialize(Definition);
	}
}
