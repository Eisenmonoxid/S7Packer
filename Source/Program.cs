using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace S7Packer.Source
{
	internal class Program
	{
        static void Main(string[] args)
		{
			Console.ForegroundColor = ConsoleColor.White;
            Console.BackgroundColor = ConsoleColor.Black;
            Console.Title = "S7Packer";
            Console.Clear();

			string Version = Assembly.GetEntryAssembly().GetCustomAttribute<AssemblyFileVersionAttribute>().Version;
            Console.WriteLine("[INFO] S7Packer v" + Version + " - github.com/Eisenmonoxid/S7Packer");
            Console.WriteLine("[INFO] Currently running on " + RuntimeInformation.OSDescription.ToString() + " with runtime: " + RuntimeInformation.FrameworkDescription);

            bool Result = false;
            bool ProductOrDemo = args.Any(Element => Element.Contains("--demo"));
            string Folder = args.FirstOrDefault(Element => !Element.EndsWith(".bba") && Directory.Exists(Element));
            string Filepath = args.FirstOrDefault(Element => Element.EndsWith(".bba"));
            string Level = args.FirstOrDefault(Element => Element.StartsWith("--CompressionLevel:"));
            CompressionLevel? ConvertedLevel = null;

            if (Level != default)
            {
                try
                {
                    ConvertedLevel = (CompressionLevel)Convert.ToUInt32(Level.Replace("--CompressionLevel:", ""));
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[ERROR] Could not parse CompressionLevel! Using: " + ConvertedLevel.ToString() + ". Message: " + ex.ToString());
                }
            }

            if (Folder == default)
            {
                FileStream Stream = GetFileStream(Filepath);
                if (Stream != null)
                {
                    Result = Unpack(Stream, !ProductOrDemo);
                    Stream.Dispose();
                }
            }
            else
            {
                Result = Pack(Folder, Filepath, !ProductOrDemo, ConvertedLevel);
            }

            Console.WriteLine("[INFO] Finished!" + (!Result ? " One or more errors occured." : " No errors occured."));
            Console.WriteLine("[INFO] Exiting ...");

            return;
		}

        static bool Unpack(FileStream Stream,  bool IsProductVersion)
        {
            bool Result = false;
            BBAArchiveFile Archive;
            Stopwatch Watch = new();
            
            try
            {
                Archive = new BBAArchiveFile(Stream, IsProductVersion);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[ERROR] " + ex.Message);
                return Result;
            }

            Console.WriteLine("[INFO] Unpacking archive file " + Stream.Name + ".\n");

            Watch.Start();
            try
            {
                Result = Archive.ExtractAllFilesFromArchive();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
            Watch.Stop();

            Console.WriteLine("\n[INFO] Operation took " + Watch.ElapsedMilliseconds + " ms.");
			return Result;
        }

		static bool Pack(string Folder, string Filepath, bool IsProductVersion, CompressionLevel? Level)
		{
            BBAArchiveFile Archive;
            Stopwatch Watch = new();

            try
            {
                Archive = new(Folder, Filepath, IsProductVersion, Level);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[ERROR] " + ex.Message);
                return false;
            }

            Console.WriteLine("\n[INFO] Packing folder content " + Folder + " to archive file " + Filepath + ".\n");

            Watch.Start();
            Archive.CreateArchiveFileByFolder();
            Watch.Stop();

            Console.WriteLine("\n[INFO] Operation took " + Watch.ElapsedMilliseconds + " ms.");
			return true;
		}

		static FileStream GetFileStream(string Filepath)
        {
            FileStream Stream;
            if (Filepath == default)
            {
                Console.WriteLine("[ERROR] No argument(s) given! Aborting ...");
                return null;
            }

            if (!File.Exists(Filepath))
            {
                Console.WriteLine("[ERROR] File does not exist! Aborting ...");
                return null;
            }

            try
            {
                Stream = new(Filepath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[ERROR] Could not open FileStream! Aborting ...\n" + ex.Message);
                return null;
            }

			return Stream;
        }
	}
}
