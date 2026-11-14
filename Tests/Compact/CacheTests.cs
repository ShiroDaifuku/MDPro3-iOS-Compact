using System;
using System.IO;
using MDPro3.Compact;

public static class CacheTests
{
    private static int assertions;
    private static void Check(bool condition, string description)
    { if (!condition) throw new Exception(description); assertions++; }
    private static void Put(BoundedResourceCache cache, string key, int count)
    {
        using (var lease = cache.TryBeginWrite(key, count))
        {
            Check(lease != null, "Write reservation accepted");
            File.WriteAllBytes(lease.TemporaryPath, new byte[count]);
            Check(lease.Commit(), "Complete write committed");
        }
    }
    public static void Main(string[] args)
    {
        var root = Path.GetFullPath(args[0]);
        Directory.CreateDirectory(root);
        var first = BoundedResourceCache.CardKey(1, false);
        var second = BoundedResourceCache.CardKey(2, false);
        var third = BoundedResourceCache.CardKey(3, true);
        var cache = new BoundedResourceCache(Path.Combine(root, "eviction"), 12);
        Put(cache, first, 6);
        cache.SetPinned(first, true);
        Put(cache, second, 6);
        Put(cache, third, 6);
        Check(cache.Read(first) != null, "Pinned image survives eviction");
        Check(cache.Read(second) == null, "Unpinned oldest image evicted");
        Check(cache.UsedBytes == 12, "Storage stays within quota");
        cache = new BoundedResourceCache(Path.Combine(root, "eviction"), 12);
        cache.ClearUnpinned();
        Check(cache.UsedBytes == 6 && cache.Read(first) != null, "Pins survive process restart");
        Check(cache.TryBeginWrite(second, 7) == null, "Cannot exceed quota with a pinned image");
        Check(!cache.SetBudget(5), "Pinned overflow is reported without deleting pinned data");
        Check(cache.Read(first) != null, "Shrinking budget preserves pinned data");
        cache.SetPinned(first, false);
        Check(cache.Trim() && cache.UsedBytes == 0, "Unpin allows quota recovery");

        cache = new BoundedResourceCache(Path.Combine(root, "leases"), 10);
        using (var lease = cache.TryBeginWrite(first, 8))
        {
            Check(cache.TryBeginWrite(first, 1) == null, "Duplicate in-flight key rejected");
            Check(cache.TryBeginWrite(second, 3) == null, "In-flight bytes count toward quota");
            File.WriteAllBytes(lease.TemporaryPath, new byte[2]);
        }
        Check(cache.ReservedBytes == 0 && cache.UsedBytes == 0, "Cancelled download releases reservation");
        using (var lease = cache.TryBeginWrite(first, 4))
        {
            File.WriteAllBytes(lease.TemporaryPath, new byte[5]);
            Check(!lease.Commit(), "Oversized completed file rejected");
        }
        Check(cache.Read(first) == null && cache.ReservedBytes == 0, "Invalid write never becomes visible");
        bool traversalRejected = false;
        try { cache.Read("../deck.ydk"); } catch (ArgumentException) { traversalRejected = true; }
        Check(traversalRejected, "Cache keys cannot escape dedicated directory");
        File.WriteAllText(Path.Combine(root, "leases", "user-deck.ydk"), "preserve");
        cache.ClearUnpinned();
        Check(File.Exists(Path.Combine(root, "leases", "user-deck.ydk")), "Cache cleanup does not delete unrelated files");

        var stream = new MemoryStream();
        using (var limited = new LimitedWriteStream(stream, 4))
        {
            limited.Write(new byte[3], 0, 3);
            bool rejected = false;
            try { limited.Write(new byte[2], 0, 2); } catch (IOException) { rejected = true; }
            Check(rejected && limited.Length == 3, "Network stream rejects overflow before writing it");
        }
        Check(CompactStoragePolicy.AllowedImageCache(600000000) == 200000000, "Budget adjusts to existing installed data");
        Check(CompactStoragePolicy.AllowedImageCache(950000000) == 0, "Downloads disabled when other data consumes budget");
        Check(CompactStoragePolicy.MeasureDirectories(Path.Combine(root, "leases"), Path.Combine(root, "leases")) == 8,
            "Storage accounting deduplicates overlapping roots");
        Check(!JpegImageInfo.HasSafeDimensions(new byte[] { 1, 2, 3 }), "Invalid image rejected");
        Check(JpegImageInfo.HasSafeDimensions(File.ReadAllBytes(args[1])), "JPEG fixture accepted");
        var huge = new byte[] { 255,216,255,192,0,8,8,255,255,255,255,1,255,217 };
        Check(!JpegImageInfo.HasSafeDimensions(huge), "Huge decoded dimensions rejected");
        Check(ReplayRandom.First(5489) == 3499211612U, "MT19937 published reference seed matches legacy replay seed");
        Check(ReplayRandom.First(0) == 2357136044U, "MT19937 zero seed reference vector matches");
        string pack = Path.Combine(root, "pre.ypk"), filtered = Path.Combine(root, "filtered.ypk");
        using (var zip = System.IO.Compression.ZipFile.Open(pack, System.IO.Compression.ZipArchiveMode.Create))
        {
            foreach (var name in new[] { "test-release.cdb", "script/c123.lua", "pics/123.jpg" })
                using (var output = zip.CreateEntry(name).Open()) { output.WriteByte(42); }
        }
        Check(PrereleasePackFilter.CreateDataOnly(pack, filtered) == 2, "Prerelease keeps database and Lua without pictures");
        using (var zip = System.IO.Compression.ZipFile.OpenRead(filtered))
            Check(zip.Entries.Count == 2 && zip.GetEntry("pics/123.jpg") == null, "Image-free YPK remains readable");
        File.Delete(pack); File.Delete(filtered);
        using (var zip = System.IO.Compression.ZipFile.Open(pack, System.IO.Compression.ZipArchiveMode.Create))
        { using (var packEntry = zip.CreateEntry("../test-release.cdb").Open()) packEntry.WriteByte(0); }
        bool unsafePack = false;
        try { PrereleasePackFilter.CreateDataOnly(pack, filtered); } catch (InvalidDataException) { unsafePack = true; }
        Check(unsafePack, "Prerelease path traversal rejected before publishing");
        using (var connection = new MDPro3.Compact.Sqlite.SqliteConnection("Data Source=" + args[2]))
        {
            connection.Open();
            using (var command = new MDPro3.Compact.Sqlite.SqliteCommand("SELECT datas.id, texts.name, datas.setcode FROM datas INNER JOIN texts ON datas.id=texts.id WHERE datas.id=89631139", connection))
            using (var reader = command.ExecuteReader())
            {
                Check(reader.Read(), "Callback-free SQLite reads the actual bundled card database");
                Check(reader.GetInt32(0) == 89631139 && reader.GetString(1).Contains("青眼"), "SQLite preserves card IDs and Chinese UTF-8 text");
                Check(!reader.Read() && !reader.Read(), "SQLite end of reader is stable");
            }
            using (var command = new MDPro3.Compact.Sqlite.SqliteCommand("DELETE FROM datas", connection))
            using (var reader = command.ExecuteReader())
            {
                bool denied = false;
                try { reader.Read(); } catch (InvalidDataException) { denied = true; }
                Check(denied, "SQLite opens card databases read-only");
            }
        }
        Console.WriteLine("PASS: " + assertions + " assertions; quota, pinning, restart, cancellation, stream limits, JPEG validation.");
    }
}
