using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using MDPro3.Duel.YGOSharp;
using UnityEngine;
using UnityEngine.Networking;

namespace MDPro3.Compact
{
    // Network work never runs under CardImageLoader's single mobile render semaphore.
    public sealed class OnDemandCardImages : MonoBehaviour
    {
        private const string Endpoint = "https://sapi.moecube.com:444/cdntx-endpoint/_endpoint";
        private const string DefaultHost = "https://cdntx.moecube.com";
        private static OnDemandCardImages instance;
        private BoundedResourceCache cache;
        private readonly Queue<Request> queue = new Queue<Request>();
        private readonly HashSet<string> pending = new HashSet<string>();
        private readonly Dictionary<string, DateTime> retryAfter = new Dictionary<string, DateTime>();
        private string host = DefaultHost;
        private int active;
        private float nextStorageCheck;
        public static event Action<int> ImageAvailable;
        public static string LastStorageStatus { get; private set; }

        private struct Request { public int Code; public bool Prerelease; public string Key; }
        [Serializable] private sealed class Hosts { public string[] hosts; }

        public static bool Enabled
        {
            get
            {
#if MDPRO3_COMPACT || (UNITY_IOS && !UNITY_EDITOR)
                return true;
#else
                return Config.GetBool("OnDemandCardImages", false);
#endif
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { instance = null; ImageAvailable = null; LastStorageStatus = null; }

        public static void Initialize()
        {
            if (!Enabled || instance != null) return;
            var go = new GameObject("OnDemandCardImages");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<OnDemandCardImages>();
            try
            {
                instance.cache = new BoundedResourceCache(
                    Path.Combine(Application.persistentDataPath, "ResourceCache", "CardImages"), CompactStoragePolicy.ImageCacheLimit);
                instance.CheckStorage();
                instance.StartCoroutine(instance.ResolveHost());
            }
            catch (Exception error)
            {
                Debug.LogWarning("Card image cache unavailable: " + error.Message);
                Destroy(go); instance = null;
            }
        }

        public static byte[] ReadCached(int code, bool prerelease)
        {
            if (instance == null || instance.cache == null || code <= 0) return null;
            var key = BoundedResourceCache.CardKey(code, prerelease);
            try
            {
                var bytes = instance.cache.Read(key);
                if (bytes != null && !JpegImageInfo.HasSafeDimensions(bytes)) { instance.cache.Remove(key); return null; }
                return bytes;
            }
            catch (Exception error) { Debug.LogWarning("Card image cache read failed: " + error.Message); return null; }
        }

        public static void RequestImage(int code, bool prerelease)
        {
            if (!Enabled || instance == null || instance.cache == null || code <= 0) return;
            var key = BoundedResourceCache.CardKey(code, prerelease);
            if (instance.pending.Contains(key) || instance.queue.Count >= 512) return;
            DateTime retry;
            if (instance.retryAfter.TryGetValue(key, out retry) && retry > DateTime.UtcNow) return;
            instance.pending.Add(key);
            instance.queue.Enqueue(new Request { Code = code, Prerelease = prerelease, Key = key });
        }

        // Includes Side. Pinning is a storage promise, not a promise that download already succeeded.
        public static void PrepareOfflineDeck(Deck deck)
        {
            if (instance == null || deck == null) return;
            var codes = new HashSet<int>(deck.Main);
            codes.UnionWith(deck.Extra); codes.UnionWith(deck.Side);
            var pins = new List<string>();
            foreach (var code in codes)
                if (code > 0) pins.Add(BoundedResourceCache.CardKey(code, CardsManager.Get(code).isPre));
            try { instance.cache.ReplacePins(pins); }
            catch (Exception error) { Debug.LogWarning("Cannot save offline deck images: " + error.Message); }
            foreach (var code in codes)
            {
                if (code <= 0) continue;
                bool pre = CardsManager.Get(code).isPre;
                if (ReadCached(code, pre) == null) RequestImage(code, pre);
            }
        }

        public static void ClearUnpinned()
        {
            if (instance == null || instance.cache == null) return;
            instance.cache.ClearUnpinned();
        }

        public static bool PrepareStorageWrite(long additionalBytes)
        {
            if (additionalBytes < 0) throw new ArgumentOutOfRangeException(nameof(additionalBytes));
            try
            {
                if (instance != null) instance.CheckStorage();
                string app = Application.dataPath;
#if UNITY_IOS && !UNITY_EDITOR
                app = Directory.GetParent(app).FullName;
#endif
                long total = CompactStoragePolicy.MeasureDirectories(app, Application.persistentDataPath, Application.temporaryCachePath);
                return additionalBytes <= CompactStoragePolicy.OperatingTarget - total;
            }
            catch (Exception error) { Debug.LogWarning(error.Message); return false; }
        }

        private void Update()
        {
            if (cache == null) return;
            if (Time.unscaledTime >= nextStorageCheck)
            {
                nextStorageCheck = Time.unscaledTime + 60;
                CheckStorage();
            }
            while (active < 3 && queue.Count > 0) StartCoroutine(Download(queue.Dequeue()));
        }

        private void CheckStorage()
        {
            try
            {
                string appRoot = Application.dataPath;
#if UNITY_IOS && !UNITY_EDITOR
                appRoot = Directory.GetParent(Application.dataPath).FullName;
#endif
                long total = CompactStoragePolicy.MeasureDirectories(appRoot, Application.persistentDataPath, Application.temporaryCachePath);
                long other = Math.Max(0, total - cache.UsedBytes);
                bool fits = cache.SetBudget(CompactStoragePolicy.AllowedImageCache(other));
                LastStorageStatus = "Measured " + total + " bytes; image cache " + cache.UsedBytes + "; pinned resources fit: " + fits;
                if (!fits || total > CompactStoragePolicy.OperatingTarget) Debug.LogWarning(LastStorageStatus);
            }
            catch (Exception error)
            {
                // Fail closed for optional downloads if accounting cannot establish free budget.
                cache.SetBudget(0);
                LastStorageStatus = "Storage accounting failed: " + error.Message;
                Debug.LogWarning(LastStorageStatus);
            }
        }

        private IEnumerator ResolveHost()
        {
            using (var request = UnityWebRequest.Get(Endpoint))
            {
                request.timeout = 8;
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success) yield break;
                SelectHost(request.downloadHandler.text);
            }
        }

        private void SelectHost(string json)
        {
            try
            {
                var parsed = JsonUtility.FromJson<Hosts>("{\"hosts\":" + json + "}");
                if (parsed == null || parsed.hosts == null) return;
                foreach (var candidate in parsed.hosts)
                {
                    Uri uri;
                    if (Uri.TryCreate("https://" + candidate, UriKind.Absolute, out uri)
                        && uri.AbsolutePath == "/" && uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0)
                    { host = uri.GetLeftPart(UriPartial.Authority); return; }
                }
            }
            catch (Exception error) { Debug.LogWarning("Using default card CDN: " + error.Message); }
        }

        private IEnumerator Download(Request image)
        {
            active++;
            BoundedResourceCache.WriteLease lease = null;
            try
            {
                lease = cache.TryBeginWrite(image.Key, CompactStoragePolicy.MaximumImageBytes);
                if (lease == null) { retryAfter[image.Key] = DateTime.UtcNow.AddSeconds(60); yield break; }
                string path = image.Prerelease ? "/ygopro-super-pre/data/pics/" : "/images/ygopro-images-zh-CN/";
                using (var handler = new ImageFileHandler(lease.TemporaryPath, lease.MaximumBytes))
                using (var request = new UnityWebRequest(host + path + image.Code + ".jpg", UnityWebRequest.kHttpVerbGET))
                {
                    request.downloadHandler = handler;
                    request.disposeDownloadHandlerOnDispose = false;
                    request.timeout = 15;
                    yield return request.SendWebRequest();
                    handler.Finish();
                    if (request.result == UnityWebRequest.Result.Success && ValidateAndCommit(lease))
                    {
                        retryAfter.Remove(image.Key);
                        ImageAvailable?.Invoke(image.Code);
                    }
                    else retryAfter[image.Key] = DateTime.UtcNow.AddSeconds(request.responseCode == 404 ? 300 : 30);
                }
            }
            finally { lease?.Dispose(); pending.Remove(image.Key); active--; }
        }

        private static bool ValidateAndCommit(BoundedResourceCache.WriteLease lease)
        {
            Texture2D texture = null;
            try
            {
                var bytes = File.ReadAllBytes(lease.TemporaryPath);
                if (!JpegImageInfo.HasSafeDimensions(bytes)) return false;
                texture = new Texture2D(2, 2);
                return texture.LoadImage(bytes) && lease.Commit();
            }
            catch (Exception error) { Debug.LogWarning("Invalid card download: " + error.Message); return false; }
            finally { if (texture != null) Destroy(texture); }
        }

        private void OnDestroy()
        {
            StopAllCoroutines();
            if (instance == this) instance = null;
        }

        private sealed class ImageFileHandler : DownloadHandlerScript
        {
            private LimitedWriteStream output;
            public ImageFileHandler(string path, long maximum) : base(new byte[32768])
            { output = new LimitedWriteStream(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None), maximum); }
            protected override bool ReceiveData(byte[] data, int length)
            {
                try { if (output == null) return false; output.Write(data, 0, length); return true; }
                catch (Exception) { return false; }
            }
            public void Finish() { output?.Dispose(); output = null; }
            protected override void CompleteContent() { Finish(); }
            public new void Dispose() { Finish(); base.Dispose(); }
        }
    }
}
