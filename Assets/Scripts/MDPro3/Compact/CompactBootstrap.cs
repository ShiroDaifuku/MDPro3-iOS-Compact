using System;
using System.Collections;
using System.IO;
using System.Security.Cryptography;
using UnityEngine;

namespace MDPro3.Compact
{
    public static class CompactBootstrap
    {
        public static bool Enabled
        {
            get
            {
#if MDPRO3_COMPACT && UNITY_IOS && !UNITY_EDITOR
                return true;
#else
                return false;
#endif
            }
        }
        [Serializable] private sealed class Manifest { public string version; public Entry[] files; }
        [Serializable] private sealed class Entry { public string path; public string sha256; public long bytes; }

        public static IEnumerator InstallData()
        {
            NativeCompatibility.Validate();
            string source = Path.Combine(Application.streamingAssetsPath, "CompactData");
            string manifestPath = Path.Combine(source, "manifest.json");
            if (!File.Exists(manifestPath)) throw new FileNotFoundException("Compact resource manifest was not built.", manifestPath);
            var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(manifestPath));
            if (manifest == null || manifest.files == null) throw new InvalidDataException("Invalid compact manifest.");
            string stamp = Path.Combine(Application.persistentDataPath, ".compact-data-version");
            bool needsUpdate = !File.Exists(stamp) || File.ReadAllText(stamp) != manifest.version;
            foreach (var entry in manifest.files)
            {
                string target = SafePath(Application.persistentDataPath, entry.path);
                if (!needsUpdate && File.Exists(target)) continue;
                string input = SafePath(source, entry.path);
                if (new FileInfo(input).Length != entry.bytes || Digest(input) != entry.sha256)
                    throw new InvalidDataException("Packaged resource failed verification: " + entry.path);
                if (entry.bytes > CompactStoragePolicy.UpdateReserve)
                    throw new InvalidDataException("Packaged file exceeds update reserve: " + entry.path);
                long total = CompactStoragePolicy.MeasureDirectories(Directory.GetParent(Application.dataPath).FullName,
                    Application.persistentDataPath, Application.temporaryCachePath);
                if (total + entry.bytes > CompactStoragePolicy.OperatingTarget)
                    throw new IOException("Insufficient compact storage budget; free downloaded resources before retrying.");
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                string temporary = target + ".compact-tmp";
                File.Copy(input, temporary, true);
                if (File.Exists(target)) File.Replace(temporary, target, null);
                else File.Move(temporary, target);
                yield return null;
            }
            foreach (var directory in new[] { "Data", "Deck", "Replay", "Expansions", "Puzzle", "Picture/DIY", "TempFolder" })
                Directory.CreateDirectory(Path.Combine(Application.persistentDataPath, directory));
            File.WriteAllText(stamp, manifest.version);
            Config.Initialize(Program.PATH_CONFIG);
            if (!Config.GetBool("CompactProfileApplied", false))
            {
                Config.Set("Language", "zh-CN"); Config.Set("CardLanguage", "zh-CN"); Config.Set("PrereleaseLanguage", "zh-CN");
                foreach (var key in new[] { "VideoCard", "DuelCloseup", "WatchCloseup", "ReplayCloseup", "DuelCutin", "WatchCutin", "ReplayCutin",
                    "DuelEffect", "WatchEffect", "ReplayEffect", "DuelSummon", "WatchSummon", "ReplaySummon" }) Config.Set(key, "0");
                Config.Set("OnDemandCardImages", "1"); Config.Set("CompactProfileApplied", "1"); Config.Save();
            }
        }

        private static string SafePath(string root, string relative)
        {
            string prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string result = Path.GetFullPath(Path.Combine(prefix, relative));
            if (Path.IsPathRooted(relative) || !result.StartsWith(prefix, StringComparison.Ordinal))
                throw new InvalidDataException("Unsafe packaged path.");
            return result;
        }
        private static string Digest(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
    }
}
