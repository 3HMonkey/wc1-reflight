namespace WingCommander.Core.Imaging;

/// <summary>
/// Exact Euclidean distance transforms of binary images (Felzenszwalb and Huttenlocher, "Distance
/// Transforms of Sampled Functions", 2012): two separable passes of the lower envelope of
/// parabolas, linear in the number of pixels. Distances are measured between pixel centres.
/// </summary>
public static class DistanceTransform
{
    private const float Infinity = 1e20f;

    /// <summary>
    /// Squared distance from every pixel to the nearest pixel where <paramref name="feature"/> is
    /// true (0 on feature pixels; a very large value when there is none).
    /// </summary>
    public static void Squared(ReadOnlySpan<bool> feature, int width, int height, Span<float> result)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfNegative(height);
        int count = width * height;
        if (feature.Length < count || result.Length < count)
            throw new ArgumentException("The spans are smaller than width * height.");
        int longest = Math.Max(1, Math.Max(width, height));
        float[] f = new float[longest];
        float[] d = new float[longest];
        int[] v = new int[longest];
        float[] z = new float[longest + 1];

        for (int i = 0; i < count; i++)
            result[i] = feature[i] ? 0f : Infinity;
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
                f[y] = result[y * width + x];
            Transform1D(f, height, d, v, z);
            for (int y = 0; y < height; y++)
                result[y * width + x] = d[y];
        }
        for (int y = 0; y < height; y++)
        {
            Span<float> row = result.Slice(y * width, width);
            row.CopyTo(f);
            Transform1D(f, width, d, v, z);
            d.AsSpan(0, width).CopyTo(row);
        }
    }

    /// <summary>
    /// Signed distance in pixels to the boundary of <paramref name="inside"/>: positive inside,
    /// negative outside, zero halfway between an inside and an outside pixel centre. Images
    /// without inside (or without outside) pixels get -<paramref name="limit"/> (or +limit).
    /// </summary>
    public static void Signed(ReadOnlySpan<bool> inside, int width, int height, Span<float> result, float limit = 1e6f)
    {
        int count = width * height;
        if (inside.Length < count || result.Length < count)
            throw new ArgumentException("The spans are smaller than width * height.");
        var outside = new bool[count];
        bool anyInside = false, anyOutside = false;
        for (int i = 0; i < count; i++)
        {
            outside[i] = !inside[i];
            anyInside |= inside[i];
            anyOutside |= !inside[i];
        }
        if (!anyInside || !anyOutside)
        {
            result[..count].Fill(anyInside ? limit : -limit);
            return;
        }
        var toInside = new float[count];
        var toOutside = new float[count];
        Squared(inside, width, height, toInside);
        Squared(outside, width, height, toOutside);
        for (int i = 0; i < count; i++)
        {
            float value = inside[i]
                ? MathF.Sqrt(toOutside[i]) - 0.5f
                : 0.5f - MathF.Sqrt(toInside[i]);
            result[i] = Math.Clamp(value, -limit, limit);
        }
    }

    /// <summary>1D squared distance transform of <paramref name="f"/> (length <paramref name="n"/>) into <paramref name="d"/>.</summary>
    private static void Transform1D(float[] f, int n, float[] d, int[] v, float[] z)
    {
        if (n == 0)
            return;
        int k = 0;
        v[0] = 0;
        z[0] = -Infinity;
        z[1] = Infinity;
        for (int q = 1; q < n; q++)
        {
            float s = Intersection(f, q, v[k]);
            while (s <= z[k])
            {
                k--;
                if (k < 0)
                    break;
                s = Intersection(f, q, v[k]);
            }
            k++;
            v[k] = q;
            z[k] = k == 0 ? -Infinity : s;
            z[k + 1] = Infinity;
        }
        k = 0;
        for (int q = 0; q < n; q++)
        {
            while (z[k + 1] < q)
                k++;
            float distance = q - v[k];
            d[q] = distance * distance + f[v[k]];
        }
    }

    /// <summary>Where the parabolas rooted at <paramref name="q"/> and <paramref name="p"/> intersect.</summary>
    private static float Intersection(float[] f, int q, int p) =>
        ((f[q] + (float)q * q) - (f[p] + (float)p * p)) / (2f * q - 2f * p);
}
