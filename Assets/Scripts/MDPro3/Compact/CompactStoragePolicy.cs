using System;
using System.Collections.Generic;
using System.IO;

namespace MDPro3.Compact
{
    public static class CompactStoragePolicy
    {
        // Decimal bytes match iOS Settings. A 100 MB margin remains below the requested 1 GB.
        public const long TotalLimit = 1000000000;
        public const long OperatingTarget = 900000000;
        public const long ImageCacheLimit = 300000000;
        public const long UserDataBudget = 50000000;
        public const long UpdateReserve = 100000000;
        public const long MaximumImageBytes = 2000000;

        public static long MeasureDirectories(params string[] directories)
        {
            var seen = new HashSet<string>(Path.DirectorySeparatorChar == '\\' ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            long result = 0;
            foreach (var directory in directories)
            {
                if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) continue;
                var pending = new Stack<string>();
                pending.Push(Path.GetFullPath(directory));
                while (pending.Count > 0)
                {
                    var path = pending.Pop();
                    if (!seen.Add(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) continue;
                    foreach (var file in Directory.GetFiles(path))
                    {
                        if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0 || !seen.Add(file)) continue;
                        result = checked(result + new FileInfo(file).Length);
                    }
                    foreach (var child in Directory.GetDirectories(path)) pending.Push(child);
                }
            }
            return result;
        }

        public static long AllowedImageCache(long installedAndOtherBytes)
        {
            if (installedAndOtherBytes < 0) throw new ArgumentOutOfRangeException(nameof(installedAndOtherBytes));
            return Math.Max(0, Math.Min(ImageCacheLimit, OperatingTarget - UpdateReserve - installedAndOtherBytes));
        }
    }
}
