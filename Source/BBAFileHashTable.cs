using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace S7Packer.Source
{
	[StructLayout(LayoutKind.Sequential, Pack = 4)]
	public struct BBAArchiveHashTableEntryDefinition
	{
		public UInt32 CRC32;
		public UInt32 Offset;
	}

	internal class BBAFileHashTable(uint AmountOfHashTableEntries)
    {
		private readonly Dictionary<BBAArchiveHashTableEntryDefinition, BBADataEntry> GlobalHashTableToDataEntryMapping = [];
		private readonly BBAArchiveHashTableEntryDefinition[] HashTableData = new BBAArchiveHashTableEntryDefinition[AmountOfHashTableEntries];
		public uint TotalSize => AmountOfHashTableEntries;

		public BBAFileHashTable ParseHashTableFromHeader(ReadOnlySpan<byte> Data)
		{
			int HashTableEntryOffset = 0;
			int Size = Utility.GetSerializedSize<BBAArchiveHashTableEntryDefinition>();
			for (uint i = 0; i < AmountOfHashTableEntries; i++)
			{
				BBAArchiveHashTableEntryDefinition Element = Utility.Parse<BBAArchiveHashTableEntryDefinition>(Data[HashTableEntryOffset..]);
				HashTableData[i] = Element;

				HashTableEntryOffset += Size;
			}

			return this;
		}

		public Span<byte> SerializeHashTable(List<BBADataEntry> Files)
		{
			uint Mask = AmountOfHashTableEntries - 1;
			uint Offset = 0;
			for (int i = 0; i < Files.Count; i++)
			{
				BBADataEntry DataEntry = Files[i];
				ReadOnlySpan<byte> FileNameData = Encoding.ASCII.GetBytes(DataEntry.FileName);
				UInt32 Hash = Utility.GetCRC32OfData(FileNameData);
				uint HashTableIndex = Hash & Mask;

				while (HashTableData[HashTableIndex].Offset != 0)
				{
					HashTableIndex = (HashTableIndex + 1) & Mask;
				}

    			HashTableData[HashTableIndex].CRC32 = Hash;
    			HashTableData[HashTableIndex].Offset = Offset;

				Offset += (uint)DataEntry.GetFullSizeWithPadding();
			}
			
			return MemoryMarshal.AsBytes(HashTableData.AsSpan());
		}
		
		public void LinkHashTableEntriesToDataEntries(List<BBADataEntry> GlobalFileData)
		{
			for (int i = 0; i < GlobalFileData.Count; i++)
			{
				BBADataEntry DataEntry = GlobalFileData[i];
				byte[] FileNameData = Encoding.ASCII.GetBytes(DataEntry.FileName);
				int DataEntryIndex = Array.FindIndex(HashTableData, Element => Element.CRC32 == Utility.GetCRC32OfData(FileNameData));
				if (DataEntryIndex != -1)
				{
					GlobalHashTableToDataEntryMapping.Add(HashTableData[DataEntryIndex], GlobalFileData[i]);
				}
			}
		}

		public static uint CalculateHashTableSize(uint AmountOfEntries)
		{
			uint Counter = AmountOfEntries + 1;
			uint Pot = 0;

			do
			{
				Counter >>= 1;
				Pot++;
			}
			while (Counter != 0);

			uint Size = 1U << (int)Pot;
			return ((AmountOfEntries * 1.5) > Size) ? Size * 4 : Size * 2;
		}
	}
}
