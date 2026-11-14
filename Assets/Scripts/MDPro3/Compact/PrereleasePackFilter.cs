using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace MDPro3.Compact
{
    public static class PrereleasePackFilter
    {
        public const long DownloadLimit = 45000000;
        public const long UncompressedLimit = 20000000;
        // Keep the YPK format and filename so existing card/script precedence still applies.
        public static long CreateDataOnly(string source, string destination)
        {
            long total = 0;
            bool database = false;
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var input = ZipFile.OpenRead(source))
            using (var output = ZipFile.Open(destination, ZipArchiveMode.Create))
            {
                if (input.Entries.Count > 20000) throw new InvalidDataException("Too many prerelease entries.");
                foreach (var entry in input.Entries)
                {
                    string path = entry.FullName.Replace('\\', '/');
                    var extension = Path.GetExtension(path).ToLowerInvariant();
                    if (extension != ".cdb" && extension != ".lua" && extension != ".conf" && extension != ".txt") continue;
                    if (path.StartsWith("/") || path.IndexOf(':') >= 0 || Array.Exists(path.Split('/'), p => p == ".." || p == ".")
                        || !names.Add(path)) throw new InvalidDataException("Unsafe or duplicated prerelease entry.");
                    if (entry.Length < 0 || entry.Length > 2000000 || entry.Length > UncompressedLimit - total)
                        throw new InvalidDataException("Prerelease data exceeds its storage budget.");
                    var target = output.CreateEntry(path, CompressionLevel.Optimal);
                    using (var src = entry.Open())
                    using (var dst = target.Open())
                    {
                        var buffer = new byte[32768];
                        long copied = 0;
                        int count;
                        while ((count = src.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            copied += count;
                            if (copied > entry.Length) throw new InvalidDataException("Invalid prerelease entry size.");
                            dst.Write(buffer, 0, count);
                        }
                        if (copied != entry.Length) throw new InvalidDataException("Truncated prerelease entry.");
                    }
                    total += entry.Length;
                    database |= Path.GetFileName(path).StartsWith("test-release", StringComparison.OrdinalIgnoreCase) && extension == ".cdb";
                }
                if (!database) throw new InvalidDataException("Prerelease database is missing.");
            }
            return total;
        }
    }
}
