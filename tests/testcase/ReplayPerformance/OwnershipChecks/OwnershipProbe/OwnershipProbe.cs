using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;

namespace THSSS.Diagnostics;

public static class OwnershipProbe
{
    private static object? registeredStage;
    private static int expectedThread;
    private static int active;
    private static long frames, violations, scopedWrites, scopedCalls, excludedWrites, excludedCalls;
    private static string? firstViolation;
    private static readonly ConcurrentDictionary<Type, PropertyInfo> stageProperties = new();
    private static readonly ConcurrentDictionary<string, long> writes = new();
    private static readonly ConcurrentDictionary<string, long> calls = new();

    public static void Arm(object stage)
    {
        ArgumentNullException.ThrowIfNull(stage);
        if (Interlocked.CompareExchange(ref expectedThread, Environment.CurrentManagedThreadId, 0) != 0)
            throw new InvalidOperationException("Ownership probe already armed; use a fresh process.");
        Volatile.Write(ref registeredStage, stage);
    }

    public static void EnterFrame(object? state)
    {
        if (Volatile.Read(ref expectedThread) == 0) return;
        if (Environment.CurrentManagedThreadId != expectedThread)
            Fail("EnterFrame", state);
        bool isStage = false;
        for (Type? type = state?.GetType(); type != null; type = type.BaseType)
            isStage |= type.FullName == "Shooting.BaseStage";
        Volatile.Write(ref active, isStage && ReferenceEquals(StageOf(state), registeredStage) ? 1 : 0);
        Interlocked.Increment(ref frames);
    }

    public static void GuardWrite(object? owner, object? assignedStage, string field, string site)
    {
        if (!InScope(owner, assignedStage)) { Interlocked.Increment(ref excludedWrites); return; }
        Interlocked.Increment(ref scopedWrites);
        writes.AddOrUpdate(field, 1, (_, count) => count + 1);
        if (Environment.CurrentManagedThreadId != Volatile.Read(ref expectedThread))
            Fail("write " + field + " @ " + site, owner);
    }

    public static void GuardCall(object? owner, string site)
    {
        if (!InScope(owner, null)) { Interlocked.Increment(ref excludedCalls); return; }
        Interlocked.Increment(ref scopedCalls);
        calls.AddOrUpdate(site, 1, (_, count) => count + 1);
        CheckThread(site, owner);
    }

    private static bool InScope(object? owner, object? assignedStage)
    {
        if (Volatile.Read(ref active) == 0 || Volatile.Read(ref expectedThread) == 0) return false;
        object? stage = Volatile.Read(ref registeredStage);
        return ReferenceEquals(owner, stage) || ReferenceEquals(assignedStage, stage)
            || ReferenceEquals(StageOf(owner), stage);
    }

    private static object? StageOf(object? owner)
    {
        if (owner == null) return null;
        if (owner.GetType().FullName == "Shooting.StageDataPackage") return owner;
        PropertyInfo property = stageProperties.GetOrAdd(owner.GetType(), type =>
            type.GetProperty("StageData", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("StageData accessor missing on " + type.FullName));
        return property.GetValue(owner);
    }

    private static void CheckThread(string site, object? owner)
    {
        if (Environment.CurrentManagedThreadId != Volatile.Read(ref expectedThread)) Fail(site, owner);
    }

    private static void Fail(string site, object? owner)
    {
        string error = $"Active-stage ownership violation: {site}; type={owner?.GetType().FullName}; thread={Environment.CurrentManagedThreadId}; expected={expectedThread}";
        Interlocked.Increment(ref violations);
        Interlocked.CompareExchange(ref firstViolation, error, null);
        throw new InvalidOperationException(error);
    }

    public static void SaveReport(string path)
    {
        bool covered = expectedThread != 0 && frames > 0 && scopedWrites > 0 && scopedCalls > 0;
        var report = new { expectedThread, armedStage = registeredStage?.GetType().FullName,
            frameObservations = frames, scopedWrites, scopedCalls, excludedWrites, excludedCalls,
            violations, firstViolation, coverageNonempty = covered, passed = covered && violations == 0,
            fieldWriteCounts = writes.OrderBy(x => x.Key).ToDictionary(),
            callCounts = calls.OrderBy(x => x.Key).ToDictionary(),
            scope = "Registered StageData only while current state derives from Shooting.BaseStage; excludes suspended/loading and unrelated stages. Diagnostic copy only." };
        File.WriteAllText(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        if (!covered || violations != 0)
            throw new InvalidOperationException("Ownership gate failed or coverage empty; inspect " + path);
    }
}
