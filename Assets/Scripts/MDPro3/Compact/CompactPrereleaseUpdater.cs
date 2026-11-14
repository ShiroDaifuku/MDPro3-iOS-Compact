using System;
using System.Collections;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace MDPro3.Compact
{
    public static class CompactPrereleaseUpdater
    {
        public static IEnumerator Update(Action<string> status)
        {
            string raw = Path.Combine(Application.temporaryCachePath, "compact-prerelease-download.part");
            string filtered = Path.Combine(Program.PATH_EXPANSIONS, "compact-prerelease-filter.part");
            string target = Path.Combine(Program.PATH_EXPANSIONS, "ygopro-super-pre.ypk");
            try
            {
                Directory.CreateDirectory(Program.PATH_EXPANSIONS);
                if (File.Exists(raw)) File.Delete(raw);
                if (File.Exists(filtered)) File.Delete(filtered);
                if (!OnDemandCardImages.PrepareStorageWrite(PrereleasePackFilter.DownloadLimit + PrereleasePackFilter.UncompressedLimit))
                { status("空间不足，请清理资源后重试。"); yield break; }
                status("正在下载先行卡数据，完成后会移除包内卡图。");
                using (var handler = new BoundedDownloadHandler(raw, PrereleasePackFilter.DownloadLimit))
                using (var request = new UnityWebRequest(Settings.GetPrereleasePackUrl(), UnityWebRequest.kHttpVerbGET))
                {
                    request.downloadHandler = handler;
                    request.disposeDownloadHandlerOnDispose = false;
                    request.timeout = 120;
                    yield return request.SendWebRequest();
                    handler.Finish();
                    if (request.result != UnityWebRequest.Result.Success)
                    { status("先行卡下载失败或超过精简版更新配额，已有卡包已保留。"); yield break; }
                }
                var filtering = Task.Run(() => PrereleasePackFilter.CreateDataOnly(raw, filtered));
                while (!filtering.IsCompleted) yield return null;
                if (filtering.IsFaulted)
                { Debug.LogException(filtering.Exception); status("先行卡数据校验失败，已有卡包已保留。"); yield break; }
                if (File.Exists(target)) File.Replace(filtered, target, null);
                else File.Move(filtered, target);
                using (var version = UnityWebRequest.Get(Settings.GetPrereleasePackVersionUrl()))
                {
                    version.timeout = 10;
                    yield return version.SendWebRequest();
                    if (version.result == UnityWebRequest.Result.Success)
                        Config.Set("Prerelease", version.downloadHandler.text.Trim().Split('\n')[0].Trim());
                }
                Config.Save();
                status("先行卡数据更新成功，卡图将按需下载。");
                Program.instance.InitializeForDataChange();
            }
            finally
            {
                if (File.Exists(raw)) File.Delete(raw);
                if (File.Exists(filtered)) File.Delete(filtered);
            }
        }
    }
}
