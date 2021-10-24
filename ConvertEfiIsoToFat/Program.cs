using DiscUtils;
using DiscUtils.Vfs;
using Microsoft.Wim;
using System;
using System.Collections.Generic;
using System.IO;

static class Program
{
    static void PrintUsage()
    {
        string name = typeof(Program).Assembly.ManifestModule.Name;
        const string DLL_SUFFIX = ".dll";
        if (name.EndsWith(DLL_SUFFIX, StringComparison.OrdinalIgnoreCase))
            name = name.Substring(0, name.Length - DLL_SUFFIX.Length);
        Console.Error.WriteLine($"{name}: extracts a EFI-bootable ISO image to a directory.");
        Console.Error.WriteLine("\tSome conversions are performed so the files fit on a FAT32 filesystem");
        Console.Error.WriteLine();
        Console.Error.WriteLine("USAGE:");
        Console.Error.WriteLine($"\t{name} ISO_PATH EXTRACT_PATH");
        Console.Error.WriteLine();
        Console.Error.WriteLine("ARGS:");
        Console.Error.WriteLine("\t<ISO_PATH>: The path to an existing .iso file.");
        Console.Error.WriteLine("\t<EXTRACT_PATH>: The directory to extract files to. Must exist.");
    }

    static int Main(string[] args)
    {
        try
        {
            if (args.Length != 2)
            {
                Console.Error.WriteLine("");
                PrintUsage();
                return 1;
            }

            string inputPath = args[0];
            string outputPath = args[1];

            if (!File.Exists(inputPath))
            {
                Console.Error.WriteLine($"Input file '{inputPath}' does not exist.");
                return 1;
            }
            if (!Directory.Exists(outputPath))
            {
                Console.Error.WriteLine($"Output directory '{outputPath}' does not exist.");
                return 1;
            }

            RealMain(inputPath, outputPath);

            Console.WriteLine("SUCCESS");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("CRASH");
            Console.Error.WriteLine();
            Console.Error.WriteLine(ex);
            return 2;
        }
    }

    //TODO: add nicer error messages and error codes instead of crashing
    static void RealMain(string inputPath, string outputPath)
    {
        using var inputFs = File.OpenRead(inputPath);

        using var disc = LoadDiscImage(inputFs);
        inputFs.Seek(0, SeekOrigin.Begin);

        if (!disc.DirectoryExists("EFI\\BOOT"))
        {
            throw new Exception("No EFI\\boot directory exists; it is unlikely that this tool will create a bootable image.");
        }

        CheckFileSizes(disc.Root);

        CopyFiles(disc.Root, outputPath);
    }

    static VfsFileSystemFacade LoadDiscImage(FileStream fs)
    {
        if (DiscUtils.Udf.UdfReader.Detect(fs))
            return new DiscUtils.Udf.UdfReader(fs);
        fs.Seek(0, SeekOrigin.Begin);
        if (DiscUtils.Iso9660.CDReader.Detect(fs))
        {
            // TODO: figure out why foliet:true mangles Ubuntu names
            // When joliet:true, extracting ubuntu-20.04.3-desktop-amd64.iso results in file names being truncated,
            // particularlly in /pool/restricted/l/linux-restricted-modules-hwe-5.11
            return new DiscUtils.Iso9660.CDReader(fs, joliet: false, hideVersions: true);
        }
        throw new Exception("Not an recognized disc image type.");
    }

    static void CheckFileSizes(DiscDirectoryInfo dir)
    {
        const long MAX_FAT32_FILE_SIZE = 4294967295;

        foreach (var f in dir.GetFiles())
        {
            if (f.Length > MAX_FAT32_FILE_SIZE)
            {
                if (!sBigFileHandlers.ContainsKey(f.FullName))
                    throw new Exception("File to big to copy to a FAT32 partition: " + f.FullName);
            }
        }

        foreach (var d in dir.GetDirectories())
        {
            CheckFileSizes(d);
        }
    }

    static void CopyFiles(DiscDirectoryInfo source, string destFolderPath)
    {
        foreach (var f in source.GetFiles())
        {
            if (sBigFileHandlers.TryGetValue(f.FullName, out Action<DiscFileInfo, string>? del))
            {
                del(f, destFolderPath);
            }
            else
            {
                if ((f.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    // TODO: support symblic links somehow? Or throw an error?
                    // Ubuntu's iso has a couple symbolic links.
                    continue;
                }

                using var sourceFile = f.OpenRead();
                using var destFile = new FileStream(Path.Combine(destFolderPath, f.Name), new FileStreamOptions()
                {
                    Mode = FileMode.Create,
                    Access = FileAccess.Write,
                    PreallocationSize = f.Length,
                });

                sourceFile.CopyTo(destFile);
            }
        }

        foreach (var d in source.GetDirectories())
        {
            string newFolderPath = Path.Combine(destFolderPath, d.Name);
            Directory.CreateDirectory(newFolderPath);
            CopyFiles(d, newFolderPath);
        }
    }

    static readonly Dictionary<string, Action<DiscFileInfo, string>> sBigFileHandlers = new()
    {
        { "sources\\install.wim", HandleWimFile },
    };

    static void HandleWimFile(DiscFileInfo sourceFile, string destDir)
    {
        string tempFolder = Path.GetTempPath();

        string tempFilePath = Path.Combine(tempFolder, Guid.NewGuid().ToString() + ".wim");

        try
        {
            using (var tempWimFile = new FileStream(tempFilePath, new FileStreamOptions()
            {
                Access = FileAccess.Write,
                Mode = FileMode.CreateNew,
                PreallocationSize = sourceFile.Length,
            }))
            using (var sourceFs = sourceFile.OpenRead())
            {
                sourceFs.CopyTo(tempWimFile);
                tempWimFile.Flush();
            }

            using (var wimHandle = WimgApi.CreateFile(
                tempFilePath,
                WimFileAccess.Read,
                WimCreationDisposition.OpenExisting,
                WimCreateFileOptions.None,
                WimCompressionType.None))
            {
                WimgApi.SetTemporaryPath(wimHandle, tempFolder);

                string destPath = Path.Combine(destDir, Path.ChangeExtension(sourceFile.Name, "swm"));
                WimgApi.SplitFile(wimHandle, destPath, 1 << 30);
            }
        }
        finally
        {
            File.Delete(tempFilePath);
        }
    }
}
