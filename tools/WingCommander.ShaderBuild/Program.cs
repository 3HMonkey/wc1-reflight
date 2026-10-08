using Vortice.ShaderCompiler;
using WingCommander.ShaderBuild;

// wc1-shaderbuild [--check] [--shaders <dir>]
//   Compiles every GLSL shader of WingCommander.Render.Vulkan to Shaders/Compiled/<name>.spv.
//   --check   compile in memory only; exit code 1 when a .spv is missing, stale or orphaned.

bool check = false;
string? shaderDirectory = null;
for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--check":
            check = true;
            break;
        case "--shaders" when i + 1 < args.Length:
            shaderDirectory = Path.GetFullPath(args[++i]);
            break;
        default:
            Console.Error.WriteLine("usage: wc1-shaderbuild [--check] [--shaders <dir>]");
            return 2;
    }
}

shaderDirectory ??= ShaderSet.FindShaderDirectory(Environment.CurrentDirectory)
    ?? ShaderSet.FindShaderDirectory(AppContext.BaseDirectory);
if (shaderDirectory is null || !Directory.Exists(shaderDirectory))
{
    Console.Error.WriteLine($"Shader folder not found; run from the repository or pass --shaders <dir> ({ShaderSet.RelativeShaderDirectory}).");
    return 2;
}

List<string> sources = ShaderSet.EnumerateSources(shaderDirectory);
string compiledDirectory = Path.Combine(shaderDirectory, ShaderSet.CompiledDirectoryName);
Directory.CreateDirectory(compiledDirectory);

int problems = 0;
using (var compiler = new Compiler())
{
    foreach (string source in sources)
    {
        string target = ShaderSet.CompiledPath(source);
        byte[] spirv;
        try
        {
            spirv = ShaderSet.Compile(compiler, source);
        }
        catch (InvalidOperationException e)
        {
            Console.Error.WriteLine(e.Message);
            problems++;
            continue;
        }

        bool upToDate = File.Exists(target) && File.ReadAllBytes(target).AsSpan().SequenceEqual(spirv);
        string name = Path.GetFileName(target);
        if (upToDate)
        {
            Console.WriteLine($"  up to date  {name} ({spirv.Length} bytes)");
        }
        else if (check)
        {
            Console.Error.WriteLine($"  STALE       {name} (run: dotnet run --project tools/WingCommander.ShaderBuild)");
            problems++;
        }
        else
        {
            File.WriteAllBytes(target, spirv);
            Console.WriteLine($"  written     {name} ({spirv.Length} bytes)");
        }
    }
}

// SPIR-V files whose GLSL source was deleted.
foreach (string compiled in Directory.EnumerateFiles(compiledDirectory, "*.spv"))
{
    string sourceName = Path.GetFileNameWithoutExtension(compiled);
    if (File.Exists(Path.Combine(shaderDirectory, sourceName)))
        continue;
    if (check)
    {
        Console.Error.WriteLine($"  ORPHAN      {Path.GetFileName(compiled)}");
        problems++;
    }
    else
    {
        File.Delete(compiled);
        Console.WriteLine($"  deleted     {Path.GetFileName(compiled)}");
    }
}

Console.WriteLine($"{sources.Count} shader(s), {problems} problem(s).");
return problems == 0 ? 0 : 1;
