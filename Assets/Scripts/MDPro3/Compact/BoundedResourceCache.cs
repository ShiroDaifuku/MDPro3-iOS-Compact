using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace MDPro3.Compact
{
    // Only owns card_*.jpg files inside its dedicated directory. User decks/imports are never evicted.
    public sealed class BoundedResourceCache
    {
        private readonly object gate = new object();
        private readonly string root;
        private readonly HashSet<string> pinned = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, WriteLease> writes = new Dictionary<string, WriteLease>();
        private long budget;

        public BoundedResourceCache(string directory, long budgetBytes)
        {
            if (budgetBytes < 0) throw new ArgumentOutOfRangeException(nameof(budgetBytes));
            root = Path.GetFullPath(directory);
            budget = budgetBytes;
            Directory.CreateDirectory(root);
            var pins = Path.Combine(root, "pinned.txt");
            if (File.Exists(pins))
                foreach (var key in File.ReadAllLines(pins))
                    if (IsKey(key)) pinned.Add(key);
            // An interrupted process cannot still own a lease in this newly-created service.
            foreach (var path in Directory.GetFiles(root, "card_*.part")) File.Delete(path);
            Trim();
        }

        public static string CardKey(int code, bool prerelease)
        {
            if (code <= 0) throw new ArgumentOutOfRangeException(nameof(code));
            return "card_" + (prerelease ? "pre_" : "ocg_") + code.ToString(CultureInfo.InvariantCulture) + ".jpg";
        }

        private static bool IsKey(string key)
        {
            if (string.IsNullOrEmpty(key) || !key.StartsWith("card_", StringComparison.Ordinal)
                || !key.EndsWith(".jpg", StringComparison.Ordinal)) return false;
            var stem = key.Substring(5, key.Length - 9);
            return stem.Length > 0 && stem.All(c => c >= '0' && c <= '9' || c >= 'a' && c <= 'z' || c == '_');
        }

        private string Resolve(string key)
        {
            if (!IsKey(key)) throw new ArgumentException("Invalid managed cache key.", nameof(key));
            return Path.Combine(root, key);
        }

        public long UsedBytes { get { lock (gate) return Files().Sum(f => f.Length); } }
        public long ReservedBytes { get { lock (gate) return writes.Values.Sum(w => w.MaximumBytes); } }

        private FileInfo[] Files()
        {
            return new DirectoryInfo(root).GetFiles("card_*.jpg")
                .Where(f => IsKey(f.Name) && (f.Attributes & FileAttributes.ReparsePoint) == 0).ToArray();
        }

        public byte[] Read(string key)
        {
            lock (gate)
            {
                var path = Resolve(key);
                if (!File.Exists(path)) return null;
                var bytes = File.ReadAllBytes(path);
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
                return bytes;
            }
        }

        public void Remove(string key)
        {
            lock (gate)
            {
                if (writes.ContainsKey(key)) throw new InvalidOperationException("Resource is being written.");
                File.Delete(Resolve(key));
            }
        }

        public void SetPinned(string key, bool value)
        {
            lock (gate)
            {
                Resolve(key);
                if (value) pinned.Add(key); else pinned.Remove(key);
                File.WriteAllLines(Path.Combine(root, "pinned.txt"), pinned.OrderBy(k => k));
            }
        }

        public bool SetBudget(long bytes)
        {
            if (bytes < 0) throw new ArgumentOutOfRangeException(nameof(bytes));
            lock (gate) { budget = bytes; return Trim(); }
        }

        public void ReplacePins(IEnumerable<string> keys)
        {
            lock (gate)
            {
                var next = new HashSet<string>(keys, StringComparer.Ordinal);
                foreach (var key in next) Resolve(key);
                File.WriteAllLines(Path.Combine(root, "pinned.txt"), next.OrderBy(k => k));
                pinned.Clear(); pinned.UnionWith(next);
            }
        }

        public bool Trim()
        {
            lock (gate) return MakeRoom(0);
        }

        private bool MakeRoom(long additional)
        {
            var files = Files();
            long used = files.Sum(f => f.Length) + writes.Values.Sum(w => w.MaximumBytes);
            if (files.Where(f => pinned.Contains(f.Name)).Sum(f => f.Length)
                + writes.Values.Sum(w => w.MaximumBytes) > budget - additional) return false;
            foreach (var file in files.Where(f => !pinned.Contains(f.Name)).OrderBy(f => f.LastWriteTimeUtc))
            {
                if (used <= budget - additional) break;
                File.Delete(file.FullName);
                used -= file.Length;
            }
            return used <= budget - additional;
        }

        public WriteLease TryBeginWrite(string key, long maximumBytes)
        {
            if (maximumBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
            lock (gate)
            {
                var target = Resolve(key);
                if (maximumBytes > budget || writes.ContainsKey(key) || File.Exists(target)) return null;
                if (!MakeRoom(maximumBytes)) return null;
                var lease = new WriteLease(this, key, target,
                    Path.Combine(root, key + "." + Guid.NewGuid().ToString("N") + ".part"), maximumBytes);
                writes.Add(key, lease);
                return lease;
            }
        }

        public void ClearUnpinned()
        {
            lock (gate)
                foreach (var file in Files().Where(f => !pinned.Contains(f.Name))) File.Delete(file.FullName);
        }

        public sealed class WriteLease : IDisposable
        {
            private readonly BoundedResourceCache owner;
            private readonly string key;
            private readonly string target;
            private bool completed;
            public string TemporaryPath { get; private set; }
            public long MaximumBytes { get; private set; }

            internal WriteLease(BoundedResourceCache owner, string key, string target, string temporary, long maximum)
            { this.owner = owner; this.key = key; this.target = target; TemporaryPath = temporary; MaximumBytes = maximum; }

            // Caller validates the encoded image and successful decode before committing.
            public bool Commit()
            {
                lock (owner.gate)
                {
                    if (completed) return false;
                    var info = new FileInfo(TemporaryPath);
                    if (!info.Exists || info.Length <= 0 || info.Length > MaximumBytes) { Dispose(); return false; }
                    File.Move(TemporaryPath, target);
                    File.SetLastWriteTimeUtc(target, DateTime.UtcNow);
                    owner.writes.Remove(key);
                    completed = true;
                    return true;
                }
            }

            public void Dispose()
            {
                lock (owner.gate)
                {
                    if (completed) return;
                    try { File.Delete(TemporaryPath); }
                    finally { owner.writes.Remove(key); completed = true; }
                }
            }
        }
    }
}
