using System;
using System.Collections.Generic;
using System.IO;
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
			foreach (string Element in Directory.GetDirectories(Input))
			{
                Files.Add(Element);
                Files.AddRange(GetFiles(Element));
			}

			return Files;
		}

		public static byte[] Serialize<T>(T Value) where T : struct
		{
			int Size = Marshal.SizeOf<T>();
			byte[] Buffer = new byte[Size];
			IntPtr Pointer = Marshal.AllocHGlobal(Size);

			try
			{
				Marshal.StructureToPtr(Value, Pointer, false);
				Marshal.Copy(Pointer, Buffer, 0, Size);
				return Buffer;
			}
			finally
			{
				Marshal.FreeHGlobal(Pointer);
			}
		}

		public static string SanitizePath(string Base, string Result)
		{
			string Text = Result.Replace(Base, "").Replace("/", "\\").Replace("\\\\", "\\");
			if (Text.StartsWith("\\"))
			{
				Text = Text.TrimStart('\\');
			}

			return string.IsNullOrEmpty(Text) ? "." : Text;
		}
	}
}
