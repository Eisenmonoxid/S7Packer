using System;
using System.Linq;
using System.Runtime.InteropServices;

namespace S7Packer.Source
{
	[StructLayout(LayoutKind.Sequential, Pack = 4)]
	public struct BBADirectoryDefinition
	{
		public uint HeaderSize;
		public uint OffsetFileEntries;
		public uint OffsetFileHashtable;
		[MarshalAs(UnmanagedType.ByValArray, SizeConst = 52)]
		public byte[] Reserved;
		[MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
		public byte[] ArchiveHeader;
		public byte Version;
		public uint Value;
		public uint HeaderSize2;
		public uint HeaderEncryptionIdentifier;
		[MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
		public byte[] Reserved2;
		public long DirectoryEncryptionIdentifier;
		[MarshalAs(UnmanagedType.ByValArray, SizeConst = 44)]
		public byte[] Reserved3;
		public long FileEncryptionIdentifier;
		[MarshalAs(UnmanagedType.ByValArray, SizeConst = 116)]
		public byte[] Reserved4;
		public uint NumberOfFiles;
	}

	internal class BBADirectory
	{
		private BBADirectoryDefinition Definition;
		public ref BBADirectoryDefinition GetDefinition() => ref Definition;

		private static long CombineEncryptionIdentifier(uint A, uint B)
		{
			ulong Combined = ((ulong)A << 32) | B;
			return unchecked((long)Combined);
		}

		public BBADirectory(uint[] Data) // Data.Length = 69
		{
			if (Data == default)
			{
				Definition = new BBADirectoryDefinition
				{
					HeaderSize = 64,
					OffsetFileEntries = 272,
					OffsetFileHashtable = uint.MaxValue,
					Reserved = new byte[52],
					ArchiveHeader = [(byte)'B', (byte)'A', (byte)'F'],
					Version = 6,
					Value = 7,
					HeaderSize2 = 64,
					HeaderEncryptionIdentifier = 1830454153,
					Reserved2 = new byte[16],
					DirectoryEncryptionIdentifier = -2151081698912143722,
					Reserved3 = new byte[44],
					FileEncryptionIdentifier = 9043076966445316930,
					Reserved4 = new byte[116],
					NumberOfFiles = 0
				};
			}
			else
			{
				Definition = new BBADirectoryDefinition
				{
					HeaderSize = Data[18],
					OffsetFileEntries = Data[1],
					OffsetFileHashtable = Data[2],
					Reserved = new byte[52],
					ArchiveHeader = [(byte)Data[16], (byte)Data[17], (byte)Data[18]],
					Version = (byte)(Data[16] >> 24),
					Value = 7,
					HeaderSize2 = Data[18],
					HeaderEncryptionIdentifier = Data[19],
					Reserved2 = new byte[16],
					DirectoryEncryptionIdentifier = CombineEncryptionIdentifier(Data[24], Data[25]),
					Reserved3 = new byte[44],
					FileEncryptionIdentifier = CombineEncryptionIdentifier(Data[37], Data[38]),
					Reserved4 = new byte[116],
					NumberOfFiles = Data[68]
				};
			}
		}

		public byte[] Serialize() => Utility.Serialize(Definition);
	}
}
