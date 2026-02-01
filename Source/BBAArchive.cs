using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.IO.Hashing;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace S7Packer.Source
{
	internal class BBAArchiveFile
    {
		private enum BBAArchiveFileType
		{
			TEA = 2,
			Compressed = 17,
			None = 0
		}

		private const int FILE_NAME_NUM = 13;
		private const int ONE_MIB = 1048576;
		private const uint PROD_HEAD_DELTA = 1231853475;
		private const int PROD_HEAD_LOOP = 7;
		private const uint PROD_FILE_DELTA = 1532281822;
		private const int PROD_FILE_LOOP = 8;
		private const uint DEMO_HEAD_DELTA = 1131190179;
		private const int DEMO_HEAD_LOOP = 6;
		private const uint DEMO_FILE_DELTA = 1532019678;
		private const int DEMO_FILE_LOOP = 9;
		private const uint HEADER_START_OFFSET = 69;
		private const uint HEADER_SIZE = 16;
		private static readonly uint[] PROD_XOR = [68040748, 166934560, 1719695735, 4247314248];
		private static readonly uint[] DEMO_XOR = [780972269, 3538276439, 2864783426, 3237865163];
		// ------------------------------------------------------------ \\
		private readonly FileStream ArchiveFileStream;
		private readonly string ArchiveOutputDirectoryPath;
		private readonly BinaryReader GlobalArchiveReader;
		private readonly List<BBAFileHashTableEntry> GlobalHashTableInfo;
		private readonly List<BBADataEntry> GlobalFileData;
		private readonly Dictionary<string, int> GlobalHashTableIndex; // File Name, Index
		private readonly BBADirectory GlobalDirectory;
		private readonly uint[] CurrentBBADirectoryHeader;
		private readonly Crypt Crypt = new();
		private readonly bool IS_PRODUCT = true;
		private readonly Dictionary<BBAArchiveFileType, List<string>> FileTypeExtensions = new()
		{
			{BBAArchiveFileType.TEA, 		[]},
			{BBAArchiveFileType.Compressed, []},
			{BBAArchiveFileType.None, 		[]}
		};

		public BBAArchiveFile(FileStream Archive)
		{
			ArchiveFileStream = Archive;

			FileInfo Info = new(ArchiveFileStream.Name);
			ArchiveOutputDirectoryPath = Path.Combine(Info.DirectoryName, Path.GetFileNameWithoutExtension(Info.Name) + "_Extracted");

			GlobalArchiveReader = new(ArchiveFileStream);
			ArchiveFileStream.Seek(0, SeekOrigin.Begin);
			IS_PRODUCT = IsDemoOrProductGameVersion(GlobalArchiveReader);

			try
			{
				CurrentBBADirectoryHeader = ParseBBADirectoryHeaderFromFile(GlobalArchiveReader);
				GlobalDirectory = GetDirectoryInfo(CurrentBBADirectoryHeader);
				GlobalFileData = GetFileInfo(CurrentBBADirectoryHeader);

				GlobalHashTableInfo = GetArchiveFileHashTable(CurrentBBADirectoryHeader, GlobalDirectory, GlobalFileData);
				GlobalHashTableIndex = GetArchiveFileHashTableIndex(GlobalHashTableInfo);
			}
			catch (Exception ex)
			{
				Console.WriteLine("[ERROR] " + ex.Message);
				throw;
			}
		}
		
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

		public bool UnpackBBAArchiveFiles()
		{
			bool Result = true;
			foreach (var Element in GlobalFileData)
			{
				if (Element.FileName != ".")
				{
					Result = ExtractFileFromArchive(Element);
				}
			}

			Console.WriteLine("\n[INFO] All files extracted to: " + ArchiveOutputDirectoryPath);
			return Result;
		}

		private ReadOnlySpan<uint> ParseBBAHeaderFromFile(BinaryReader Reader)
		{
            uint Delta = IS_PRODUCT ? PROD_HEAD_DELTA : DEMO_HEAD_DELTA;
            int LoopCount = IS_PRODUCT ? PROD_HEAD_LOOP : DEMO_HEAD_LOOP;
            ReadOnlySpan<uint> Keys = [623092400, 3578011951, 428883204, 211416760];

            BBAHeader Header = new(Reader.ReadBytes((int)HEADER_SIZE));
            byte[] Bytes = Reader.ReadBytes((int)Header.GetDefinition().HeaderSize);

            Crypt.Decrypt(Bytes, Keys, Delta, LoopCount);
			return MemoryMarshal.Cast<byte, uint>(Bytes.AsSpan());
        }

        private uint[] ParseBBADirectoryHeaderFromFile(BinaryReader Reader)
        {
            uint Delta = IS_PRODUCT ? PROD_FILE_DELTA : DEMO_FILE_DELTA;
            int LoopCount = IS_PRODUCT ? PROD_FILE_LOOP : DEMO_FILE_LOOP;
            ReadOnlySpan<uint> Keys = [892421091, 3702197895, 3064602941, 189782668];

            BBADirectoryHeader Header = new(ParseBBAHeaderFromFile(Reader));
            Reader.BaseStream.Position = (int)Header.GetDefinition().OffsetLow;
            byte[] Bytes = Reader.ReadBytes((int)Header.GetDefinition().Length);

            Crypt.Decrypt(Bytes, Keys, Delta, LoopCount);
			return MemoryMarshal.Cast<byte, uint>(Bytes.AsSpan()).ToArray();
        }

		private List<BBAFileHashTableEntry> GetArchiveFileHashTable(uint[] DirectoryHeader, BBADirectory Directory, List<BBADataEntry> FileData)
		{
			int HashTableStartIndex = (int)Directory.GetDefinition().OffsetFileHashtable / 4;
			uint[] Destination = new uint[DirectoryHeader[HashTableStartIndex] * 2];
			Array.Copy(DirectoryHeader, HashTableStartIndex + 1, Destination, 0, Destination.Length);
			return BBAFileHashTableEntry.GetBBAFileHashTable(Destination, FileData);
		}

		private Dictionary<string, int> GetArchiveFileHashTableIndex(List<BBAFileHashTableEntry> Info)
		{
			Dictionary<string, int> Dict = [];
			int Index = 0;
			foreach (var Element in Info)
			{
				if (!string.IsNullOrEmpty(Element.FileName) && !Dict.ContainsKey(Element.FileName))
				{
					Dict.Add(Element.FileName, Index);
				}

				Index++;
			}

			return Dict;
		}

		private BBAArchiveFileType GetOriginalFileTypeByName(string FileName)
		{
			string Extension = Path.GetExtension(FileName).ToLowerInvariant();
			foreach (var Element in FileTypeExtensions)
			{
				if (Element.Value.Contains(Extension))
				{
					return Element.Key;
				}
			}

			return BBAArchiveFileType.None;
		}

		private BBADataEntry GetBBAFileDefinition(string BasePath, string FilePath, BinaryWriter Writer)
		{
			BBADataEntry BBA;
			if (File.Exists(FilePath))
			{
				BBAArchiveFileType OriginalType = GetOriginalFileTypeByName(Utility.SanitizePath(BasePath, FilePath));
				if (OriginalType == BBAArchiveFileType.Compressed)
				{
					BBA = CreateBBAFileWithZLIBCompression(BasePath, FilePath, Writer);
				}
				else
				{
					BBA = CreateBBAFileWithoutCompression(BasePath, FilePath, Writer);
				}
			}
			else
			{
				BBA = CreateBBAFileForDirectory(BasePath, FilePath);
			}

			return BBA;
		}

		public bool AddFolderFilesToArchive(string InputFolderPath)
		{
			if (!Directory.Exists(InputFolderPath))
			{
				Console.WriteLine("[ERROR] Directory " + InputFolderPath + " does not exist! Aborting ...");
				return false;
			}

			List<BBADataEntry> PackedFiles = [];
			List<string> ElementsToPack = Utility.GetFiles(InputFolderPath);
			ElementsToPack.Insert(0, InputFolderPath);

			string Temporary = Path.GetTempFileName();
			BinaryWriter Writer = new(File.OpenWrite(Temporary));
            foreach (string FileOrFolder in ElementsToPack)
			{
				var BBAFile = GetBBAFileDefinition(InputFolderPath, FileOrFolder, Writer);
				PackedFiles.Add(BBAFile);
            }

			Writer.Dispose();

			string ArchiveFileName = ArchiveFileStream.Name;
			ArchiveFileStream.Dispose();
			
			string TemporaryFilePath = ArchiveFileName + BitConverter.ToString(SHA1.HashData(Encoding.ASCII.GetBytes(ArchiveFileName)));
			WriteFullBBAArchiveFile(PackedFiles, Temporary, TemporaryFilePath);

			try
			{
				File.Replace(TemporaryFilePath, ArchiveFileStream.Name, null);
				File.Delete(Temporary);
			}
			catch (Exception ex)
			{
				Console.WriteLine("[ERROR] " + ex.Message);
				return false;
			}

			return true;
        }

		private bool ExtractFileFromArchive(BBADataEntry CurrentFile)
		{
			BBADataEntryDefinition Definition = CurrentFile.GetDefinition();
			GlobalArchiveReader.BaseStream.Position = Definition.FileOffset;

			uint Delta = IS_PRODUCT ? PROD_FILE_DELTA : DEMO_FILE_DELTA;
            int LoopCount = IS_PRODUCT ? PROD_FILE_LOOP : DEMO_FILE_LOOP;

			string OutputPath = Path.Combine(ArchiveOutputDirectoryPath, CurrentFile.FileName).Replace("\\", Path.DirectorySeparatorChar.ToString());
			string OutputDirectory = Path.GetDirectoryName(OutputPath);

			if (!Directory.Exists(OutputDirectory))
			{
				Directory.CreateDirectory(OutputDirectory);
			}

			if (!Path.HasExtension(OutputPath))
			{
				return true;
			}

            using BinaryWriter Writer = new(File.OpenWrite(OutputPath));
            byte[] Source = GlobalArchiveReader.ReadBytes((int)Definition.CompressedFileSize);

            switch (Definition.FileType)
            {
				case (uint)BBAArchiveFileType.Compressed:
				{
					Console.WriteLine("File Type ZLIB - Name: " + CurrentFile.FileName);
					for (int Offset = 0; Offset < Source.Length; Offset += ONE_MIB)
					{
						int ChunkSize = Math.Min(ONE_MIB, Source.Length - Offset);
						Span<byte> Chunk = Source.AsSpan(Offset, ChunkSize);
						Crypt.DecryptCompressedFile(Chunk, Definition.FileNameLength, IS_PRODUCT ? PROD_XOR : DEMO_XOR);
					}

					using MemoryStream Memory = new(Source);
					using GZipStream GZip = new(Memory, CompressionMode.Decompress);
					GZip.CopyTo(Writer.BaseStream);
					break;
				}
                case (uint)BBAArchiveFileType.TEA:
				{
					Console.WriteLine("File Type TEA - Name: " + CurrentFile.FileName);
                    Crypt.HandleTEAFile(Source, Definition.FileNameLength, (int)Definition.BlockSize, Delta, LoopCount, false);
                    Writer.Write(Source);
                    break;
				}
                default:
				{
					Console.WriteLine("File Type DEFAULT - Name: " + CurrentFile.FileName);
                    Writer.Write(Source);
                    break;
				}
            }

			return true;
        }

		private void ModifyHashTableElement(List<BBAFileHashTableEntry> GlobalHashTable, string FileName, uint Offset)
		{
			if (!GlobalHashTableIndex.TryGetValue(FileName, out int Index))
			{
				// Create new HashTable Entry
				Crc32 CRC = new();
				CRC.Append(Encoding.UTF8.GetBytes(FileName));
                BBAFileHashTableEntry Entry = new([CRC.GetCurrentHashAsUInt32(), Offset])
                {
                    FileName = FileName
                };

				int FoundIndex = GlobalHashTable.FindIndex(Element => Element.FileName == string.Empty);
				if (FoundIndex == -1)
				{
					Console.WriteLine("[ERROR]: No empty HashTable entry found for file " + FileName);
					return;
				}

                GlobalHashTable[FoundIndex] = Entry;
			}
			else
			{
				GlobalHashTable[Index].Offset = Offset;
			}
		}

		private void CleanHashTableEntries(List<BBADataEntry> Files, List<BBAFileHashTableEntry> GlobalHashTable)
		{
			foreach (var Element in GlobalHashTable)
			{
				if (Element.FileName != string.Empty)
				{
					if (Files.FindIndex(File => File.FileName == Element.FileName) == -1)
					{
						Element.FileName = string.Empty;
						Element.Hash = 0;
						Element.Offset = 0;
					}	
				}
			}
		}

		private List<byte> UpdateBBADataEntryWithNewData(List<BBADataEntry> Files, List<BBAFileHashTableEntry> GlobalHashTable)
		{
			List<byte> Result = [];
			uint Count = 0;
			uint Size = HEADER_SIZE + 64; // 64 = BBAHeader.DEFAULT_HEADER_SIZE;

			for (int i = 0; i < Files.Count; i++)
			{
				BBADataEntry curFile = Files[i];
				ref BBADataEntryDefinition Definition = ref curFile.GetDefinition();
				byte[] FileData = curFile.Serialize();

				ModifyHashTableElement(GlobalHashTable, curFile.FileName, Count);
				Count += (uint)FileData.Length;

				if (i == Files.Count - 1)
				{
					Count = uint.MaxValue;
				}

				if (Definition.FileType != 256) // Directory
				{
					Definition.FileOffset = Size;
					Definition.NextFileOffset = Count;
					Definition.NextDirectoryOffset = uint.MaxValue;
				}
				else
				{
					Definition.NextFileOffset = uint.MaxValue;
					Definition.NextDirectoryOffset = Count;
				}

				Size += Definition.CompressedFileSize;
				Result.AddRange(curFile.Serialize());
				Console.WriteLine("Packing file: " + curFile.FileName);
            }

			return Result;
		}

		private void WriteFullBBAArchiveFile(List<BBADataEntry> Files, string filePath, string output)
		{
			List<BBAFileHashTableEntry> GlobalHashTable = [.. GlobalHashTableInfo];
			FileInfo Info = new(filePath);
			BBAHeader curHeader = new(default);
			List<byte> UpdatedBBAEntry = UpdateBBADataEntryWithNewData(Files, GlobalHashTable);
			CleanHashTableEntries(Files, GlobalHashTable);

            BBADirectory Directory = new(default);
			ref BBADirectoryDefinition bbaDirectoryDef = ref Directory.GetDefinition();
			bbaDirectoryDef.NumberOfFiles = (uint)Files.Count;
            bbaDirectoryDef.OffsetFileHashtable = (uint)(UpdatedBBAEntry.Count + Directory.Serialize().Length);

			UpdatedBBAEntry.InsertRange(0, Directory.Serialize());
			UpdatedBBAEntry.AddRange(BitConverter.GetBytes(GlobalHashTable.Count));

			foreach (BBAFileHashTableEntry Element in GlobalHashTable)
			{
				UpdatedBBAEntry.AddRange(Element.CreateHashTableData());
			}

			uint[] Keys = [892421091, 3702197895, 3064602941, 189782668];
			uint Delta = IS_PRODUCT ? PROD_FILE_DELTA : DEMO_FILE_DELTA;
			int LoopCount = IS_PRODUCT ? PROD_FILE_LOOP : DEMO_FILE_LOOP;

			byte[] Data = [.. UpdatedBBAEntry];
			Crypt.Encrypt(Data.AsSpan(), Keys, Delta, LoopCount);

            using BinaryReader Reader = new(File.OpenRead(filePath));
            using BinaryWriter Writer = new(File.OpenWrite(output));

            Writer.Write(curHeader.Serialize());
            Keys = [623092400, 3578011951, 428883204, 211416760];

			Delta = IS_PRODUCT ? PROD_HEAD_DELTA : DEMO_HEAD_DELTA;
			LoopCount = IS_PRODUCT ? PROD_HEAD_LOOP : DEMO_HEAD_LOOP;

			Crc32 CRC = new();
			CRC.Append(Data);

			byte[] HeaderData = CreateDirectoryHeader(curHeader, (uint)(int)Info.Length, (uint)Data.Length, CRC.GetCurrentHashAsUInt32());
            Crypt.Encrypt(HeaderData, Keys, Delta, LoopCount);
            Writer.Write(HeaderData);

            while (Reader.BaseStream.Position < Reader.BaseStream.Length)
            {
                Writer.Write(Reader.ReadBytes(ONE_MIB));
            }

            Writer.Write(Data);
        }

		private byte[] CreateDirectoryHeader(BBAHeader Header, uint FileLength, uint HeaderLength, uint CRC32)
		{
			uint OffsetLow = HEADER_SIZE + Header.GetDefinition().HeaderSize + FileLength;
			uint[] HeaderData = [OffsetLow, 0, HeaderLength, CRC32, 0, 0];
            BBADirectoryHeader Result = new(HeaderData);
			return Result.Serialize();
		}

		private BBADataEntry SetupBBADataEntry(string BasePath, string FilePath, BBAArchiveFileType FileType)
		{
			BBADataEntry BBAFile = new(default);
			ref BBADataEntryDefinition Definition = ref BBAFile.GetDefinition();
			string Name = Utility.SanitizePath(BasePath, FilePath);

			BBAFile.FileName = Name;
			Definition.FileNameLength = (uint)Name.Length;
			Definition.FileNameOffset = (uint)Name.LastIndexOf('\\') + 1;
			Definition.FileType = (uint)FileType;
			Definition.TimeStamp = BBAFile.GetTimeStamp(File.GetCreationTime(FilePath).AddYears(-1600));
			Definition.BlockSize = ONE_MIB;

			FileInfo Info = new(FilePath);
			Definition.DecompressedFileSize = (uint)Info.Length;
			Definition.CompressedFileSize = 0;

			return BBAFile;
		}

		private BBADataEntry CreateBBAFileWithoutCompression(string BasePath, string CurrentPath, BinaryWriter Writer)
		{
			BBADataEntry curFile = SetupBBADataEntry(BasePath, CurrentPath, BBAArchiveFileType.None);
			ref BBADataEntryDefinition Definition = ref curFile.GetDefinition();
            using FileStream Input = File.OpenRead(CurrentPath);
            Crc32 CRC = new();
            long Position = Writer.BaseStream.Position;
			byte[] Buffer = new byte[Math.Min(ONE_MIB, Input.Length - Input.Position)];

			int bytesRead;
			while ((bytesRead = Input.Read(Buffer, 0, Buffer.Length)) > 0)
			{
				CRC.Append(Buffer.AsSpan(0, bytesRead));
                Writer.Write(Buffer, 0, bytesRead);
            }

			uint Length = (uint)(Writer.BaseStream.Position - Position);
			Definition.DecompressedFileSize = Length;
            Definition.CompressedFileSize = Length;

			uint CRC32 = CRC.GetCurrentHashAsUInt32();
			Definition.DecompressedCrc32 = CRC32;
			Definition.CompressedCrc32 = CRC32;

            return curFile;
        }

		private BBADataEntry CreateBBAFileWithZLIBCompression(string BasePath, string CurrentPath, BinaryWriter Writer)
		{
			BBADataEntry bbaFile = SetupBBADataEntry(BasePath, CurrentPath, BBAArchiveFileType.Compressed);
			ref BBADataEntryDefinition Definition = ref bbaFile.GetDefinition();

			using FileStream Input = File.OpenRead(CurrentPath);
			using MemoryStream CompressedBuffer = new();
			using GZipStream GZIP = new(CompressedBuffer, CompressionMode.Compress, true);

			Crc32 DecompressedCRC = new();
			Crc32 CompressedCRC   = new();

			byte[] inputBuffer = new byte[ONE_MIB];
			int bytesRead;
			while ((bytesRead = Input.Read(inputBuffer, 0, inputBuffer.Length)) > 0)
			{
				DecompressedCRC.Append(inputBuffer.AsSpan(0, bytesRead));
				GZIP.Write(inputBuffer, 0, bytesRead);
			}

			GZIP.Dispose();

			CompressedBuffer.Position = 0;
			byte[] outputBuffer = new byte[ONE_MIB];

			while ((bytesRead = CompressedBuffer.Read(outputBuffer, 0, outputBuffer.Length)) > 0)
			{
				byte[] curBlock = outputBuffer[..bytesRead];
				Crypt.EncryptCompressedFile(curBlock.AsSpan(), Definition.FileNameLength, IS_PRODUCT ? PROD_XOR : DEMO_XOR);

				CompressedCRC.Append(curBlock);
				Writer.Write(curBlock);

				Definition.CompressedFileSize += (uint)bytesRead;
			}

			Definition.DecompressedCrc32 = DecompressedCRC.GetCurrentHashAsUInt32();
			Definition.CompressedCrc32 = CompressedCRC.GetCurrentHashAsUInt32();

			return bbaFile;
		}

		private BBADataEntry CreateBBAFileForDirectory(string BasePath, string CurrentPath)
		{
			BBADataEntry BBAFile = new(default);
			ref BBADataEntryDefinition Definition = ref BBAFile.GetDefinition();
			string Name = Utility.SanitizePath(BasePath, CurrentPath);

			BBAFile.FileName = Name;
			Definition.FileNameLength = (uint)Name.Length;
			Definition.FileNameOffset = 0;
			Definition.FileType = 256;
			Definition.DecompressedFileSize = 0;
			Definition.DecompressedCrc32 = 0;
			Definition.CompressedFileSize = 0;
			Definition.CompressedCrc32 = 0;
			Definition.FileOffset = 0;
			Definition.BlockSize = 0;
			Definition.TimeStamp = new uint[2];

			return BBAFile;
		}

		private BBADirectory GetDirectoryInfo(uint[] Data)
		{
			uint Size = HEADER_START_OFFSET;
			uint[] Current = new uint[Size];
			Array.Copy(Data, 0, Current, 0, Size);
			return new BBADirectory(Current);
		}

		private List<BBADataEntry> GetFileInfo(uint[] Data)
		{
			List<BBADataEntry> Result = [];

			uint HeaderSizeWithoutFileName = HEADER_SIZE;
			uint curFileNameLength = 0;

			int FileIndex = 1;
			int curEntryStartIndex = (int)HEADER_START_OFFSET;
			int curEntryLength = 0;
			int DataIndex = (int)HEADER_START_OFFSET;

			for (; DataIndex < Data.Length; DataIndex++, FileIndex++, curEntryLength++)
			{
				if (FileIndex == FILE_NAME_NUM)
				{
					if (Data[DataIndex] == 0)
					{
						break;
					}

					curFileNameLength = (uint)Math.Ceiling(Data[DataIndex] / 4.0); // Padded to 4 byte
				}
				else if (FileIndex > HeaderSizeWithoutFileName && (FileIndex - HeaderSizeWithoutFileName) > curFileNameLength)
				{
					Result.Add(new BBADataEntry(Data.AsSpan(curEntryStartIndex, curEntryLength)));
					ref BBADataEntryDefinition Definition = ref Result[^1].GetDefinition();

					FileIndex = (Definition.FileNameLength % 4 == 0) ? 0 : 1;
					curEntryStartIndex = DataIndex + (FileIndex == 0 ? 1 : 0);
            		curEntryLength = 0;

					string Extension = Path.GetExtension(Result[^1].FileName).ToLowerInvariant();
					bool Found = FileTypeExtensions.TryGetValue((BBAArchiveFileType)Definition.FileType, out List<string> Extensions);
					if (Found && !string.IsNullOrEmpty(Extension) && !Extensions.Contains(Extension))
					{
						Extensions.Add(Extension);
					}
				}
			}
			
			return Result;
		}
	}
}
