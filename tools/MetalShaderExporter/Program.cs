using SlimDX.Direct3D9;
using THSSS.Graphics;
using THSSS.Graphics.Metal;

if (args.Length != 1)
    throw new ArgumentException("Usage: MetalShaderExporter OUTPUT_DIRECTORY");

string output = Path.GetFullPath(args[0]);
Directory.CreateDirectory(output);
var stages = new Dictionary<string, string>(StringComparer.Ordinal);

void Export(ShaderSource source)
{
    foreach (var (glsl, vertex) in new[] { (source.Vertex, true), (source.Fragment, false) })
    {
        string key = MetalSourceCompiler.CacheKey(glsl, vertex);
        if (stages.ContainsKey(key)) continue;
        string msl = MetalSourceCompiler.Translate(glsl, vertex);
        if (!msl.Contains("metal_stdlib", StringComparison.Ordinal)
            || !msl.Contains(vertex ? "vertex " : "fragment ", StringComparison.Ordinal)
            || !msl.Contains("main0", StringComparison.Ordinal))
            throw new InvalidOperationException("Shader translation produced invalid MSL: " + key);
        stages.Add(key, msl);
    }
}

// Read the compiled sources used by the game, without constructing a GPU device.
Export(Sprite.Source);
Export(Device3D.CreateShaderSource());
foreach (var kind in Enum.GetValues<EffectKind>()) Export(EffectShaders.BackendSource(kind));

foreach (var (key, msl) in stages)
    File.WriteAllText(Path.Combine(output, key), msl);
Console.WriteLine($"Exported {stages.Count} Metal shader stages to {output}");
