using System;
using System.Runtime.InteropServices;
using System.Text;

namespace S7Packer.Source
{
	public enum BBAArchiveDataEntryType : uint
	{
		TEA = 2,
		Compressed = 17,
		Directory = 256,
		None = 0
	}

	[StructLayout(LayoutKind.Sequential, Pack = 4)]
	public unsafe struct BBADataEntryDefinition
	{
		public UInt64 TimeStamp;
		public UInt32 DecompressedFileSize;
		public UInt32 DecompressedCRC32;
		public UInt32 FileType;
		public fixed byte Padding[4];
		public UInt32 FileOffset;
		public fixed byte Padding1[4];
		public UInt32 CompressedFileSize;
		public UInt32 CompressedCRC32;
		public UInt32 BlockSize;
		public fixed byte Padding2[4];
		public UInt32 FileNameLength;
		public UInt32 FileNameOffset;
		public Int32 FirstChild;
		public Int32 NextSibling;
		// string FileName follows (dynamic length)
	}

	internal class BBADataEntry
	{
		private BBADataEntryDefinition Definition;
		public ref BBADataEntryDefinition GetDefinition() => ref Definition;
		public string FileName;
		public static uint Size => (uint)Marshal.SizeOf<BBADataEntryDefinition>();
		public static UInt64 GetTimeStamp(DateTime Date) => BitConverter.ToUInt64(BitConverter.GetBytes(Date.Ticks), 0);

		public BBADataEntry(ReadOnlySpan<byte> Data)
		{
			Definition = Utility.Parse<BBADataEntryDefinition>(Data);
			FileName = Encoding.ASCII.GetString(Data[(int)Size .. (int)(Size + Definition.FileNameLength)]).TrimEnd('\0');
		}

		public BBADataEntry()
		{
			Definition = new();
		}

		public byte[] Serialize()
		{
			Definition.FileNameLength = (uint)FileName.Length;
			return Utility.Serialize(Definition, GetFullSizeWithPadding());
		}

		public int GetFullSizeWithPadding()
		{
			int FileNameSize = (int)((Definition.FileNameLength + 3) & ~3);
			if (Definition.FileNameLength % 4 == 0)
			{
				FileNameSize += 4;
			}

			return (int)(Size + FileNameSize);
		}
	}
}
