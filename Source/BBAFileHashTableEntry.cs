using System;
using System.Collections.Generic;

namespace S7Packer.Source
{
	internal class BBAFileHashTableEntry(uint[] Data)
    {
		public uint Offset = Data[1];
		public readonly uint Hash = Data[0];
		public string FileName;

        public List<byte> CreateHashTableData() => [.. BitConverter.GetBytes(Hash), .. BitConverter.GetBytes(Offset)];

		public static List<BBAFileHashTableEntry> GetBBAFileHashTable(uint[] Data, List<BBADataEntry> Files)
		{
			List<BBAFileHashTableEntry> HashTable = [];
			Dictionary<uint, string> Mapping = [];
			uint Count = 0;
			
			foreach (BBADataEntry Element in Files)
			{
				Mapping.Add(Count, Element.FileName);
				Count += (uint)Element.Serialize().Length;
			}

			int Size = 2;
			uint[] Buffer = new uint[Size];
			for (int i = 0; i < Data.Length; i += Size)
			{
				Array.Copy(Data, i, Buffer, 0, Size);
				BBAFileHashTableEntry Table = new(Buffer);
				
				if (Table.Hash == 0)
				{
					Table.FileName = "";
				}
				else
				{
					if (!Mapping.TryGetValue(Table.Offset, out string Value))
					{
						Console.WriteLine("[ERROR]: Missing Key at HashTable Offset - " + Table.Offset);
					}

					Table.FileName = Value;
				}

				// TODO: Restore the HashTable (add/remove files instead of modifying existing ones) ...
				/*
				byte[] Text = Encoding.UTF8.GetBytes(Table.FileName);
				Crc32 CRC = new();
				CRC.Append(Text);
				uint hash = CRC.GetCurrentHashAsUInt32();
				// Debugger.Log(0, "BBAFileHashTableEntry", "Calculated Hash: " + hash + " | Stored Hash: " + Table.Hash + " | FileName: " + Table.FileName + "\n");
				*/

				HashTable.Add(Table);
			}

			return HashTable;
		}
	}
}
