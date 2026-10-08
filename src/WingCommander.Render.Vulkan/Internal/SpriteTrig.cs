namespace WingCommander.Render.Vulkan.Internal;

/// <summary>
/// Sprite rotation trigonometry with the original's resolution: angles in 0.1 degree steps and
/// values of the 16.16 quarter-cosine table (<c>anRLEQuarterCosine</c>, which holds
/// <c>round(cos(i * 0.1 deg) * 65536)</c> for i = 0..900), so rotated sprites match the software
/// path's angles exactly.
/// </summary>
internal static class SpriteTrig
{
    private static readonly float[] Cosine = BuildTable();

    /// <summary>Cosine and sine of <paramref name="degrees"/> rounded to 0.1 degree.</summary>
    public static (float Cos, float Sin) Get(float degrees)
    {
        double tenths = Math.Round(degrees * 10.0) % 3600.0;
        if (tenths < 0)
            tenths += 3600.0;
        int a = (int)tenths % 3600;
        return (Cosine[a], Cosine[(a + 2700) % 3600]); // sin(a) = cos(a - 90 deg)
    }

    /// <summary>The 16.16 quarter table value for <paramref name="tenths"/> of a degree (0..900).</summary>
    public static int QuarterCosine(int tenths) => (int)Math.Round(Math.Cos(tenths * Math.PI / 1800.0) * 65536.0);

    private static float[] BuildTable()
    {
        var table = new float[3600];
        for (int a = 0; a < 3600; a++)
        {
            int value = a <= 900 ? QuarterCosine(a)
                : a <= 1800 ? -QuarterCosine(1800 - a)
                : a <= 2700 ? -QuarterCosine(a - 1800)
                : QuarterCosine(3600 - a);
            table[a] = value / 65536f;
        }
        return table;
    }
}
