using System;
using System.Runtime.InteropServices;
using System.Text;

namespace S7Packer.Source
{
	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	public struct BBADataEntryDefinition
	{
		[MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
		public uint[] TimeStamp;
		public uint DecompressedFileSize;
		public uint DecompressedCrc32;
		public uint FileType;
		[MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
		public byte[] Padding1;
		public uint FileOffset;
		[MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
		public byte[] Padding2;
		public uint CompressedFileSize;
		public uint CompressedCrc32;
		public uint BlockSize;
		[MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
		public byte[] Padding3;
		public uint FileNameLength;
		public uint FileNameOffset;
		public uint NextDirectoryOffset;
		public uint NextFileOffset;
		// string FileName follows (dynamic length)
	}

	internal class BBADataEntry
	{
		private BBADataEntryDefinition Definition;
		public ref BBADataEntryDefinition GetDefinition() => ref Definition;
		public string FileName;

		public uint[] GetTimeStamp(DateTime Date)
		{
			byte[] Result = BitConverter.GetBytes(Date.Ticks);
			return [BitConverter.ToUInt32(Result, 0), BitConverter.ToUInt32(Result, 4)];
		}

		public BBADataEntry(ReadOnlySpan<uint> Data, DateTime Date = default) // Data.Length >= 17
		{
			if (Data.IsEmpty)
			{
				return;
			}

            Definition = new BBADataEntryDefinition
			{
				TimeStamp = Date != default ? GetTimeStamp(Date) : [Data[0], Data[1]],
				DecompressedFileSize = Data[2],
				DecompressedCrc32 = Data[3],
				FileType = Data[4],
				Padding1 = new byte[4],
				FileOffset = Data[6],
				Padding2 = new byte[4],
				CompressedFileSize = Data[8],
				CompressedCrc32 = Data[9],
				BlockSize = Data[10],
				Padding3 = new byte[4],
				FileNameLength = Data[12],
				FileNameOffset = Data[13],
				NextDirectoryOffset = Data[14],
				NextFileOffset = Data[15],
			};

			ReadOnlySpan<byte> Name = MemoryMarshal.Cast<uint, byte>(Data[16..]);
			FileName = Encoding.ASCII.GetString(Name)[..(int)Definition.FileNameLength].TrimEnd('\0');
		}

		public byte[] Serialize()
		{
			byte[] Name = Encoding.ASCII.GetBytes(FileName);
			byte[] Result = Utility.Serialize(Definition);
			int Size = Result.Length;
			int NewSize = Size + (Name.Length + 3 & ~3);

			if (Definition.FileNameLength % 4 == 0)
			{
				NewSize += 4;
			}

			Array.Resize(ref Result, NewSize);
			Name.CopyTo(Result, Size);
			return Result;
		}
	}
}
