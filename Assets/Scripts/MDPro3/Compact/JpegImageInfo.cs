using System;

namespace MDPro3.Compact
{
    public static class JpegImageInfo
    {
        // Reject huge dimensions before passing network bytes to Unity's native decoder.
        public static bool HasSafeDimensions(byte[] bytes, int maximumSide = 2048)
        {
            if (bytes == null || bytes.Length < 4 || bytes[0] != 255 || bytes[1] != 216) return false;
            int position = 2;
            while (position < bytes.Length)
            {
                if (bytes[position++] != 255) return false;
                while (position < bytes.Length && bytes[position] == 255) position++;
                if (position >= bytes.Length) return false;
                int marker = bytes[position++];
                if (marker == 217 || marker == 218) return false;
                if (marker == 1 || marker >= 208 && marker <= 215) continue;
                if (position + 2 > bytes.Length) return false;
                int length = (bytes[position] << 8) | bytes[position + 1];
                if (length < 2 || length > bytes.Length - position) return false;
                bool frame = marker >= 192 && marker <= 207 && marker != 196 && marker != 200 && marker != 204;
                if (frame)
                {
                    if (length < 8) return false;
                    int height = (bytes[position + 3] << 8) | bytes[position + 4];
                    int width = (bytes[position + 5] << 8) | bytes[position + 6];
                    return width >= 16 && height >= 16 && width <= maximumSide && height <= maximumSide;
                }
                position += length;
            }
            return false;
        }
    }
}
