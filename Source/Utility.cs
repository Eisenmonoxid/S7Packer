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
		public static FileStream OpenFileStream(string Path)
		{
			try
			{
				return new FileStream(Path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex.Message);
				return null;
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

		public static string SanitizePath(string Base, string Result)
		{
			string Text = Result.Replace(Base, string.Empty).Replace('/', '\\');
			Text = Text.Replace("\\\\", "\\");
			if (Text.StartsWith('\\'))
			{
				Text = Text.TrimStart('\\');
			}

			return string.IsNullOrEmpty(Text) ? "." : Text;
		}
	}
}
