using Vortice.ShaderCompiler;

namespace WingCommander.ShaderBuild;

/// <summary>
/// The GLSL shaders of <c>WingCommander.Render.Vulkan</c> and the single definition of how they
/// are compiled to SPIR-V. Shared (linked source) by the shader build tool, which writes
/// <c>Shaders/Compiled/&lt;name&gt;.spv</c>, and by the renderer tests, which fail when a
/// checked-in <c>.spv</c> no longer matches its GLSL source.
/// </summary>
public static class ShaderSet
{
    /// <summary>Folder (relative to the repository root) that holds the GLSL sources.</summary>
    public const string RelativeShaderDirectory = "src/WingCommander.Render.Vulkan/Shaders";

    /// <summary>Sub-folder of the shader folder that receives the SPIR-V files.</summary>
    public const string CompiledDirectoryName = "Compiled";

    /// <summary>Shader stages by file extension (shaderc infers the stage from it).</summary>
    private static readonly string[] StageExtensions = [".vert", ".frag", ".comp", ".geom", ".tesc", ".tese"];

    /// <summary>Walks up from <paramref name="startDirectory"/> to the repository and returns the shader folder.</summary>
    public static string? FindShaderDirectory(string startDirectory)
    {
        for (var dir = new DirectoryInfo(startDirectory); dir is not null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, RelativeShaderDirectory);
            if (Directory.Exists(candidate))
                return candidate;
        }
        return null;
    }

    /// <summary>All shader sources in <paramref name="shaderDirectory"/>, ordinal order.</summary>
    public static List<string> EnumerateSources(string shaderDirectory)
    {
        var sources = new List<string>();
        foreach (string path in Directory.EnumerateFiles(shaderDirectory))
        {
            string extension = Path.GetExtension(path);
            if (Array.IndexOf(StageExtensions, extension) >= 0)
                sources.Add(path);
        }
        sources.Sort(StringComparer.Ordinal);
        return sources;
    }

    /// <summary>Where the SPIR-V of <paramref name="sourcePath"/> lives (<c>Compiled/classic.frag.spv</c>).</summary>
    public static string CompiledPath(string sourcePath) =>
        Path.Combine(Path.GetDirectoryName(sourcePath)!, CompiledDirectoryName, Path.GetFileName(sourcePath) + ".spv");

    /// <summary>
    /// Compiles one GLSL file. Target: Vulkan 1.2 (SPIR-V 1.5, the renderer's baseline), optimised
    /// for performance, no debug info, warnings are errors. Line endings are normalised so a CRLF
    /// checkout produces the same bytes.
    /// </summary>
    public static byte[] Compile(Compiler compiler, string sourcePath)
    {
        string source = File.ReadAllText(sourcePath).Replace("\r\n", "\n", StringComparison.Ordinal);
        var options = new CompilerOptions
        {
            TargetEnv = TargetEnvironmentVersion.Vulkan_1_2,
            OptimizationLevel = OptimizationLevel.Performance,
            WarningsAsErrors = true,
        };
        options.IncludeDirectories.Add(Path.GetDirectoryName(Path.GetFullPath(sourcePath))!);

        CompileResult result = compiler.Compile(source, Path.GetFileName(sourcePath), options);
        if (result.Status != CompilationStatus.Success || result.Bytecode.Length == 0)
            throw new InvalidOperationException($"Shader compilation failed for {sourcePath} ({result.Status}):{Environment.NewLine}{result.ErrorMessage}");
        return result.Bytecode;
    }
}
