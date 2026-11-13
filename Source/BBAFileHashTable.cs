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
			BBAArchiveHashTableEntryDefinition[] ExistingEntries = (BBAArchiveHashTableEntryDefinition[])HashTableData.Clone();
			uint[] Hashes = new uint[Files.Count];
			uint[] Offsets = new uint[Files.Count];
			uint Offset = 0;
			for (int i = 0; i < Files.Count; i++)
			{
				BBADataEntry DataEntry = Files[i];
				Hashes[i] = Utility.GetCRC32OfData(Encoding.ASCII.GetBytes(DataEntry.FileName));
				Offsets[i] = Offset;
				Offset += (uint)DataEntry.GetFullSizeWithPadding();
			}

			int ExistingEntryCount = 0;
			for (int i = 0; i < ExistingEntries.Length; i++)
			{
				if (ExistingEntries[i].CRC32 != 0)
				{
					ExistingEntryCount++;
				}
			}

			bool[] ReusedSlots = new bool[ExistingEntries.Length];
			int[] SlotIndexes = new int[Files.Count];
			bool CanReuseSlots = ExistingEntryCount == Files.Count;
			for (int i = 0; i < Files.Count; i++)
			{
				int SlotIndex = -1;
				for (int j = 0; j < ExistingEntries.Length; j++)
				{
					if (!ReusedSlots[j] && ExistingEntries[j].CRC32 == Hashes[i] && Hashes[i] != 0)
					{
						SlotIndex = j;
						break;
					}
				}

				if (SlotIndex == -1)
				{
					CanReuseSlots = false;
					break;
				}

				SlotIndexes[i] = SlotIndex;
				ReusedSlots[SlotIndex] = true;
			}

			Array.Clear(HashTableData);
			if (CanReuseSlots)
			{
				for (int i = 0; i < Files.Count; i++)
				{
					HashTableData[SlotIndexes[i]] = new BBAArchiveHashTableEntryDefinition
					{
						CRC32 = Hashes[i],
						Offset = Offsets[i]
					};
				}

				return MemoryMarshal.AsBytes(HashTableData.AsSpan());
			}

			bool[] Occupied = new bool[HashTableData.Length];
			uint Mask = AmountOfHashTableEntries - 1;
			for (int i = 0; i < Files.Count; i++)
			{
				uint HashTableIndex = Hashes[i] & Mask;
				while (Occupied[HashTableIndex])
				{
					HashTableIndex = (HashTableIndex + 1) & Mask;
				}

				HashTableData[HashTableIndex] = new BBAArchiveHashTableEntryDefinition
				{
					CRC32 = Hashes[i],
					Offset = Offsets[i]
				};
				Occupied[HashTableIndex] = true;
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
