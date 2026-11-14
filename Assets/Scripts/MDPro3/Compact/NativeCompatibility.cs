using System;
using System.Runtime.InteropServices;

namespace MDPro3.Compact
{
    public static class NativeCompatibility
    {
#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern int mdpro3_protocol_version();
        [DllImport("__Internal")] private static extern int mdpro3_card_data_size();
#endif
        public static void Validate()
        {
#if UNITY_IOS && !UNITY_EDITOR
            if (mdpro3_protocol_version() != Config.ClientVersion)
                throw new InvalidOperationException("Local server protocol does not match the client.");
            int managedSize = Marshal.SizeOf(typeof(Percy.CardData));
            if (mdpro3_card_data_size() != managedSize)
                throw new InvalidOperationException("Native card_data ABI mismatch: managed=" + managedSize + ", native=" + mdpro3_card_data_size());
            if (Marshal.SizeOf(typeof(YGOSharp.OCGWrapper.Card.CardData)) != managedSize)
                throw new InvalidOperationException("Legacy card wrapper ABI does not match the selected native core.");
#endif
        }
    }
}
