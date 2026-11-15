using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Hashing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace S7Packer.Source
{
	public static class Utility
	{
		private const int ONE_MIB = 1048576;
		public static readonly byte[] EMPTY_GZIP_STREAM = [0x1f, 0x8b, 0x08, 0x00, 0x00, 0x00, 0x00, 
			0x00, 0x00, 0x03, 0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];
		public static FileStream OpenInputFile(string FullPath)
		{
			try
			{
				return File.OpenRead(FullPath);
			}
			catch (Exception ex)
			{
				Console.WriteLine("\n[ERROR] Could not open file \"" + FullPath + "\": " + ex.Message);
				return null;
			}
		}

		public static void CopyToWithCRC(Stream Input, Stream Output, Crc32 CRC)
		{
			byte[] Buffer = new byte[ONE_MIB];
			int BytesRead;
			while ((BytesRead = Input.Read(Buffer, 0, Buffer.Length)) > 0)
			{
				CRC.Append(Buffer.AsSpan(0, BytesRead));
				Output.Write(Buffer, 0, BytesRead);
			}
		}

		public static List<string> GetFiles(string Input)
		{
			List<string> Files = [.. Directory.GetFiles(Input)];
			string[] Directories = Directory.GetDirectories(Input);
			for (int i = 0; i < Directories.Length; i++)
			{
				string Element = Directories[i];
				Files.Add(Element);
				Files.AddRange(GetFiles(Element));
			}

			return Files;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static byte[] Serialize<T>(in T Struct, int Size = 0) where T : struct
		{
			byte[] Buffer = new byte[Size == 0 ? GetSerializedSize<T>() : Size];
			MemoryMarshal.Write(Buffer, in Struct);
			return Buffer;
		}
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static int GetSerializedSize<T>() where T : struct => Marshal.SizeOf<T>();
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static ref readonly T Parse<T>(ReadOnlySpan<byte> Buffer) where T : struct => ref MemoryMarshal.AsRef<T>(Buffer);

		public static UInt32 GetCRC32OfData(ReadOnlySpan<byte> Data)
		{
			Crc32 CRC = new();
			CRC.Append(Data);
			return CRC.GetCurrentHashAsUInt32();
		}

		public static UInt64 GetLastWriteTimeFromFileOrDirectory(string FileOrDirectoryPath)
		{
			Func<string, DateTime> GetWriteTime = File.Exists(FileOrDirectoryPath) ? File.GetLastWriteTime : Directory.GetLastWriteTime;
			try
			{
				return BBADataEntry.GetTimeStamp(GetWriteTime(FileOrDirectoryPath));
			}
			catch
			{
				return 0;
			}
		}

		public static string SanitizePath(string BasePath, string FullPath)
		{
			string Text = FullPath.Replace(BasePath, string.Empty).Replace('/', '\\').Replace("\\\\", "\\").TrimStart('\\');
			return Text.Length == 0 ? "." : Text;
		}
	}
}
