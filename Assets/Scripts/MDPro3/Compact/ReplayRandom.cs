namespace MDPro3.Compact
{
    // Standard MT19937 first output used by the legacy YRP replay format.
    public static class ReplayRandom
    {
        public static uint First(uint seed)
        {
            var state = new uint[624];
            state[0] = seed;
            unchecked
            {
                for (uint i = 1; i < 624; ++i) state[i] = 1812433253U * (state[i - 1] ^ (state[i - 1] >> 30)) + i;
                uint joined = (state[0] & 0x80000000U) | (state[1] & 0x7fffffffU);
                uint value = state[397] ^ (joined >> 1) ^ ((joined & 1) != 0 ? 0x9908b0dfU : 0);
                value ^= value >> 11; value ^= (value << 7) & 0x9d2c5680U;
                value ^= (value << 15) & 0xefc60000U; value ^= value >> 18;
                return value;
            }
        }
    }
}
