using System.Numerics;
using WingCommander.Core.Imaging;
using Xunit;
using Xunit.Abstractions;

namespace WingCommander.Core.Tests.Imaging;

public sealed class PixelOutlineTests(ITestOutputHelper output)
{
    private static bool[] Mask(params string[] rows)
    {
        int width = rows[0].Length;
        var mask = new bool[width * rows.Length];
        for (int y = 0; y < rows.Length; y++)
            for (int x = 0; x < width; x++)
                mask[y * width + x] = rows[y][x] == '#';
        return mask;
    }

    private static List<List<Vector2>> Vectorize(params string[] rows)
    {
        var contours = PixelOutline.Trace(Mask(rows), rows[0].Length, rows.Length);
        return contours.ConvertAll(PixelOutline.Smooth);
    }

    private void Dump(List<List<Vector2>> polygons)
    {
        foreach (var polygon in polygons)
            output.WriteLine(string.Join(" ", polygon.ConvertAll(p => $"({p.X},{p.Y})")));
    }

    [Fact]
    public void SinglePixel_StaysASquare()
    {
        var polygons = Vectorize("#");
        Assert.Single(polygons);
        Assert.Equal([new(0, 0), new(1, 0), new(1, 1), new(0, 1)], polygons[0]);
    }

    [Fact]
    public void Rectangle_KeepsItsCorners()
    {
        var polygons = Vectorize("###", "###");
        Assert.Single(polygons);
        Assert.Equal([new(0, 0), new(3, 0), new(3, 2), new(0, 2)], polygons[0]);
    }

    [Fact]
    public void SmallPlus_KeepsSquareArms()
    {
        var polygons = Vectorize(".#.", "###", ".#.");
        Dump(polygons);
        Assert.Single(polygons);
        // A thin cross keeps its pixel shape: square arm ends and sharp inner corners.
        Assert.Equal(12, polygons[0].Count);
        Assert.Contains(new Vector2(2, 1), polygons[0]);
        Assert.Contains(new Vector2(3, 1), polygons[0]);
    }

    [Fact]
    public void DiagonalLine_IsOneConnectedShape_WithStraightSides()
    {
        var polygons = Vectorize("#...", ".#..", "..#.", "...#");
        Dump(polygons);
        Assert.Single(polygons);
        // The middle of the upper side lies on the 45-degree line through the step midpoints.
        Assert.Contains(new Vector2(2, 1.5f), polygons[0]);
        Assert.Contains(new Vector2(2.5f, 2), polygons[0]);
    }

    [Fact]
    public void CornerCut_BecomesAChamfer()
    {
        // A rounded rectangle corner (one pixel cut) as in font 0's O.
        var polygons = Vectorize(".####", "#####", "#####");
        Dump(polygons);
        Assert.Single(polygons);
        Assert.Contains(new Vector2(0.5f, 1), polygons[0]);
        Assert.Contains(new Vector2(1, 0.5f), polygons[0]);
        Assert.DoesNotContain(new Vector2(1, 1), polygons[0]);
        Assert.Contains(new Vector2(5, 0), polygons[0]); // the other corners stay
    }

    [Fact]
    public void TriangleTip_SmoothsTheLongStaircases()
    {
        // The top of a hollow one-pixel A: the cap stays flat, the sides become diagonals.
        var polygons = Vectorize("...#...", "..#.#..", ".#...#.", "#.....#");
        Dump(polygons);
        Assert.Single(polygons);
        Assert.Contains(new Vector2(3, 0), polygons[0]);
        Assert.Contains(new Vector2(4, 0), polygons[0]);
        Assert.DoesNotContain(new Vector2(5, 2), polygons[0]); // the staircase corners are gone
    }

    [Fact]
    public void SignedDistance_UsesNonZeroWinding_ForOverlappingContours()
    {
        // Two overlapping squares with the same orientation (as in fonts built from bricks): the
        // overlap stays inside (even-odd would cut a hole).
        var a = new List<Vector2> { new(0, 0), new(2, 0), new(2, 2), new(0, 2) };
        var b = new List<Vector2> { new(1, 1), new(3, 1), new(3, 3), new(1, 3) };
        var distance = new float[6 * 6];
        PixelOutline.SignedDistance([a, b], 6, 6, 2f, 0f, 0f, distance, 10f);
        Assert.True(distance[3 * 6 + 3] > 0f); // sample (1.75, 1.75): in both squares
        Assert.True(distance[0] > 0f);         // (0.25, 0.25): only in a
        Assert.True(distance[5 * 6 + 0] < 0f); // (0.25, 2.75): in neither
    }

    [Fact]
    public void Ring_HasAHole()
    {
        var polygons = Vectorize("###", "#.#", "###");
        Assert.Equal(2, polygons.Count);
    }

    [Fact]
    public void SignedDistance_IsPositiveInside_NegativeOutside_ZeroOnTheEdge()
    {
        var polygons = Vectorize("##", "##");
        var distance = new float[8 * 8];
        // 2 samples per unit, starting one unit before the square: samples at -0.75, -0.25, 0.25, ...
        PixelOutline.SignedDistance(polygons.ConvertAll(p => (IReadOnlyList<Vector2>)p), 8, 8, 2f, -1f, -1f, distance, 10f);
        Assert.True(distance[4 * 8 + 4] > 0f);        // sample (1.25, 1.25): inside
        Assert.True(distance[0] < 0f);                // sample (-0.75, -0.75): outside
        Assert.Equal(0.5f, distance[2 * 8 + 4], 3);   // (1.25, 0.25): 0.25 units = 0.5 samples inside the top edge
        Assert.Equal(-0.5f, distance[1 * 8 + 4], 3);  // (1.25, -0.25): 0.5 samples outside
    }

    [Fact]
    public void DistanceTransform_MatchesBruteForce()
    {
        var random = new Random(5);
        const int width = 23, height = 17;
        var feature = new bool[width * height];
        for (int i = 0; i < feature.Length; i++)
            feature[i] = random.Next(9) == 0;
        var result = new float[feature.Length];
        DistanceTransform.Squared(feature, width, height, result);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float best = float.MaxValue;
                for (int i = 0; i < feature.Length; i++)
                {
                    if (!feature[i])
                        continue;
                    float dx = i % width - x, dy = i / width - y;
                    best = MathF.Min(best, dx * dx + dy * dy);
                }
                Assert.Equal(best, result[y * width + x], 3);
            }
        }
    }

    [Fact]
    public void Scale2x_SmoothsADiagonal_AndKeepsABlock()
    {
        byte[] diagonal = [1, 0, 0, 1];
        var scaled = new byte[16];
        PixelArtScaler.Scale2x(diagonal, 2, 2, scaled, 0);
        // The two background pixels get the sub-pixel that touches both diagonal neighbours.
        Assert.Equal(1, scaled[0 * 4 + 2 + 1 * 4]); // bottom-left sub-pixel of the top-right pixel: (2, 1)
        byte[] block = [1, 1, 1, 1];
        PixelArtScaler.Scale2x(block, 2, 2, scaled, 1);
        Assert.All(scaled, value => Assert.Equal(1, value));
    }
}
