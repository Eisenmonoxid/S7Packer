using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.IO.Hashing;
using System.Runtime.InteropServices;

namespace S7Packer.Source
{
	internal class BBAArchiveFile
    {
		private const int ONE_MIB = 1048576;
		private const uint PROD_HEAD_DELTA = 1231853475;
		private const int PROD_HEAD_LOOP = 7;
		private const uint PROD_FILE_DELTA = 1532281822;
		private const int PROD_FILE_LOOP = 8;
		private const uint DEMO_HEAD_DELTA = 1131190179;
		private const int DEMO_HEAD_LOOP = 6;
		private const uint DEMO_FILE_DELTA = 1532019678;
		private const int DEMO_FILE_LOOP = 9;
		// ------------------------------------------------------------ \\
		private readonly FileStream ArchiveFileStream;
		private readonly string ArchiveOutputDirectoryPath;
		private readonly BinaryReader GlobalArchiveReader;
		private readonly BBAFileHashTable GlobalHashTable;
		private readonly List<BBADataEntry> GlobalFileData;
		private readonly BBADirectory GlobalDirectory;
		private readonly BBAHeader GlobalHeader;
		private readonly BBADirectoryHeader GlobalDirectoryHeader;
		private readonly bool IS_PRODUCT = true; // TODO
		private readonly Dictionary<BBAArchiveDataEntryType, List<string>> FileTypeExtensions = new()
		{
			{BBAArchiveDataEntryType.TEA, 			[]},
			{BBAArchiveDataEntryType.Compressed, 	[]},
			{BBAArchiveDataEntryType.None, 			[]}
		};

		public BBAArchiveFile(FileStream Archive)
		{
			ArchiveFileStream = Archive;

			FileInfo Info = new(ArchiveFileStream.Name);
			ArchiveOutputDirectoryPath = Path.Combine(Info.DirectoryName, Path.GetFileNameWithoutExtension(Info.Name) + "_Extracted");

			GlobalArchiveReader = new(ArchiveFileStream);
			ArchiveFileStream.Seek(0, SeekOrigin.Begin);
			IS_PRODUCT = true; //IsDemoOrProductGameVersion(GlobalArchiveReader);

			try
			{
				GlobalHeader = new(GlobalArchiveReader.ReadBytes((int)BBAHeader.Size));
				GlobalDirectoryHeader = ParseBBADirectoryHeader(GlobalArchiveReader);
				Tuple<BBADirectory, ReadOnlyMemory<byte>> Data = ParseBBADirectoryAndFileDictionary(GlobalArchiveReader);
				GlobalDirectory = Data.Item1;
				GlobalFileData = ParseBBADataEntries(Data.Item2[(int)(GlobalDirectory.GetDefinition().OffsetFileEntries + 4)..], GlobalDirectory.GetDefinition().NumberOfFiles);
				GlobalHashTable = ParseHashTable(Data.Item2[(int)GlobalDirectory.GetDefinition().OffsetFileHashtable..]);
				GlobalHashTable.LinkHashTableEntriesToDataEntries(GlobalFileData);
			}
			catch (Exception ex)
			{
				Console.WriteLine("[ERROR] " + ex.Message);
				throw;
			}

			Console.WriteLine("[INFO] Loaded BBA Archive with " + GlobalFileData.Count + " files.");
		}

		/*
		private bool IsDemoOrProductGameVersion(BinaryReader Reader)
		{
			try
			{
				ParseBBADirectoryHeaderFromFile(Reader);
			}
			catch (ArgumentOutOfRangeException)
			{
				return false;
			}
			finally
			{
				Reader.BaseStream.Seek(0, SeekOrigin.Begin);
			}

			return true;
		}
		*/

		public bool ExtractAllFilesFromArchive()
		{
			uint Delta = IS_PRODUCT ? PROD_FILE_DELTA : DEMO_FILE_DELTA;
            int LoopCount = IS_PRODUCT ? PROD_FILE_LOOP : DEMO_FILE_LOOP;

			bool Result = true;
			foreach (var Element in GlobalFileData)
			{
				if (Element.FileName != ".")
				{
					Result &= ExtractFileFromArchive(Element, Delta, LoopCount);
				}
			}

			Console.WriteLine("\n[INFO] All files extracted to: " + ArchiveOutputDirectoryPath);
			return Result;
		}

		private bool ExtractFileFromArchive(BBADataEntry CurrentFile, uint Delta, int LoopCount)
		{
			ref BBADataEntryDefinition Definition = ref CurrentFile.GetDefinition();
			GlobalArchiveReader.BaseStream.Position = Definition.FileOffset;

			string OutputPath = Path.Combine(ArchiveOutputDirectoryPath, CurrentFile.FileName).Replace('\\', Path.DirectorySeparatorChar);
			string OutputDirectory = Path.GetDirectoryName(OutputPath);

			if (!Directory.Exists(OutputDirectory))
			{
				Directory.CreateDirectory(OutputDirectory);
			}

			if (!Path.HasExtension(OutputPath))
			{
				return true;
			}

            FileStream WriteStream;
            try
			{
				WriteStream = File.OpenWrite(OutputPath);
			}
			catch (Exception ex)
			{
				Console.WriteLine("[ERROR] Could not open file for writing: " + OutputPath + "\n" + ex.Message);
				return false;
			}

            using BinaryWriter Writer = new(WriteStream);
            uint StoredSize = Definition.FileType == (uint)BBAArchiveDataEntryType.TEA
                ? (Definition.CompressedFileSize + 3) & ~3u : Definition.CompressedFileSize;
            byte[] Source = GlobalArchiveReader.ReadBytes((int)StoredSize);

            switch (Definition.FileType)
            {
				case (uint)BBAArchiveDataEntryType.Compressed:
				{
					Console.WriteLine("[ZLIB] Name: " + CurrentFile.FileName);
					for (int Offset = 0; Offset < Source.Length; Offset += ONE_MIB)
					{
						int ChunkSize = Math.Min(ONE_MIB, Source.Length - Offset);
						Span<byte> Chunk = Source.AsSpan(Offset, ChunkSize);
						Crypt.DecryptCompressedChunk(Chunk, Definition.FileNameLength, IS_PRODUCT);
					}

					using MemoryStream Memory = new(Source);
					using GZipStream GZip = new(Memory, CompressionMode.Decompress);
					GZip.CopyTo(Writer.BaseStream);
					break;
				}
                case (uint)BBAArchiveDataEntryType.TEA:
				{
					Console.WriteLine("[TEA] Name: " + CurrentFile.FileName);
                    Crypt.HandleTEAFile(Source, Definition.FileNameLength, Definition.BlockSize, Delta, LoopCount, false);
                    Writer.Write(Source, 0, (int)Definition.DecompressedFileSize);
                    break;
				}
                default:
				{
					Console.WriteLine("[DEFAULT] Name: " + CurrentFile.FileName);
                    Writer.Write(Source);
                    break;
				}
            }

			Writer.Dispose();

			long TimeStamp = (long)Definition.TimeStamp;
			if (Definition.TimeStamp == 0)
			{
				TimeStamp = DateTime.Now.Ticks;
			}

			new FileInfo(OutputPath)
			{
				LastWriteTime = new DateTime(1600, 1, 1).AddTicks(TimeStamp)
			};

			return true;
        }

		private void UpdateFileTypeExtensions(BBADataEntry File)
		{
			string Extension = Path.GetExtension(File.FileName).ToLowerInvariant();
			BBAArchiveDataEntryType FileType = (BBAArchiveDataEntryType)File.GetDefinition().FileType;

			if (!string.IsNullOrEmpty(Extension) &&
				FileTypeExtensions.TryGetValue(FileType, out List<string> Extensions) &&
				!Extensions.Contains(Extension))
			{
				Extensions.Add(Extension);
			}
		}

		private List<BBADataEntry> ParseBBADataEntries(ReadOnlyMemory<byte> Data, UInt32 AmountOfFiles)
		{
			List<BBADataEntry> Result = new((int)AmountOfFiles);

			int DataIndex = 0;
			for (uint i = 0; i < AmountOfFiles; i++)
			{
				BBADataEntry Entry = new(Data[DataIndex..].Span);
				Result.Add(Entry);
				UpdateFileTypeExtensions(Entry);

				DataIndex += Entry.GetFullSizeWithPadding();
			}

			return Result;
		}

		private Tuple<BBADirectory, ReadOnlyMemory<byte>> ParseBBADirectoryAndFileDictionary(BinaryReader Reader)
		{
            uint Delta = IS_PRODUCT ? PROD_FILE_DELTA : DEMO_FILE_DELTA;
            int LoopCount = IS_PRODUCT ? PROD_FILE_LOOP : DEMO_FILE_LOOP;

            Reader.BaseStream.Position = (int)GlobalDirectoryHeader.GetDefinition().OffsetLow;
            byte[] Bytes = Reader.ReadBytes((int)GlobalDirectoryHeader.GetDefinition().Length);
            Crypt.Decrypt(Bytes, BBADirectory.Keys, Delta, LoopCount);

			return new(new BBADirectory(Bytes), Bytes.AsMemory());
		}

        private BBADirectoryHeader ParseBBADirectoryHeader(BinaryReader Reader)
        {
			uint Delta = IS_PRODUCT ? PROD_HEAD_DELTA : DEMO_HEAD_DELTA;
            int LoopCount = IS_PRODUCT ? PROD_HEAD_LOOP : DEMO_HEAD_LOOP;

			byte[] Bytes = Reader.ReadBytes((int)GlobalHeader.GetDefinition().Size);
			Crypt.Decrypt(Bytes, BBADirectoryHeader.Keys, Delta, LoopCount);

			return new BBADirectoryHeader(Bytes);
        }

		private static BBAFileHashTable ParseHashTable(ReadOnlyMemory<byte> Data)
		{
			Int32 Offset = 4;
			UInt32 AmountOfHashTableEntries = MemoryMarshal.Read<UInt32>(Data[..Offset].Span);
			BBAFileHashTable HashTable = new(AmountOfHashTableEntries);
			return HashTable.ParseHashTableFromHeader(Data[Offset..].Span);
		}

		private BBAArchiveDataEntryType GetOriginalFileTypeByName(string FileName)
		{
			if (string.IsNullOrEmpty(FileName))
			{
				return BBAArchiveDataEntryType.None;
			}

			string Extension = Path.GetExtension(FileName).ToLowerInvariant();
			foreach (var Element in FileTypeExtensions)
			{
				for (int i = 0; i < Element.Value.Count; i++)
				{
					if (string.Equals(Element.Value[i], Extension, StringComparison.Ordinal))
					{
						return Element.Key;
					}
				}
			}

			return BBAArchiveDataEntryType.None;
		}

		public bool CreateArchiveFileByFolder(string InputFolderPath)
		{
			MemoryStream TemporaryFileData = new();

			List<BBADataEntry> PackedFiles = [];
			List<string> ElementsToPack = Utility.GetFiles(InputFolderPath);
			ElementsToPack.Insert(0, InputFolderPath);

            foreach (string Element in ElementsToPack)
			{
				var Entry = CreateDataEntry(InputFolderPath, Element, TemporaryFileData);
				if (Entry == null)
				{
					continue;
				}
				
				PackedFiles.Add(Entry);
            }

			string ArchiveFileName = ArchiveFileStream.Name;
			ArchiveFileStream.Dispose();

			bool Result = WriteFullBBAArchiveFile(PackedFiles, TemporaryFileData, ArchiveFileName + "TEST");
			return Result;
		}

		private BBADataEntry AddRawFileData(string BasePath, string FullPath, MemoryStream TemporaryFileData)
		{
			BBADataEntry Entry = SetupBasicDataEntry(BasePath, FullPath, BBAArchiveDataEntryType.None);
			ref BBADataEntryDefinition Definition = ref Entry.GetDefinition();

			FileStream Input;
			try
			{
				Input = File.OpenRead(FullPath);
			}
			catch (Exception ex)
			{
				Console.WriteLine("\n[ERROR] Could not open file \"CurrentPath\": " + ex.Message);
				return null;
			}

            Crc32 CRC = new();
            long Position = TemporaryFileData.Position;
			byte[] Buffer = new byte[Math.Min(ONE_MIB, Input.Length - Input.Position)];

			int BytesRead;
			while ((BytesRead = Input.Read(Buffer, 0, Buffer.Length)) > 0)
			{
				CRC.Append(Buffer.AsSpan(0, BytesRead));
                TemporaryFileData.Write(Buffer, 0, BytesRead);
            }

			uint Length = (uint)(TemporaryFileData.Position - Position);
			Definition.DecompressedFileSize = Length;
            Definition.CompressedFileSize = Length;

			uint CRC32 = CRC.GetCurrentHashAsUInt32();
			Definition.DecompressedCRC32 = CRC32;
			Definition.CompressedCRC32 = CRC32;

            return Entry;
        }

		private BBADataEntry AddGZIPCompressedData(string BasePath, string FullPath, MemoryStream TemporaryFileData)
		{
			BBADataEntry Entry = SetupBasicDataEntry(BasePath, FullPath, BBAArchiveDataEntryType.Compressed);
			ref BBADataEntryDefinition Definition = ref Entry.GetDefinition();

			FileStream Input;
			try
			{
				Input = File.OpenRead(FullPath);
			}
			catch (Exception ex)
			{
				Console.WriteLine("\n[ERROR] Could not open file \"CurrentPath\": " + ex.Message);
				return null;
			}

			using MemoryStream CompressedBuffer = new();
			using GZipStream GZIP = new(CompressedBuffer, CompressionMode.Compress, true);

			Crc32 DecompressedCRC = new();
			Crc32 CompressedCRC = new();

			byte[] Buffer = new byte[ONE_MIB];
			int BytesRead;
			while ((BytesRead = Input.Read(Buffer, 0, Buffer.Length)) > 0)
			{
				DecompressedCRC.Append(Buffer.AsSpan(0, BytesRead));
				GZIP.Write(Buffer, 0, BytesRead);
			}

			GZIP.Dispose();

			CompressedBuffer.Position = 0;
			byte[] OutputBuffer = new byte[ONE_MIB];
			while ((BytesRead = CompressedBuffer.Read(OutputBuffer, 0, OutputBuffer.Length)) > 0)
			{
				Span<byte> Block = OutputBuffer.AsSpan(0, BytesRead);
				Crypt.EncryptCompressedChunk(Block, Definition.FileNameLength, IS_PRODUCT);

				CompressedCRC.Append(Block);
				TemporaryFileData.Write(Block);

				Definition.CompressedFileSize += (uint)BytesRead;
			}

			Definition.DecompressedCRC32 = DecompressedCRC.GetCurrentHashAsUInt32();
			Definition.CompressedCRC32 = CompressedCRC.GetCurrentHashAsUInt32();

			return Entry;
		}

		private BBADataEntry AddTEAEncryptedData(string BasePath, string FullPath, MemoryStream TemporaryFileData)
		{
			BBADataEntry Entry = SetupBasicDataEntry(BasePath, FullPath, BBAArchiveDataEntryType.TEA);
			ref BBADataEntryDefinition Definition = ref Entry.GetDefinition();

			FileStream Input;
			try
			{
				Input = File.OpenRead(FullPath);
			}
			catch (Exception ex)
			{
				Console.WriteLine("\n[ERROR] Could not open file \"CurrentPath\": " + ex.Message);
				return null;
			}

			uint Delta = IS_PRODUCT ? PROD_FILE_DELTA : DEMO_FILE_DELTA;
			int LoopCount = IS_PRODUCT ? PROD_FILE_LOOP : DEMO_FILE_LOOP;

			Crc32 DecompressedCRC = new();
			Crc32 CompressedCRC = new();

			byte[] Buffer = new byte[ONE_MIB];
			long TotalLength = 0;
			long StoredLength = 0;

			while (true)
			{
				int BytesRead = Input.ReadAtLeast(Buffer, Buffer.Length, throwOnEndOfStream: false);
				if (BytesRead == 0)
				{
					break;
				}

				DecompressedCRC.Append(Buffer.AsSpan(0, BytesRead));
				TotalLength += BytesRead;

				byte[] EncryptedBlock = Buffer;
				if (BytesRead < Buffer.Length)
				{
					int PaddedLength = (BytesRead + 3) & ~3;
					EncryptedBlock = new byte[PaddedLength];
					Buffer.AsSpan(0, BytesRead).CopyTo(EncryptedBlock);
				}

				Crypt.HandleTEAFile(EncryptedBlock, Definition.FileNameLength, ONE_MIB, Delta, LoopCount, true);
				CompressedCRC.Append(EncryptedBlock);
				TemporaryFileData.Write(EncryptedBlock);
				StoredLength += EncryptedBlock.Length;

				if (BytesRead < Buffer.Length)
				{
					break;
				}
			}

			Definition.DecompressedFileSize = (uint)TotalLength;
			Definition.CompressedFileSize = (uint)StoredLength;
			Definition.DecompressedCRC32 = DecompressedCRC.GetCurrentHashAsUInt32();
			Definition.CompressedCRC32 = CompressedCRC.GetCurrentHashAsUInt32();

			return Entry;
		}

		private static BBADataEntry SetupBasicDataEntry(string BasePath, string FilePath, BBAArchiveDataEntryType FileType)
		{
			BBADataEntry Entry = new();
			ref BBADataEntryDefinition Definition = ref Entry.GetDefinition();
			string Name = Utility.SanitizePath(BasePath, FilePath);

			Entry.FileName = Name;
			Definition.FileNameLength = (uint)Name.Length;
			Definition.FileNameOffset = (uint)Name.LastIndexOf('\\') + 1;
			Definition.FileType = (uint)FileType;
			Definition.TimeStamp = Utility.GetLastWriteTimeFromFileOrDirectory(FilePath);
			Definition.BlockSize = ONE_MIB;
			Definition.NextFileOffset = uint.MaxValue;
			Definition.NextDirectoryOffset = uint.MaxValue;

			FileInfo Info = new(FilePath);
			Definition.DecompressedFileSize = (uint)Info.Length;
			Definition.CompressedFileSize = 0;

			return Entry;
		}

		private byte[] BuildBBADirectory(List<BBADataEntry> Files, byte[] FileDictionary)
		{
			MemoryStream Directory = new();
			Directory.Seek(0, SeekOrigin.Begin);

			BBADirectory RawDirectory = new((uint)FileDictionary.Length + BBADirectory.Size, (uint)Files.Count);
			Directory.Write(RawDirectory.Serialize());

			Directory.Write(FileDictionary);

			uint HashTableSize = BBAFileHashTable.CalculateHashTableSize((uint)Files.Count);
			BBAFileHashTable HashTableData = GlobalHashTable.TotalSize == HashTableSize
				? GlobalHashTable
				: new BBAFileHashTable(HashTableSize);
			Span<byte> HashTable = HashTableData.SerializeHashTable(Files);
			Directory.Write(BitConverter.GetBytes(HashTableData.TotalSize));
			Directory.Write(HashTable);

			byte[] Data = Directory.ToArray();
            Crypt.Encrypt(Data.AsSpan(), BBADirectory.Keys, IS_PRODUCT ? PROD_FILE_DELTA : DEMO_FILE_DELTA, IS_PRODUCT ? PROD_FILE_LOOP : DEMO_FILE_LOOP);

			return Data;
		}

		private static byte[] BuildFileDictionary(List<BBADataEntry> Files)
		{
			MemoryStream FileDictionary = new();
			FileDictionary.Seek(0, SeekOrigin.Begin);

			uint TotalSize = 0;
			uint Offset = BBAHeader.Size + BBADirectoryHeader.Size;

			for (int i = 0; i < Files.Count; i++)
			{
				BBADataEntry Element = Files[i];
				ref BBADataEntryDefinition Definition = ref Element.GetDefinition();
				uint EntrySize = (uint)Element.GetFullSizeWithPadding();
				uint NextEntryOffset = i == Files.Count - 1 ? uint.MaxValue : TotalSize + EntrySize;

				if (Definition.FileType != (uint)BBAArchiveDataEntryType.Directory)
				{
					Definition.FileOffset = Offset;
					Definition.NextFileOffset = NextEntryOffset;
					Definition.NextDirectoryOffset = uint.MaxValue;
				}
				else
				{
					Definition.NextFileOffset = uint.MaxValue;
					Definition.NextDirectoryOffset = NextEntryOffset;
				}

				Offset += Definition.CompressedFileSize;
				TotalSize += EntrySize;
				FileDictionary.Write(Element.Serialize());

				Console.WriteLine("[PACKED] Name: " + Element.FileName);
			}

			return FileDictionary.ToArray();
		}

		private bool WriteFullBBAArchiveFile(List<BBADataEntry> Files, MemoryStream TemporaryFileData, string OutputPath)
		{
			FileStream FinalArchiveFile;
			try
			{
				FinalArchiveFile = File.OpenWrite(OutputPath);
			}
			catch
			{
				TemporaryFileData.Dispose();
				return false;
			}

			using BinaryWriter Writer = new(FinalArchiveFile);

			BBAHeader Header = new();
			Writer.Write(Header.Serialize());

			byte[] FileDictionary = BuildFileDictionary(Files);
			byte[] Data = BuildBBADirectory(Files, FileDictionary);

			Crc32 CRC = new();
			CRC.Append(Data);

			BBADirectoryHeader DirectoryHeader = new(Header, (uint)TemporaryFileData.Length, (uint)Data.Length, CRC.GetCurrentHashAsUInt32());
			byte[] DirectoryHeaderData = DirectoryHeader.Encrypt(IS_PRODUCT ? PROD_HEAD_DELTA : DEMO_HEAD_DELTA, IS_PRODUCT ? PROD_HEAD_LOOP : DEMO_HEAD_LOOP);
            Writer.Write(DirectoryHeaderData);

			TemporaryFileData.Seek(0, SeekOrigin.Begin);
			TemporaryFileData.CopyTo(Writer.BaseStream);
			TemporaryFileData.Dispose();

            Writer.Write(Data);
			return true;
        }

		private BBADataEntry CreateDataEntry(string BasePath, string FilePath, MemoryStream TemporaryDataStream)
		{
			BBADataEntry Entry;
			if (File.Exists(FilePath))
			{
				BBAArchiveDataEntryType OriginalType = GetOriginalFileTypeByName(Utility.SanitizePath(BasePath, FilePath));
				if (OriginalType == BBAArchiveDataEntryType.Compressed)
				{
					Entry = AddGZIPCompressedData(BasePath, FilePath, TemporaryDataStream);
				}
				else if (OriginalType == BBAArchiveDataEntryType.TEA)
				{
					Entry = AddTEAEncryptedData(BasePath, FilePath, TemporaryDataStream);
				}
				else
				{
					Entry = AddRawFileData(BasePath, FilePath, TemporaryDataStream);
				}
			}
			else
			{
				Entry = AddDirectoryData(BasePath, FilePath);
			}

			return Entry;
		}

		private static BBADataEntry AddDirectoryData(string BasePath, string FullPath)
		{
			BBADataEntry Entry = new();
			ref BBADataEntryDefinition Definition = ref Entry.GetDefinition();
			string Name = Utility.SanitizePath(BasePath, FullPath);

			Entry.FileName = Name;
			Definition.FileNameLength = (uint)Name.Length;
			Definition.FileNameOffset = 0;
			Definition.FileType = (uint)BBAArchiveDataEntryType.Directory;
			Definition.DecompressedFileSize = 0;
			Definition.DecompressedCRC32 = 0;
			Definition.CompressedFileSize = 0;
			Definition.CompressedCRC32 = 0;
			Definition.FileOffset = 0;
			Definition.BlockSize = 0;
			Definition.TimeStamp = Utility.GetLastWriteTimeFromFileOrDirectory(FullPath);

			return Entry;
		}
	}
}
