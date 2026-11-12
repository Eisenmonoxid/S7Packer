using System;
using System.Linq;
using System.Runtime.InteropServices;

namespace S7Packer.Source
{
	[StructLayout(LayoutKind.Sequential, Pack = 4)]
	public unsafe struct BBAHeaderDefinition
	{
		public fixed byte Magic[3];
		public byte Version;
		public uint Reserved;
		public uint Size;
		public uint EncryptionID;
	}

	internal class BBAHeader
	{
		private BBAHeaderDefinition Definition;
		public ref BBAHeaderDefinition GetDefinition() => ref Definition;
		public byte[] Serialize() => Utility.Serialize(Definition);
		public static uint Size => (uint)Marshal.SizeOf<BBAHeaderDefinition>(); // 16

		public BBAHeader()
		{
			Definition = new BBAHeaderDefinition
			{
				Version = 6,
				Reserved = 7,
				Size = 64,
				EncryptionID = Crypt.S7_HEAD_CRYPTID
			};

			unsafe
			{
				fixed (byte* Pointer = Definition.Magic)
				{
        			"BAF"u8.CopyTo(new Span<byte>(Pointer, 3));
				}
			}
		}

		public BBAHeader(ReadOnlySpan<byte> Data)
		{
			Definition = Utility.Parse<BBAHeaderDefinition>(Data);
		}
	}
}
