using System;
using System.Collections;
using System.IO;
using UnityEngine.Networking;

namespace MDPro3.Compact
{
    public static class CompactPackageDownloader
    {
        public static IEnumerator Download(string url, Action<string> status)
        {
            string name = Path.GetFileName(new Uri(url).AbsolutePath);
            string target = Path.Combine(Program.PATH_EXPANSIONS, name);
            string temporary = target + ".compact-download";
            Directory.CreateDirectory(Program.PATH_EXPANSIONS);
            try
            {
                if (File.Exists(temporary)) File.Delete(temporary);
                if (!OnDemandCardImages.PrepareStorageWrite(PrereleasePackFilter.DownloadLimit))
                { status("扩展包下载已停止：精简版空间预算不足。"); yield break; }
                using (var handler = new BoundedDownloadHandler(temporary, PrereleasePackFilter.DownloadLimit))
                using (var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbGET))
                {
                    request.downloadHandler = handler; request.disposeDownloadHandlerOnDispose = false;
                    request.timeout = 120;
                    yield return request.SendWebRequest();
                    handler.Finish();
                    if (request.result != UnityWebRequest.Result.Success)
                    { status("下载失败或文件超过 45 MB 配额，已有扩展包已保留。"); yield break; }
                }
                if (File.Exists(target)) File.Replace(temporary, target, null);
                else File.Move(temporary, target);
                status("扩展包下载成功。");
                Program.instance.InitializeForDataChange();
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
