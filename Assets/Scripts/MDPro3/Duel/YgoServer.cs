using System.Runtime.InteropServices;
using System.Threading;
using MDPro3.Duel.YGOSharp;

namespace MDPro3.Net
{
    internal static unsafe class Dll
    {
#if UNITY_IOS && !UNITY_EDITOR
        private const string NativeLibrary = "__Internal";
#else
        private const string NativeLibrary = "ygoserver";
#endif
        [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
        public static extern int start_server([MarshalAs(UnmanagedType.LPUTF8Str)] string args);
        [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
        public static extern void stop_server();
#if UNITY_IOS && !UNITY_EDITOR
        [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
        public static extern int mdpro3_server_ready();
#endif
    }

    public class YgoServer
    {
        public static Thread serverThread;
        public static void StartServer(string args)
        {
            if (ServerRunning())
                StopServer();

            serverThread = new Thread(() =>
            {
                Dll.start_server(args);
            });
            serverThread.IsBackground = true;
            serverThread.Start();
        }

        public static void StopServer()
        {
            if (serverThread == null || !serverThread.IsAlive) return;
#if UNITY_IOS && !UNITY_EDITOR
            var deadline = System.Diagnostics.Stopwatch.StartNew();
            while (serverThread.IsAlive && Dll.mdpro3_server_ready() == 0 && deadline.ElapsedMilliseconds < 3000)
                Thread.Sleep(1);
#endif
            Dll.stop_server();
            if (!serverThread.Join(3000))
                throw new System.TimeoutException("Local duel server did not stop; refusing to start a second native session.");
            serverThread = null;
        }

        public static bool ServerRunning()
        {
            return serverThread != null && serverThread.IsAlive;
        }
    }
}
