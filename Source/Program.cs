using System;
using System.Diagnostics;
using System.IO;
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

            FileStream Stream = GetFileStream(args);
            if (Stream == null)
            {
                try
                {
                    Console.ReadKey();
                }
                catch
                {
                    // Ignore
                }

                return;
            }

            string FolderPath = args.FirstOrDefault(Element => !Element.EndsWith(".bba") && Directory.Exists(Element));
			bool Result = HandleFile(Stream, FolderPath);
			Stream.Dispose();

            Console.WriteLine("\n[INFO] Finished!" + (!Result ? " One or more errors occured." : " No errors occured."));
            Console.WriteLine("[INFO] Press any key to exit ...");
            
            try
            {
                Console.ReadKey();
            }
            catch
            {
                // Ignore
            }
            
            return;
		}

		static bool HandleFile(FileStream Stream, string Folder)
		{
            bool Result = false;
            BBAArchiveFile Archive;
            Stopwatch Watch = new();
            
            try
            {
                Archive = new BBAArchiveFile(Stream);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[ERROR] " + ex.Message);
                return Result;
            }

            Watch.Start();
            if (Folder == default)
            {
                Console.WriteLine("[INFO] Unpacking archive file " + Stream.Name);
                try
                {
                    Result = Archive.ExtractAllFilesFromArchive();
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                }
            }
            else
            {
                Console.WriteLine("\n[INFO] Packing folder content " + Folder + " to archive file " + Stream.Name);
                try
                {
                    Result = Archive.CreateArchiveFileByFolder();
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                }
            }
            Watch.Stop();

            Console.WriteLine("[INFO] Operation took " + Watch.ElapsedMilliseconds + " ms.");
			return Result;
		}

		static FileStream GetFileStream(string[] args)
        {
            FileStream Stream;
            string Filepath = args.FirstOrDefault(Element => Element.EndsWith(".bba"));
            if (Filepath == default)
            {
                Console.WriteLine("[ERROR] No argument(s) given! Aborting ...");
                return null;
            }

            if (File.Exists(Filepath) == false)
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
