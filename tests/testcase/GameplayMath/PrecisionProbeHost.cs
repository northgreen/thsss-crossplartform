using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.Loader;

// Set the software precision on the same thread that executes the unchanged probe entry point.
if (args.Length < 3) throw new ArgumentException("Expected game assembly, precision (24/53), probe assembly and probe arguments");
string gamePath = Path.GetFullPath(args[0]);
int precision = int.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture);
if (precision is not (24 or 53)) throw new ArgumentOutOfRangeException(nameof(precision));
string directory = Path.GetDirectoryName(gamePath)!;
AssemblyLoadContext.Default.Resolving += (context, name) =>
{
    string candidate = Path.Combine(directory, name.Name + ".dll");
    return File.Exists(candidate) ? context.LoadFromAssemblyPath(candidate) : null;
};
var game = AssemblyLoadContext.Default.LoadFromAssemblyPath(gamePath);
var runtime = game.GetType("SlimDX.Compat.LegacyFloat", true)!;
runtime.GetProperty("Precision")!.SetValue(null, precision);
Console.WriteLine("SOFTWARE_PRECISION=" + precision);
var probe = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(args[2]));
var entry = probe.EntryPoint ?? throw new MissingMethodException("Probe has no entry point");
try
{
    object? result = entry.Invoke(null, new object[] { args.Skip(3).ToArray() });
    if (result is int exitCode) Environment.ExitCode = exitCode;
}
catch (TargetInvocationException error) when (error.InnerException != null)
{
    ExceptionDispatchInfo.Capture(error.InnerException).Throw();
    throw;
}
