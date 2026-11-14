using System;
using System.IO;
using UnityEngine.Networking;

namespace MDPro3.Compact
{
    internal sealed class BoundedDownloadHandler : DownloadHandlerScript
    {
        private LimitedWriteStream output;
        public BoundedDownloadHandler(string path, long maximum) : base(new byte[32768])
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
