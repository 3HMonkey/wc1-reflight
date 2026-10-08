using System.Runtime.CompilerServices;
using Vortice.ShaderCompiler;
using WingCommander.Render.Vulkan.Internal;
using WingCommander.ShaderBuild;

namespace WingCommander.Render.Vulkan.Tests;

/// <summary>A fact that is skipped when the shaderc native library (NuGet) cannot be loaded on this platform.</summary>
public sealed class ShadercFactAttribute : FactAttribute
{
    private static readonly Lazy<string?> Problem = new(() =>
    {
        try
        {
            using var compiler = new Compiler();
            return null;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or TypeInitializationException)
        {
            return e.Message;
        }
    });

    public ShadercFactAttribute()
    {
        if (Problem.Value is { } problem)
            Skip = $"shaderc is not available on this platform: {problem}";
    }
}

/// <summary>The checked-in SPIR-V is what the GLSL sources compile to, and it is embedded.</summary>
public sealed class ShaderTests
{
    private static string ShaderDirectory([CallerFilePath] string thisFile = "") =>
        ShaderSet.FindShaderDirectory(Path.GetDirectoryName(thisFile)!)
        ?? ShaderSet.FindShaderDirectory(Environment.CurrentDirectory)
        ?? throw new DirectoryNotFoundException($"{ShaderSet.RelativeShaderDirectory} not found above {thisFile}.");

    [Fact]
    public void EveryShaderSource_HasEmbeddedSpirvIdenticalToTheCheckedInFile()
    {
        List<string> sources = ShaderSet.EnumerateSources(ShaderDirectory());
        Assert.Contains(sources, s => s.EndsWith("classic.vert", StringComparison.Ordinal));
        Assert.Contains(sources, s => s.EndsWith("classic.frag", StringComparison.Ordinal));
        foreach (string source in sources)
        {
            string compiled = ShaderSet.CompiledPath(source);
            Assert.True(File.Exists(compiled), $"{compiled} is missing: run dotnet run --project tools/WingCommander.ShaderBuild");
            byte[] embedded = ShaderLibrary.Load(Path.GetFileName(compiled));
            Assert.Equal(File.ReadAllBytes(compiled), embedded);
        }
    }

    [ShadercFact]
    public void CheckedInSpirv_IsNotStale()
    {
        using var compiler = new Compiler();
        foreach (string source in ShaderSet.EnumerateSources(ShaderDirectory()))
        {
            byte[] fresh = ShaderSet.Compile(compiler, source);
            byte[] checkedIn = File.ReadAllBytes(ShaderSet.CompiledPath(source));
            Assert.True(fresh.AsSpan().SequenceEqual(checkedIn),
                $"{Path.GetFileName(source)} changed but its SPIR-V was not regenerated: run dotnet run --project tools/WingCommander.ShaderBuild");
        }
    }
}
