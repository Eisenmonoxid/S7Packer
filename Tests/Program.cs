using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.IO.Hashing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using S7Packer.Source;

// Run: dotnet run --project Tests/S7Packer.RegressionTests.csproj
// Optionally append -- <path-to-Sound.bba> to check a private copy of a real archive too.
internal static class RegressionTests
{
    private const uint BlockSize = 32768;
    private static readonly uint[] HeaderKeys = [623092400, 3578011951, 428883204, 211416760];
    private static readonly uint[] FileKeys = [892421091, 3702197895, 3064602941, 189782668];
    private static readonly Crypt Cipher = new();

    private static int Main(string[] args)
    {
        string Temporary = Path.Combine(Path.GetTempPath(), "S7Packer-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Temporary);
        TextWriter Output = Console.Out;
        try
        {
            Console.SetOut(TextWriter.Null);
            int Count = 0;
            // All four word alignments, exact block boundaries and multiple blocks.
            foreach (int Length in new[] { 0, 9, 10, 11, 12, 32765, 32766, 32767, 32768, 32777, 32778, 32779, 32780, 65536, 65545 })
            {
                string PathName = Path.Combine(Temporary, "tea-" + Length + ".bba");
                byte[] Plaintext = new byte[Length];
                new Random(Length).NextBytes(Plaintext);
                WriteFixture(PathName, Plaintext, 2);
                CheckRoundTrip(PathName);
                Count++;
            }

            // The padding read must not change ordinary or gzip-compressed entries.
            foreach (uint Type in new uint[] { 0, 17 })
            {
                string PathName = Path.Combine(Temporary, "type-" + Type + ".bba");
                WriteFixture(PathName, Encoding.ASCII.GetBytes("Unaligned audio payload for the archive regression test."), Type);
                CheckRoundTrip(PathName);
                Count++;
            }

            Console.SetOut(Output);
            Console.WriteLine("PASS: " + Count + " synthetic archive extraction/repack cases.");
            foreach (string Original in args)
            {
                // Only this private copy is opened by the packer; the original stays read-only.
                string Copy = Path.Combine(Temporary, "real-" + Guid.NewGuid().ToString("N") + ".bba");
                File.Copy(Original, Copy);
                Console.SetOut(TextWriter.Null);
                int Files = CheckRoundTrip(Copy);
                Console.SetOut(Output);
                Console.WriteLine("PASS: " + Files + " real archive files match stored size/CRC32 and repack byte-for-byte.");
            }
            return 0;
        }
        catch (Exception Error)
        {
            Console.SetOut(Output);
            Console.Error.WriteLine(Error);
            return 1;
        }
        finally
        {
            Console.SetOut(Output);
            Directory.Delete(Temporary, true);
        }
    }

    private static int CheckRoundTrip(string ArchivePath)
    {
        List<BBADataEntry> Entries = ReadEntries(ArchivePath);
        string Extracted = Path.Combine(Path.GetDirectoryName(ArchivePath), Path.GetFileNameWithoutExtension(ArchivePath) + "_Extracted");
        using (FileStream Stream = File.OpenRead(ArchivePath))
            Require(new BBAArchiveFile(Stream).UnpackBBAArchiveFiles(), "Extraction failed.");

        var Expected = new Dictionary<string, byte[]>();
        foreach (BBADataEntry Entry in Entries)
        {
            BBADataEntryDefinition Definition = Entry.GetDefinition();
            if (Definition.FileType == 256) continue;
            byte[] Bytes = File.ReadAllBytes(Path.Combine(Extracted, Entry.FileName.Replace('\\', Path.DirectorySeparatorChar)));
            Require(Bytes.Length == Definition.DecompressedFileSize, "Wrong size: " + Entry.FileName);
            Require(Crc32.HashToUInt32(Bytes) == Definition.DecompressedCrc32, "Wrong CRC32: " + Entry.FileName);
            Expected.Add(Entry.FileName, Bytes);
        }

        using (FileStream Stream = File.Open(ArchivePath, FileMode.Open, FileAccess.ReadWrite))
            Require(new BBAArchiveFile(Stream).AddFolderFilesToArchive(Extracted), "Repacking failed.");

        // Extract into a fresh directory to detect wrong output lengths as well as bytes.
        string Repacked = Path.Combine(Path.GetDirectoryName(ArchivePath), Path.GetFileNameWithoutExtension(ArchivePath) + "-repacked.bba");
        File.Copy(ArchivePath, Repacked);
        using (FileStream Stream = File.OpenRead(Repacked))
            Require(new BBAArchiveFile(Stream).UnpackBBAArchiveFiles(), "Repacked extraction failed.");
        string ReExtracted = Path.Combine(Path.GetDirectoryName(Repacked), Path.GetFileNameWithoutExtension(Repacked) + "_Extracted");
        foreach (var FileEntry in Expected)
        {
            byte[] Actual = File.ReadAllBytes(Path.Combine(ReExtracted, FileEntry.Key.Replace('\\', Path.DirectorySeparatorChar)));
            Require(Actual.AsSpan().SequenceEqual(FileEntry.Value), "Round-trip mismatch: " + FileEntry.Key);
        }
        return Expected.Count;
    }

    private static List<BBADataEntry> ReadEntries(string ArchivePath)
    {
        using BinaryReader Reader = new(File.OpenRead(ArchivePath));
        BBAHeader Header = new(Reader.ReadBytes(16));
        byte[] DirectoryHeader = Reader.ReadBytes((int)Header.GetDefinition().HeaderSize);
        Cipher.Decrypt(DirectoryHeader, HeaderKeys, 1231853475, 7);
        uint[] Words = MemoryMarshal.Cast<byte, uint>(DirectoryHeader).ToArray();
        Reader.BaseStream.Position = Words[0];
        byte[] DirectoryData = Reader.ReadBytes((int)Words[2]);
        Cipher.Decrypt(DirectoryData, FileKeys, 1532281822, 8);
        Words = MemoryMarshal.Cast<byte, uint>(DirectoryData).ToArray();
        var Entries = new List<BBADataEntry>();
        int Offset = 276;
        for (int Index = 0; Index < Words[68]; Index++)
        {
            uint NameLength = BitConverter.ToUInt32(DirectoryData, Offset + 48);
            int Size = 64 + ((int)NameLength + 3 & ~3) + (NameLength % 4 == 0 ? 4 : 0);
            Entries.Add(new BBADataEntry(MemoryMarshal.Cast<byte, uint>(DirectoryData.AsSpan(Offset, Size))));
            Offset += Size;
        }
        return Entries;
    }

    private static void WriteFixture(string PathName, byte[] Plaintext, uint Type)
    {
        const string Name = "audio.mp3";
        const uint Delta = 1532281822;
        const int Rounds = 8;
        byte[] Payload = Plaintext.ToArray();
        if (Type == 2)
        {
            // The archive stores encrypted zero padding outside the logical file size.
            Array.Resize(ref Payload, (Payload.Length + 3) & ~3);
            uint[] Keys = FileKeys.Select(Key => Key ^ (uint)Name.Length).ToArray();
            for (int Offset = 0; Offset < Payload.Length; Offset += (int)BlockSize)
                Cipher.Encrypt(Payload.AsSpan(Offset, Math.Min((int)BlockSize, Payload.Length - Offset)), Keys, Delta, Rounds);
        }
        else if (Type == 17)
        {
            using MemoryStream Compressed = new();
            using (GZipStream GZip = new(Compressed, CompressionMode.Compress, true))
                GZip.Write(Plaintext);
            Payload = Compressed.ToArray();
            Cipher.EncryptCompressedFile(Payload, (uint)Name.Length, [68040748, 166934560, 1719695735, 4247314248]);
        }

        BBADataEntry Root = MakeEntry(".");
        Root.GetDefinition().FileType = 256;
        Root.GetDefinition().NextDirectoryOffset = (uint)Root.Serialize().Length;
        BBADataEntry FileEntry = MakeEntry(Name);
        ref BBADataEntryDefinition Definition = ref FileEntry.GetDefinition();
        Definition.FileType = Type;
        Definition.FileOffset = 80;
        Definition.DecompressedFileSize = (uint)Plaintext.Length;
        Definition.DecompressedCrc32 = Crc32.HashToUInt32(Plaintext);
        Definition.CompressedFileSize = Type == 2 ? (uint)Plaintext.Length : (uint)Payload.Length;
        Definition.CompressedCrc32 = Crc32.HashToUInt32(Payload);
        Definition.BlockSize = BlockSize;

        BBADirectory Directory = new(default);
        Directory.GetDefinition().NumberOfFiles = 2;
        Directory.GetDefinition().OffsetFileHashtable = (uint)(276 + Root.Serialize().Length + FileEntry.Serialize().Length);
        using MemoryStream DirectoryStream = new();
        using (BinaryWriter Writer = new(DirectoryStream, Encoding.ASCII, true))
        {
            Writer.Write(Directory.Serialize());
            Writer.Write(Root.Serialize());
            Writer.Write(FileEntry.Serialize());
            Writer.Write(4u);
            Writer.Write(Crc32.HashToUInt32(Encoding.ASCII.GetBytes(".")));
            Writer.Write(0u);
            Writer.Write(Crc32.HashToUInt32(Encoding.ASCII.GetBytes(Name)));
            Writer.Write((uint)Root.Serialize().Length);
            Writer.Write(new byte[16]);
        }
        byte[] DirectoryBytes = DirectoryStream.ToArray();
        Cipher.Encrypt(DirectoryBytes, FileKeys, Delta, Rounds);
        BBADirectoryHeader DirectoryHeader = new([(uint)(80 + Payload.Length), 0, (uint)DirectoryBytes.Length, Crc32.HashToUInt32(DirectoryBytes), 0, 0]);
        byte[] HeaderBytes = DirectoryHeader.Serialize();
        Cipher.Encrypt(HeaderBytes, HeaderKeys, 1231853475, 7);
        using BinaryWriter ArchiveWriter = new(File.Create(PathName));
        ArchiveWriter.Write(new BBAHeader(default).Serialize());
        ArchiveWriter.Write(HeaderBytes);
        ArchiveWriter.Write(Payload);
        ArchiveWriter.Write(DirectoryBytes);
    }

    private static BBADataEntry MakeEntry(string Name)
    {
        byte[] Data = new byte[64 + ((Name.Length + 3) & ~3)];
        Encoding.ASCII.GetBytes(Name).CopyTo(Data, 64);
        BitConverter.GetBytes((uint)Name.Length).CopyTo(Data, 48);
        BBADataEntry Entry = new(MemoryMarshal.Cast<byte, uint>(Data));
        Entry.GetDefinition().NextDirectoryOffset = uint.MaxValue;
        Entry.GetDefinition().NextFileOffset = uint.MaxValue;
        return Entry;
    }

    private static void Require(bool Condition, string Message)
    {
        if (!Condition) throw new InvalidOperationException(Message);
    }
}
