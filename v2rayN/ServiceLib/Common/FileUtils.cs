using System.IO.Compression;

namespace ServiceLib.Common;

public static class FileUtils
{
    private const int TarBlockSize = 512;
    private static readonly string _tag = "FileManager";

    public static bool ByteArrayToFile(string fileName, byte[] content)
    {
        try
        {
            File.WriteAllBytes(fileName, content);
            return true;
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
        }
        return false;
    }

    public static void DecompressFile(string fileName, byte[] content)
    {
        try
        {
            using var fs = File.Create(fileName);
            using GZipStream input = new(new MemoryStream(content), CompressionMode.Decompress, false);
            input.CopyTo(fs);
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
        }
    }

    public static void DecompressFile(string fileName, string toPath, string? toName)
    {
        try
        {
            FileInfo fileInfo = new(fileName);
            using var originalFileStream = fileInfo.OpenRead();
            using var decompressedFileStream = File.Create(toName != null ? Path.Combine(toPath, toName) : toPath);
            using GZipStream decompressionStream = new(originalFileStream, CompressionMode.Decompress);
            decompressionStream.CopyTo(decompressedFileStream);
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
        }
    }

    public static void DecompressTarFile(string fileName, string toPath)
    {
        try
        {
            using var fs = new FileStream(fileName, FileMode.Open, FileAccess.Read);
            using var gz = new GZipStream(fs, CompressionMode.Decompress, leaveOpen: true);
            ExtractTarArchive(gz, toPath);
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
        }
    }

    public static string NonExclusiveReadAllText(string path)
    {
        return NonExclusiveReadAllText(path, Encoding.Default);
    }

    private static string NonExclusiveReadAllText(string path, Encoding encoding)
    {
        try
        {
            using FileStream fs = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using StreamReader sr = new(fs, encoding);
            return sr.ReadToEnd();
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
            throw;
        }
    }

    public static bool ZipExtractToFile(string fileName, string toPath, string ignoredName)
    {
        try
        {
            using var archive = ZipFile.OpenRead(fileName);
            foreach (var entry in archive.Entries)
            {
                if (entry.Length == 0)
                {
                    continue;
                }
                try
                {
                    if (ignoredName.IsNotEmpty() && entry.Name.Contains(ignoredName))
                    {
                        continue;
                    }
                    entry.ExtractToFile(Path.Combine(toPath, entry.Name), true);
                }
                catch (IOException ex)
                {
                    Logging.SaveLog(_tag, ex);
                }
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
            return false;
        }
        return true;
    }

    public static List<string>? GetFilesFromZip(string fileName)
    {
        if (!File.Exists(fileName))
        {
            return null;
        }
        try
        {
            using var archive = ZipFile.OpenRead(fileName);
            return archive.Entries.Select(entry => entry.FullName).ToList();
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
            return null;
        }
    }

    public static bool CreateFromDirectory(string sourceDirectoryName, string destinationArchiveFileName)
    {
        try
        {
            if (File.Exists(destinationArchiveFileName))
            {
                File.Delete(destinationArchiveFileName);
            }

            ZipFile.CreateFromDirectory(sourceDirectoryName, destinationArchiveFileName, CompressionLevel.SmallestSize, true);
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
            return false;
        }
        return true;
    }

    public static void CopyDirectory(string sourceDir, string destinationDir, bool recursive, bool overwrite, string? ignoredName = null)
    {
        // Get information about the source directory
        var dir = new DirectoryInfo(sourceDir);

        // Check if the source directory exists
        if (!dir.Exists)
        {
            throw new DirectoryNotFoundException($"Source directory not found: {dir.FullName}");
        }

        // Cache directories before we start copying
        var dirs = dir.GetDirectories();

        // Create the destination directory
        _ = Directory.CreateDirectory(destinationDir);

        // Get the files in the source directory and copy to the destination directory
        foreach (var file in dir.GetFiles())
        {
            if (ignoredName.IsNotEmpty() && file.Name.Contains(ignoredName))
            {
                continue;
            }
            if (file.Extension == file.Name)
            {
                continue;
            }
            var targetFilePath = Path.Combine(destinationDir, file.Name);
            if (!overwrite && File.Exists(targetFilePath))
            {
                continue;
            }
            _ = file.CopyTo(targetFilePath, overwrite);
        }

        // If recursive and copying subdirectories, recursively call this method
        if (recursive)
        {
            foreach (var subDir in dirs)
            {
                var newDestinationDir = Path.Combine(destinationDir, subDir.Name);
                CopyDirectory(subDir.FullName, newDestinationDir, true, overwrite, ignoredName);
            }
        }
    }

    public static void DeleteExpiredFiles(string sourceDir, DateTime dtLine)
    {
        try
        {
            var files = Directory.GetFiles(sourceDir, "*.*");
            foreach (var filePath in files)
            {
                var file = new FileInfo(filePath);
                if (file.CreationTime >= dtLine)
                {
                    continue;
                }
                file.Delete();
            }
        }
        catch
        {
            // ignored
        }
    }

    /// <summary>
    /// Creates a Linux shell file with the specified contents.
    /// </summary>
    /// <param name="fileName"></param>
    /// <param name="contents"></param>
    /// <param name="overwrite"></param>
    /// <returns></returns>
    public static async Task<string> CreateLinuxShellFile(string fileName, string contents, bool overwrite)
    {
        var shFilePath = Utils.GetBinConfigPath(fileName);

        // Check if the file already exists and if we should overwrite it
        if (!overwrite && File.Exists(shFilePath))
        {
            return shFilePath;
        }

        File.Delete(shFilePath);
        await File.WriteAllTextAsync(shFilePath, contents);
        await Utils.SetLinuxChmod(shFilePath);

        return shFilePath;
    }

    // Parse the tar stream directly so the net6 build does not depend on System.Formats.Tar.
    private static void ExtractTarArchive(Stream tarStream, string destinationDirectory)
    {
        var destinationRoot = Path.GetFullPath(destinationDirectory);
        _ = Directory.CreateDirectory(destinationRoot);

        var header = new byte[TarBlockSize];
        while (true)
        {
            var bytesRead = ReadBlock(tarStream, header);
            if (bytesRead == 0)
            {
                break;
            }
            if (bytesRead != TarBlockSize)
            {
                throw new InvalidDataException("Unexpected end of tar archive.");
            }
            if (header.All(static b => b == 0))
            {
                break;
            }

            var entryName = GetTarEntryName(header);
            var size = ParseTarOctal(header, 124, 12);
            var typeFlag = header[156];
            var isDirectory = typeFlag == (byte)'5' || entryName.EndsWith("/", StringComparison.Ordinal);
            var dataHandled = false;

            if (entryName.IsNotEmpty())
            {
                var entryPath = GetTarEntryPath(destinationRoot, entryName);
                if (isDirectory)
                {
                    _ = Directory.CreateDirectory(entryPath);
                }
                else if (typeFlag is 0 or (byte)'0')
                {
                    var parentDirectory = Path.GetDirectoryName(entryPath);
                    if (parentDirectory.IsNotEmpty())
                    {
                        _ = Directory.CreateDirectory(parentDirectory);
                    }

                    using var output = new FileStream(entryPath, FileMode.Create, FileAccess.Write);
                    CopyBytes(tarStream, output, size);
                    dataHandled = true;
                }
            }

            if (!dataHandled && size > 0)
            {
                SkipBytes(tarStream, size);
            }

            var padding = GetTarPadding(size);
            if (padding > 0)
            {
                SkipBytes(tarStream, padding);
            }
        }
    }

    private static string GetTarEntryName(byte[] header)
    {
        var name = GetTarString(header, 0, 100);
        var prefix = GetTarString(header, 345, 155);
        return prefix.IsNotEmpty() ? $"{prefix}/{name}" : name;
    }

    private static string GetTarString(byte[] buffer, int offset, int length)
    {
        return Encoding.UTF8.GetString(buffer, offset, length).TrimEnd('\0', ' ');
    }

    private static long ParseTarOctal(byte[] buffer, int offset, int length)
    {
        var value = GetTarString(buffer, offset, length).Trim();
        return value.IsNullOrEmpty() ? 0 : Convert.ToInt64(value, 8);
    }

    private static string GetTarEntryPath(string destinationRoot, string entryName)
    {
        var relativePath = entryName.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(destinationRoot, relativePath));
        var normalizedRoot = destinationRoot.EndsWith(Path.DirectorySeparatorChar)
            ? destinationRoot
            : destinationRoot + Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(fullPath, destinationRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Tar entry escapes destination directory: {entryName}");
        }

        return fullPath;
    }

    private static int ReadBlock(Stream stream, byte[] buffer)
    {
        var totalRead = 0;
        while (totalRead < buffer.Length)
        {
            var read = stream.Read(buffer, totalRead, buffer.Length - totalRead);
            if (read == 0)
            {
                break;
            }
            totalRead += read;
        }

        return totalRead;
    }

    private static void CopyBytes(Stream input, Stream output, long bytesToCopy)
    {
        var buffer = new byte[81920];
        var remaining = bytesToCopy;
        while (remaining > 0)
        {
            var read = input.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
            if (read == 0)
            {
                throw new EndOfStreamException("Unexpected end of tar entry data.");
            }

            output.Write(buffer, 0, read);
            remaining -= read;
        }
    }

    private static void SkipBytes(Stream input, long bytesToSkip)
    {
        var buffer = new byte[81920];
        var remaining = bytesToSkip;
        while (remaining > 0)
        {
            var read = input.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
            if (read == 0)
            {
                throw new EndOfStreamException("Unexpected end of tar archive.");
            }

            remaining -= read;
        }
    }

    private static long GetTarPadding(long size)
    {
        var remainder = size % TarBlockSize;
        return remainder == 0 ? 0 : TarBlockSize - remainder;
    }
}
