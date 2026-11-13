using System;
using System.Runtime.InteropServices;

namespace S7Packer.Source
{
	[StructLayout(LayoutKind.Sequential, Pack = 4)]
	public unsafe struct BBADirectoryDefinition
	{
		public uint HeaderSize;
		public uint OffsetFileEntries;
		public uint OffsetFileHashtable;
		public fixed byte Reserved[52];
		public fixed byte Magic[3];
		public byte Version;
		public uint Value;
		public uint HeaderSize2;
		public uint HeaderEncryptionID;
		public fixed byte Reserved1[16];
		public long DirectoryEncryptionID;
		public fixed byte Reserved2[44];
		public long FileEncryptionID;
		public fixed byte Reserved3[116];
		public uint NumberOfFiles;
	}

	internal class BBADirectory
	{
		private BBADirectoryDefinition Definition;
		public ref BBADirectoryDefinition GetDefinition() => ref Definition;
		public byte[] Serialize() => Utility.Serialize(Definition);
		public static uint Size => (uint)Marshal.SizeOf<BBADirectoryDefinition>(); // 276
		public static readonly uint[] Keys = [892421091, 3702197895, 3064602941, 189782668];

		private static long CombineEncryptionIdentifier(uint A, uint B)
		{
			ulong Combined = ((ulong)A << 32) | B;
			return unchecked((long)Combined);
		}

		public BBADirectory(ReadOnlySpan<byte> Data)
		{
			Definition = Utility.Parse<BBADirectoryDefinition>(Data);
		}

		public BBADirectory(UInt32 OffsetFileHashtable, UInt32 NumberOfFiles)
		{
			Definition = new BBADirectoryDefinition
			{
				HeaderSize = 64,
				OffsetFileEntries = Size - 4, // 272
				OffsetFileHashtable = OffsetFileHashtable,
				Version = 6,
				Value = 7,
				HeaderSize2 = 64,
				HeaderEncryptionID = Crypt.S7_HEAD_CRYPTID,
				DirectoryEncryptionID = Crypt.S7_DIRECTORY_CRYPTID,
				FileEncryptionID = Crypt.S7_FILE_CRYPTID,
				NumberOfFiles = NumberOfFiles
			};

			unsafe
			{
				fixed (byte* Pointer = Definition.Magic)
				{
        			"BAF"u8.CopyTo(new Span<byte>(Pointer, 3));
				}
			}
		}
	}
}
