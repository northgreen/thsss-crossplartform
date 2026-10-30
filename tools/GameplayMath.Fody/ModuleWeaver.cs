using Fody;
using GameplayMath;

public sealed class ModuleWeaver : BaseModuleWeaver
{
    public override void Execute()
    {
        var random = (string?)Config?.Attribute("ExcludeOwnedRandom") ?? "true";
        var width = (string?)Config?.Attribute("TargetPointerSize") ?? "0";
        if (!bool.TryParse(random, out var excludeRandom) || !excludeRandom || !int.TryParse(width, out var pointerSize) || pointerSize is not (0 or 4 or 8))
            throw new WeavingException("GameplayMath: ExcludeOwnedRandom must be true (manually owned runtime); TargetPointerSize must be 0/4/8");
        string manifestPath = (string?)Config?.Attribute("ManifestPath") ?? Path.Combine(Path.GetDirectoryName(ModuleDefinition.FileName)!, "GameplayMath.manifest.json");
        try
        {
            var manifest = RewritePass.Execute(ModuleDefinition, new(excludeRandom, pointerSize));
            manifest.Write(manifestPath);
            WriteInfo($"GameplayMath: {manifest.Methods.Count(m => m.Action == "rewritten")} methods rewritten; {manifest.OutsideScopeUnchanged} excluded methods unchanged. Manifest: {manifestPath}");
        }
        catch (WeaveException error)
        {
            error.Manifest.Write(manifestPath);
            throw new WeavingException($"GameplayMath rejected {error.Manifest.Errors.Count} unsupported/review-required methods. See {manifestPath}\n{error.Message}");
        }
    }
    public override IEnumerable<string> GetAssembliesForScanning() => [];
    public override bool ShouldCleanReference => true;
}
