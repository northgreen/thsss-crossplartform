using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Text.Json;
using Shooting;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;

internal static class Entry
{
    public static int Main(string[] args)
    {
        if (args.Length != 2 || !int.TryParse(args[1], out int frames) || frames < 0)
            throw new ArgumentException("Usage: ReplayPerformance ABSOLUTE_OUTPUT frames(0=whole replay)");
        if (Environment.GetEnvironmentVariable("THSSS_PROFILE_OWNERSHIP") == "1")
            Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, "OwnershipProbe.dll"));
        using var run = new ProfileRun(Path.GetFullPath(args[0]), frames);
        var subscription = new Thread(run.Subscribe) { IsBackground = true };
        subscription.Start();
        Shooting.Program.Main(Array.Empty<string>());
        subscription.Join(TimeSpan.FromSeconds(2));
        run.VerifyCompleted();
        return Environment.ExitCode;
    }
}

internal sealed class ProfileRun : IDisposable
{
    private static readonly JsonSerializerOptions Json = new() { IncludeFields = true, WriteIndented = true };
    private static readonly FieldInfo WindowField = Field(typeof(Shooting.Program), "_window");
    private static readonly FieldInfo GameField = Field(typeof(Shooting.Program), "_game");
    private static readonly FieldInfo StateField = Field(typeof(Game_Main), "PresentState");
    private static readonly string[] PhaseNames = { "UpdateTicks", "DrawTicks", "PresentTicks", "PacingTicks", "MainTicks", "CallbackTicks" };
    private readonly FieldInfo[]? phaseFields;
    private readonly FieldInfo[] callCounterFields;
    private long[]? initialCallCounts;
    private readonly bool backgroundReplayDiagnostic;
    private readonly bool replayKeyboardIsolated;
    private readonly string output;
    private readonly int limit;
    private readonly Stopwatch wall = Stopwatch.StartNew();
    private readonly List<Frame> rows = new(24000);
    private readonly List<object> transitions = new();
    private readonly List<string>? stateDigests = Environment.GetEnvironmentVariable("THSSS_PROFILE_STATE_HASH") == "1" ? new() : null;
    private readonly ReplayInfo metadata;
    private readonly string targetStage;
    private readonly int selectedRecord;
    private readonly MyPlaneInfo initialRecord;
    private readonly MyPlaneInfo completionRecord;
    private readonly string completionState;
    private readonly long markerOffset;
    private readonly string replayPath;
    private IWindow? window;
    private Game_Main? game;
    private StageDataPackage? stage;
    private Exception? failure;
    private bool started, completed;
    private string? lastState;
    private long lastTimestamp, lastPosition, stageAllocatedBefore;
    private bool loadingGap;
    private int lastTime, lastLife, lifeDrops, deathEntries, previousDeadTime = -1;
    private object? initial;
    private readonly long allocatedBefore = GC.GetTotalAllocatedBytes(false);
    private readonly int[] gcBefore = { GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2) };

    private readonly record struct Frame(long Timestamp, long IntervalTicks, long CallbackTicks, bool LoadingGap,
        int StageTime, int TimeDelta, long KeyPosition, long KeyBytes,
        int Bullets, int Enemies, int Items, int MyBullets, int Life, int DeadTime,
        long AllocatedBatch, int Gen0, int Gen1, int Gen2,
        long UpdateTicks, long DrawTicks, long PresentTicks, long PacingTicks, long MainTicks, long InstrumentedCallbackTicks);

    public ProfileRun(string output, int limit)
    {
        this.output = output; this.limit = limit;
        Type? counters = typeof(Game_Main).Assembly.GetType("THSSS.Diagnostics.ReplayPhaseCounters");
        backgroundReplayDiagnostic = counters?.GetField("BackgroundReplayDiagnostic")?.GetRawConstantValue() is true;
        replayKeyboardIsolated = counters?.GetField("ReplayKeyboardIsolated")?.GetRawConstantValue() is true;
        phaseFields = counters == null ? null : PhaseNames.Select(name =>
            counters.GetField(name, BindingFlags.Public | BindingFlags.Static)
            ?? throw new MissingFieldException(counters.FullName, name)).ToArray();
        callCounterFields = counters?.GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.Name.StartsWith("Count_", StringComparison.Ordinal)).ToArray() ?? Array.Empty<FieldInfo>();
        replayPath = Path.Combine(Environment.GetEnvironmentVariable("THSSS_DATA_ROOT")!, "Replay/thSSS_25.rpy");
        metadata = Replay.ReadTitle(replayPath);
        targetStage = Environment.GetEnvironmentVariable("THSSS_PROFILE_STAGE") ?? metadata.StartStage;
        int startOrdinal = StageOrdinal(metadata.StartStage);
        int targetOrdinal = StageOrdinal(targetStage);
        selectedRecord = targetOrdinal - startOrdinal;
        if ((targetStage == "StEx") != (metadata.StartStage == "StEx") ||
            selectedRecord < 0 || selectedRecord + 1 >= metadata.MyPlaneData.Count)
            throw new InvalidDataException($"Replay {metadata.StartStage}→{metadata.LastStage} has no initial/final records for {targetStage}.");
        initialRecord = metadata.MyPlaneData[selectedRecord];
        completionRecord = metadata.MyPlaneData[selectedRecord + 1];
        completionState = targetStage == "StEx" ? "GameSetMenu" : targetStage == "St6" ? "All" : $"St{targetOrdinal + 2}";
        if (completionRecord.DataPosition <= initialRecord.DataPosition)
            throw new InvalidDataException("Selected stage final key offset must follow its initial offset.");
        byte[] data = File.ReadAllBytes(replayPath);
        markerOffset = -1;
        for (int i = 0; i + 1 < data.Length; i += 2)
            if (data[i] == 240 && data[i + 1] == 15) { markerOffset = i; break; }
        if (markerOffset < 0) throw new InvalidDataException("No aligned 4080 replay marker.");
        int stageControlMarkers = 0;
        for (long i = initialRecord.DataPosition; i < completionRecord.DataPosition; i += 2)
            if (data[i] == 14 && data[i + 1] == 224) stageControlMarkers++;
        Save("metadata.json", new { metadata, selectedStage = targetStage, selectedRecord, completionState,
            replayAlias = replayPath, replaySha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(data)).ToLowerInvariant(),
            sourceReplay = Environment.GetEnvironmentVariable("THSSS_PROFILE_REPLAY_SOURCE"), bytes = data.Length, markerOffset,
            initialKeyOffset = initialRecord.DataPosition,
            keyWordsToMarker = (markerOffset - initialRecord.DataPosition) / 2,
            stageControlMarkers, terminalMarkerValue = 4080,
            recordedCompletionOffset = completionRecord.DataPosition });
    }

    private static int StageOrdinal(string name) => name switch
    {
        "St1" => 0, "St2" => 1, "St3" => 2, "St4" => 3, "St5" => 4, "St6" => 5, "StEx" => 6,
        _ => throw new InvalidDataException($"Unsupported profiling stage '{name}'; expected St1..St6 or StEx.")
    };

    private static Type OwnershipProbeType() => AppDomain.CurrentDomain.GetAssemblies()
        .Single(a => a.GetName().Name == "OwnershipProbe").GetType("THSSS.Diagnostics.OwnershipProbe", throwOnError: true)!;

    internal static FieldInfo Field(Type type, string name) =>
        type.GetField(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
        ?? throw new MissingFieldException(type.FullName, name);

    public void Subscribe()
    {
        try
        {
            while (wall.Elapsed.TotalSeconds < 45)
            {
                window = (IWindow?)WindowField.GetValue(null);
                if (window != null && GameField.GetValue(null) is Game_Main)
                {
                    // Wrap the existing callback, not MainProcess or any gameplay method.
                    var method = typeof(Shooting.Program).GetMethod("OnRender", BindingFlags.Static | BindingFlags.NonPublic)!;
                    var production = method.CreateDelegate<Action<double>>();
                    window.Render -= production;
                    window.Render += delta => Render(production, delta);
                    return;
                }
                Thread.Sleep(2);
            }
            throw new TimeoutException("Production did not publish its window/game within 45 seconds.");
        }
        catch (Exception error) { failure = error; window?.Close(); }
    }

    private void Render(Action<double> production, double delta)
    {
        long before = Stopwatch.GetTimestamp();
        production(delta);
        long after = Stopwatch.GetTimestamp();
        if (completed || failure != null || window!.IsClosing) return;
        try { Observe(after, after - before); }
        catch (Exception error)
        {
            failure = error;
            Save("failure.json", new { error = error.ToString(), frames = rows.Count, transitions });
            // An unhandled Main exception may terminate before using/finally unwinds.
            WriteCsv();
            window.Close();
        }
    }

    private void Observe(long timestamp, long callbackTicks)
    {
        if (wall.Elapsed.TotalSeconds > 1800) throw new TimeoutException("Replay exceeded 30 minutes.");
        game ??= (Game_Main)GameField.GetValue(null)!;
        var state = (IGameState)StateField.GetValue(game)!;
        string name = state.GetType().Name;
        if (name != lastState)
        {
            var transition = new { seconds = wall.Elapsed.TotalSeconds, name, state.StageName, frames = rows.Count,
                focused = game.Form_Main.Focused, onPause = Field(typeof(Game_Main), "OnPause").GetValue(game) };
            transitions.Add(transition);
            Console.WriteLine(JsonSerializer.Serialize(transition));
            lastState = name;
        }
        if (!started)
        {
            if (name != "GameState_MainMenu" || !game.Form_Main.Focused) return;
            StartViaReplayMenu(state);
            return;
        }
        if (name == "GameState_MusicLoading") { loadingGap = true; return; }
        if ((!backgroundReplayDiagnostic && !game.Form_Main.Focused) || name == "GameState_PauseMenu")
            throw new InvalidOperationException($"Focus/pause interrupted replay at frame {rows.Count}: {name}, focused={game.Form_Main.Focused}.");
        if (state.StageName != targetStage)
            throw new InvalidOperationException($"Unexpected state {name}/{state.StageName}, expected {targetStage}.");
        stage = state.StageData;
        if (!stage.OnReplay) throw new InvalidOperationException("Production stage is not replaying.");
        if (rows.Count == 0) Ready(state);
        Capture(timestamp, callbackTicks);
        long position = stage.Rep.DataPosition;
        if (position == completionRecord.DataPosition)
        {
            Save("recorded-final-offset.json", new { frame = rows.Count, position, actual = Player(stage.MyPlane), expected = completionRecord });
            if (state.StateSwitchData?.NextState is string next && next == completionState)
            {
                VerifyRestored(stage.MyPlane, completionRecord);
                Save("stage-completion.json", new { kind = "recorded-final-state-before-next-stage", position, next, endMarkerConsumed = false });
                Finish(true, state);
                return;
            }
        }
        bool end = position == markerOffset + 2 && state.StateSwitchData?.NextState == "GameOverMenu";
        if (position > markerOffset + 2) throw new InvalidDataException("Read past replay end marker.");
        if (end)
        {
            if (metadata.LastStage == "All" || selectedRecord + 2 < metadata.MyPlaneData.Count)
                throw new InvalidOperationException($"Replay reached GameOver end marker without recorded {targetStage}→{completionState} completion.");
            VerifyRestored(stage.MyPlane, completionRecord);
            Save("stage-completion.json", new { kind = "recorded-terminal-game-over", position, endMarkerConsumed = true });
        }
        if (end || limit > 0 && rows.Count >= limit) Finish(end, state);
    }

    private void StartViaReplayMenu(IGameState state)
    {
        var type = typeof(Game_Main).Assembly.GetType("Shooting.MenuGroup_ReplayStageSelect", true)!;
        var item = new DescriptionMenuItem(state.StageData, "25") { Description = "profile replay" };
        object menu = Activator.CreateInstance(type, state.StageData, new PointF(0, 0), item)!;
        int selection = StageOrdinal(targetStage);
        type.GetProperty("MenuSelectIndex")!.SetValue(menu, selection);
        // The actual menu restores every MyPlaneInfo setter, plane type, rank and key offset.
        type.GetMethod("ProcessZ")!.Invoke(menu, null);
        var change = state.StageData.StateSwitchData ?? throw new InvalidOperationException("Replay menu did not request a stage.");
        if (change.NextState != targetStage || change.SDPswitch == null)
            throw new InvalidOperationException("Replay menu selected incorrect start metadata.");
        initial = Player(change.SDPswitch.MyPlane);
        VerifyRestored(change.SDPswitch.MyPlane, initialRecord);
        if (change.SDPswitch.Difficulty != metadata.Rank || state.StageData.Rep.DataPosition != initialRecord.DataPosition)
            throw new InvalidOperationException("Replay menu restored incorrect rank or key offset.");
        lastLife = change.SDPswitch.MyPlane.Life;
        lastPosition = state.StageData.Rep.DataPosition;
        state.StateSwitchData = change;
        started = true;
        Save("start.json", new { pid = Environment.ProcessId, change.NextState, keyOffset = lastPosition,
            rank = change.SDPswitch.Difficulty.ToString(), playerType = change.SDPswitch.MyPlane.GetType().Name,
            restored = initial, selectedStage = targetStage, selectedRecord, source = initialRecord });
    }

    private void Ready(IGameState state)
    {
        if (Environment.GetEnvironmentVariable("THSSS_PROFILE_OWNERSHIP") == "1")
            OwnershipProbeType().GetMethod("Arm")!.Invoke(null, new object[] { stage! });
        initialCallCounts = callCounterFields.Select(f => (long)f.GetValue(null)!).ToArray();
        string expectedDataRoot = Path.GetFullPath(Environment.GetEnvironmentVariable("THSSS_DATA_ROOT")!);
        if (Path.GetFullPath(Environment.CurrentDirectory) != expectedDataRoot)
            throw new InvalidOperationException("Replay profiler is not running inside its isolated data root");
        stageAllocatedBefore = GC.GetTotalAllocatedBytes(false);
        var gl = SlimDX.Direct3D9.Device.CurrentContext;
        string gpu = gl?.GetStringS(StringName.Renderer) ?? throw new InvalidOperationException("No active OpenGL context.");
        if (new[] { "llvmpipe", "softpipe", "swrast", "software", "swiftshader" }.Any(name => gpu.Contains(name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Hardware OpenGL required, got {gpu}.");
        string compiledMode = typeof(Game_Main).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .SingleOrDefault(a => a.Key == "THSSSMathMode")?.Value ?? "auto";
        string requestedBackend = Environment.GetEnvironmentVariable("THSSS_MATH_BACKEND") ?? "auto";
        string expectedBackend = compiledMode == "software" || requestedBackend == "software" ||
            ((requestedBackend is "auto" or "") && System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture == System.Runtime.InteropServices.Architecture.Arm64)
            ? "software" : "x87";
        var ready = new { pid = Environment.ProcessId, stage = state.StageName,
            selectedRecord, expectedInitialKeyPosition = initialRecord.DataPosition, difficulty = stage!.Difficulty.ToString(),
            dataRoot = expectedDataRoot, currentDirectory = Environment.CurrentDirectory,
            frames = rows.Count, keyPosition = stage!.Rep.DataPosition, markerOffset,
            backend = SlimDX.Compat.LegacyFloat.Backend, compiledMode, requestedBackend, expectedBackend,
            renderer = game!.DeviceMain.RenderBackend.Kind.ToString(),
            gpu = gl?.GetStringS(StringName.Renderer), vendor = gl?.GetStringS(StringName.Vendor),
            glVersion = gl?.GetStringS(StringName.Version), window = window!.Size.ToString(), framebuffer = window.FramebufferSize.ToString(),
            presentInterval = Field(typeof(Game_Main), "presentInterval").GetValue(game),
            fixedFPS = Field(typeof(Game_Main), "fixedFPS").GetValue(game),
            verticalSync = Field(typeof(Game_Main), "verticalSync").GetValue(game),
            backgroundReplayDiagnostic, replayKeyboardIsolated, phaseCountersAvailable = phaseFields != null, phaseCounterNames = phaseFields == null ? null : PhaseNames,
            stopwatchFrequency = Stopwatch.Frequency };
        if (SlimDX.Compat.LegacyFloat.Backend != expectedBackend)
            throw new InvalidOperationException($"Expected {expectedBackend} backend, got {SlimDX.Compat.LegacyFloat.Backend}.");
        Save("ready.json", ready);
        Console.WriteLine("PROFILE_READY " + JsonSerializer.Serialize(ready));
    }

    private void Capture(long timestamp, long callbackTicks)
    {
        if (rows.Count % 600 == 0) Save("progress.json", new { frames = rows.Count, stageTime = stage!.TimeMain, keyPosition = stage.Rep.DataPosition, timestamp });
        var player = stage!.MyPlane;
        if (player.Life < lastLife) lifeDrops += lastLife - player.Life;
        if (player.DeadTime >= 0 && previousDeadTime < 0) deathEntries++;
        bool batch = rows.Count % 60 == 0;
        rows.Add(new Frame(timestamp, lastTimestamp == 0 ? 0 : timestamp - lastTimestamp, callbackTicks, loadingGap,
            stage.TimeMain, stage.TimeMain - lastTime, stage.Rep.DataPosition, stage.Rep.DataPosition - lastPosition,
            stage.BulletList.Count, stage.EnemyPlaneList.Count, stage.ItemList.Count, stage.MyBulletList.Count,
            player.Life, player.DeadTime, batch ? GC.GetTotalAllocatedBytes(false) : -1,
            batch ? GC.CollectionCount(0) : -1, batch ? GC.CollectionCount(1) : -1, batch ? GC.CollectionCount(2) : -1,
            Phase(0), Phase(1), Phase(2), Phase(3), Phase(4), Phase(5)));
        if (stateDigests != null) stateDigests.Add($"{stage.Rep.DataPosition},{GameplayDigest.Capture(stage)}");
        lastTimestamp = timestamp; lastTime = stage.TimeMain; lastPosition = stage.Rep.DataPosition;
        lastLife = player.Life; previousDeadTime = player.DeadTime;
        loadingGap = false;
    }

    private long Phase(int index) => phaseFields == null ? -1 : (long)phaseFields[index].GetValue(null)!;

    private static void VerifyRestored(BaseMyPlane p, MyPlaneInfo m)
    {
        if (p.Life != m.Life || p.Spell != m.Spell || p.Power != m.Power || p.Score != m.Score ||
            p.Graze != m.Graze || p.OriginalPosition.X != m.PosX || p.OriginalPosition.Y != m.PosY ||
            p.LifeChip != m.LifeChip || p.SpellChip != m.SpellChip || p.LifeUpCount != m.LifeUpCount ||
            p.StarPoint != m.StarPoint || p.HighItemScore != m.HighItemScore || p.Rate != m.Rate || p.LastColor != m.LastColor)
            throw new InvalidOperationException("Player state differs from selected replay MyPlaneInfo (including score, graze and exact float fields).");
    }

    private static object Player(BaseMyPlane plane) => new { plane.Life, plane.Spell, plane.Power, plane.Score,
        plane.Graze, plane.OriginalPosition, plane.LifeChip, plane.SpellChip, plane.LifeUpCount,
        plane.StarPoint, plane.HighItemScore, plane.Rate, plane.LastColor };

    private static object Stats(IEnumerable<double> values)
    {
        double[] sorted = values.Order().ToArray();
        if (sorted.Length == 0) return new { count = 0 };
        double Percentile(double p) => sorted[Math.Min(sorted.Length - 1, (int)Math.Ceiling(sorted.Length * p) - 1)];
        return new { count = sorted.Length, meanMs = sorted.Average(), p50 = Percentile(.5), p95 = Percentile(.95),
            p99 = Percentile(.99), max = sorted[^1], over16_67 = sorted.Count(x => x > 16.66667),
            over20 = sorted.Count(x => x > 20), over25 = sorted.Count(x => x > 25), over33 = sorted.Count(x => x > 33) };
    }

    private void Finish(bool end, IGameState state)
    {
        if (Environment.GetEnvironmentVariable("THSSS_PROFILE_OWNERSHIP") == "1")
            OwnershipProbeType().GetMethod("SaveReport")!.Invoke(null, new object[] { Path.Combine(output, "ownership-report.json") });
        if (initialCallCounts != null && callCounterFields.Length > 0)
            Save("call-counts.json", callCounterFields.Select((f, i) => new { name = f.Name, count = (long)f.GetValue(null)! - initialCallCounts[i] }));
        double milliseconds = 1000.0 / Stopwatch.Frequency;
        var measured = rows.Skip(120).Where(x => !x.LoadingGap).ToArray();
        Save("summary.json", new { backgroundReplayDiagnostic, replayKeyboardIsolated, recordedStageCompleted = end, endMarkerConsumed = lastPosition == markerOffset + 2, captured = rows.Count, markerOffset, finalKeyPosition = lastPosition,
            elapsedSeconds = wall.Elapsed.TotalSeconds, lifeDrops, deathEntries, initial, final = Player(stage!.MyPlane),
            selectedStage = targetStage, selectedRecord, expectedFinal = completionRecord, pendingState = state.StateSwitchData?.NextState,
            stageTime = stage.TimeMain, warmupFrames = Math.Min(120, rows.Count),
            interval = Stats(measured.Select(x => x.IntervalTicks * milliseconds)),
            callbackIncludingPacing = Stats(measured.Select(x => x.CallbackTicks * milliseconds)),
            warmup = Stats(rows.Take(120).Skip(1).Where(x => !x.LoadingGap).Select(x => x.IntervalTicks * milliseconds)),
            loadingGaps = Stats(rows.Where(x => x.LoadingGap).Select(x => x.IntervalTicks * milliseconds)),
            stageAllocationBytes = GC.GetTotalAllocatedBytes(false) - stageAllocatedBefore,
            allocationBytes = GC.GetTotalAllocatedBytes(false) - allocatedBefore,
            collections = Enumerable.Range(0, 3).Select(i => GC.CollectionCount(i) - gcBefore[i]), transitions });
        WriteCsv();
        stage.Rep.CloseRpy();
        completed = true;
        window!.Close();
    }

    private void WriteCsv()
    {
        if (stateDigests != null) File.WriteAllLines(Path.Combine(output, "state-digests.csv"), stateDigests);
        using var writer = new StreamWriter(Path.Combine(output, "frames.csv"));
        writer.WriteLine("frame,timestamp,interval_ticks,production_callback_ticks,loading_gap,stage_time,time_delta,key_position,key_bytes,bullets,enemies,items,my_bullets,life,dead_time,allocated_batch,gen0,gen1,gen2,update_ticks,draw_ticks,present_ticks,pacing_ticks,main_ticks,instrumented_callback_ticks");
        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            writer.WriteLine($"{i},{r.Timestamp},{r.IntervalTicks},{r.CallbackTicks},{(r.LoadingGap ? 1 : 0)},{r.StageTime},{r.TimeDelta},{r.KeyPosition},{r.KeyBytes},{r.Bullets},{r.Enemies},{r.Items},{r.MyBullets},{r.Life},{r.DeadTime},{r.AllocatedBatch},{r.Gen0},{r.Gen1},{r.Gen2},{r.UpdateTicks},{r.DrawTicks},{r.PresentTicks},{r.PacingTicks},{r.MainTicks},{r.InstrumentedCallbackTicks}");
        }
    }

    private void Save(string name, object value) => File.WriteAllText(Path.Combine(output, name), JsonSerializer.Serialize(value, Json));

    public void VerifyCompleted()
    {
        if (failure != null) throw new InvalidOperationException($"Replay profile failed; evidence {output}", failure);
        if (!completed || Environment.ExitCode != 0)
            throw new InvalidOperationException($"Production exited before profile completion ({rows.Count} frames), exit={Environment.ExitCode}.");
    }

    public void Dispose()
    {
        if (!completed && rows.Count > 0) WriteCsv();
        game?.GlobalData.Rep?.CloseRpy();
    }
}
