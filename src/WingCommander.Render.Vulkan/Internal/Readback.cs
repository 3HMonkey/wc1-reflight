using Vortice.Vulkan;

namespace WingCommander.Render.Vulkan.Internal;

/// <summary>Converts read-back pixels of the supported target formats to RGBA8 (alpha forced to 255).</summary>
internal static unsafe class Readback
{
    public static bool CanConvert(VkFormat format) => format is
        VkFormat.R8G8B8A8Unorm or VkFormat.R8G8B8A8Srgb or
        VkFormat.B8G8R8A8Unorm or VkFormat.B8G8R8A8Srgb or
        VkFormat.A2B10G10R10UnormPack32 or VkFormat.A2R10G10B10UnormPack32;

    /// <summary>All supported formats are 4 bytes per pixel.</summary>
    public const int BytesPerPixel = 4;

    public static byte[] ToRgba(byte* source, int width, int height, VkFormat format)
    {
        int count = width * height;
        var rgba = new byte[count * 4];
        switch (format)
        {
            case VkFormat.R8G8B8A8Unorm:
            case VkFormat.R8G8B8A8Srgb:
                new ReadOnlySpan<byte>(source, count * 4).CopyTo(rgba);
                for (int i = 3; i < rgba.Length; i += 4)
                    rgba[i] = 255;
                break;

            case VkFormat.B8G8R8A8Unorm:
            case VkFormat.B8G8R8A8Srgb:
                for (int i = 0, o = 0; i < count; i++, o += 4)
                {
                    rgba[o] = source[o + 2];
                    rgba[o + 1] = source[o + 1];
                    rgba[o + 2] = source[o];
                    rgba[o + 3] = 255;
                }
                break;

            case VkFormat.A2B10G10R10UnormPack32:
            case VkFormat.A2R10G10B10UnormPack32:
            {
                bool redLow = format == VkFormat.A2B10G10R10UnormPack32;
                uint* words = (uint*)source;
                for (int i = 0, o = 0; i < count; i++, o += 4)
                {
                    uint v = words[i];
                    uint low = v & 0x3FF, mid = (v >> 10) & 0x3FF, high = (v >> 20) & 0x3FF;
                    rgba[o] = Ten(redLow ? low : high);
                    rgba[o + 1] = Ten(mid);
                    rgba[o + 2] = Ten(redLow ? high : low);
                    rgba[o + 3] = 255;
                }
                break;
            }

            default:
                throw new NotSupportedException($"Cannot convert {VkNames.Format(format)} pixels to RGBA.");
        }
        return rgba;
    }

    private static byte Ten(uint value) => (byte)((value * 255 + 511) / 1023);
}
