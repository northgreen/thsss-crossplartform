using System.Diagnostics;
using System.Text.Json;
using Shooting;
using Silk.NET.Input;
using Silk.NET.Windowing;

internal static class Entry
{
    public static int Main(string[] args)
    {
        StateTrace.VerifyGameplaySensitivity();
        if (args.Length != 3 || args[0] is not ("record" or "playback") ||
            !int.TryParse(args[2], out int frames) || frames < 200 || frames > 18000)
            throw new ArgumentException("Usage: GameplayRuntime record|playback ABSOLUTE_OUTPUT_DIRECTORY frames(200..18000)");
        using var run = new LiveRun(args[0] == "playback", Path.GetFullPath(args[1]), frames);
        var setup = new Thread(run.Subscribe) { IsBackground = true, Name = "HarnessRenderSubscription" };
        setup.Start();
        Shooting.Program.Main(Array.Empty<string>());
        setup.Join(TimeSpan.FromSeconds(2));
        run.VerifyCompleted();
        return Environment.ExitCode;
    }
}

internal sealed class LiveRun : IDisposable
{
    private static readonly Key[] Controls = { Key.Z, Key.X, Key.Left, Key.Right, Key.Up, Key.Down, Key.ShiftLeft, Key.Escape };
    private readonly bool playback;
    private readonly string output;
    private readonly int target;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly StreamWriter trace;
    private readonly List<double> intervals = new();
    private readonly HashSet<int> playerPositions = new();
    private IWindow? window;
    private Game_Main? game;
    private StageDataPackage? stage;
    private WindowInputBridge? input;
    private Exception? failure;
    private bool requested, completed, pauseRequested, resumed;
    private int captured, pausedFrames, maxEntities, maxMyBullets;
    private string? frozen;
    private double lastStageAt;
    private double nextDiagnosticAt;
    private string? lastState;

    public LiveRun(bool playback, string output, int target)
    {
        this.playback = playback; this.output = output; this.target = target;
        Directory.CreateDirectory(output);
        trace = new StreamWriter(Path.Combine(output, playback ? "playback.jsonl" : "record.jsonl"));
    }

    public void Subscribe()
    {
        try
        {
            while (clock.Elapsed < TimeSpan.FromSeconds(45))
            {
                window = (IWindow?)StateTrace.Field(typeof(Shooting.Program), "_window").GetValue(null);
                // Load must have published _game: OnRender was registered before Load ran.
                if (window != null && StateTrace.Field(typeof(Shooting.Program), "_game").GetValue(null) != null)
                {
                    window.Render += AfterRender;
                    return;
                }
                Thread.Sleep(2);
            }
            throw new TimeoutException("Production window/game was not published within 45 seconds.");
        }
        catch (Exception error) { failure = error; }
    }

    private void AfterRender(double delta)
    {
        if (completed || failure != null || window!.IsClosing) return;
        try { Tick(); }
        catch (Exception error)
        {
            failure = error;
            game?.GlobalData.Rep?.CloseRpy();
            window.Close();
        }
    }

    private void Tick()
    {
        if (clock.Elapsed > TimeSpan.FromSeconds(240)) throw new TimeoutException($"Live run timed out at capture {captured}.");
        game ??= (Game_Main)StateTrace.Field(typeof(Shooting.Program), "_game").GetValue(null)!;
        input ??= (WindowInputBridge?)StateTrace.Field(typeof(Shooting.Program), "_inputBridge").GetValue(null);
        if (input is null) return;
        // A bare Xvfb server has no window manager to assign initial keyboard focus.
        if (!game.Form_Main.Focused) window!.Focus();
        input.SetFocus(true);
        var state = (IGameState)StateTrace.Field(typeof(Game_Main), "PresentState").GetValue(game)!;
        string name = state.GetType().Name;
        if (name != lastState || clock.Elapsed.TotalSeconds >= nextDiagnosticAt)
        {
            var loading = (Thread?)StateTrace.Field(typeof(Game_Main), "LoadingThread").GetValue(game);
            Console.WriteLine($"[LIVE] seconds={clock.Elapsed.TotalSeconds:F2} state={name}/{state.StageName} capture={captured} requested={requested} focused={game.Form_Main.Focused} OnPause={StateTrace.Field(typeof(Game_Main), "OnPause").GetValue(game)} loading={loading?.ThreadState} visible={window!.IsVisible} windowState={window.WindowState}");
            lastState = name;
            nextDiagnosticAt = clock.Elapsed.TotalSeconds + 5;
        }
        if (!requested)
        {
            if (name != "GameState_MainMenu") return;
            if (!game.Form_Main.Focused) return;
            StartStage(state);
            return;
        }
        if (name == "GameState_PauseMenu") { PauseFrame(); return; }
        if (state.StageName != "St1")
        {
            if (name == "GameState_MusicLoading") return;
            throw new InvalidOperationException($"Unexpected state {name}/{state.StageName} at capture {captured}.");
        }
        stage = state.StageData;
        if (pausedFrames > 0 && !resumed)
        {
            resumed = true;
            SetControls();
        }
        Capture();
        if (captured == target) { Finish(); return; }
        if (playback) return;
        if (captured == 150 && !pauseRequested)
        {
            pauseRequested = true;
            // Keep ordinary gameplay input for this update; ESC is outside the replay key word.
            Script(captured);
            input.PublishKey(null!, Key.Escape, true);
            return;
        }
        if (pauseRequested && pausedFrames == 0)
        {
            input.PublishKey(null!, Key.Escape, false);
            return;
        }
        Script(captured);
    }

    private void StartStage(IGameState menu)
    {
        StateTrace.Field(typeof(Game_Main), "presentInterval").SetValue(game, 1);
        menu.GlobalData.Rep = new Replay();
        if (playback) menu.GlobalData.Rep.LoadRpy(Path.Combine(output, "keys.rpy"));
        else menu.GlobalData.Rep.CreatRpy();
        // The shipped menu selects Reimu; BaseStage's unused Aya fallback lacks Aya00 assets.
        var stageData = new StageDataPackage(menu.GlobalData) { OnReplay = playback, Difficulty = DifficultLevel.Normal };
        stageData.MyPlane = new MyPlane_Reimu(stageData, new System.Drawing.Point(192, 398));
        menu.StateSwitchData = new StateSwitchDataPackage
        {
            NextState = "St1", NeedInit = true, SDPswitch = stageData
        };
        requested = true;
        SetControls();
    }

    private void Capture()
    {
        var values = StateTrace.Capture(stage!);
        double now = clock.Elapsed.TotalMilliseconds;
        if (lastStageAt > 0 && (pausedFrames == 0 || resumed)) intervals.Add(now - lastStageAt);
        lastStageAt = now;
        trace.WriteLine(JsonSerializer.Serialize(new { step = captured, digest = StateTrace.Digest(values), state = values }));
        trace.Flush();
        playerPositions.Add(BitConverter.SingleToInt32Bits(stage!.MyPlane.OriginalPosition.X));
        maxEntities = Math.Max(maxEntities, stage.BulletList.Count + stage.EnemyPlaneList.Count + stage.ItemList.Count);
        maxMyBullets = Math.Max(maxMyBullets, stage.MyBulletList.Count);
        captured++;
    }

    private void PauseFrame()
    {
        if (playback || !pauseRequested || resumed || stage is null)
            throw new InvalidOperationException($"Unrequested pause at capture {captured}: nativeFocused={game!.Form_Main.Focused}, OnPause={StateTrace.Field(typeof(Game_Main), "OnPause").GetValue(game)}, ESC={game.GlobalData.KClass.Key_ESC}, replayPosition={game.GlobalData.Rep.DataPosition}.");
        string current = StateTrace.Digest(StateTrace.Capture(stage));
        frozen ??= current;
        if (current != frozen) throw new InvalidOperationException($"Gameplay state changed during pause menu frame {pausedFrames}.");
        pausedFrames++;
        SetControls(pausedFrames >= 35 && pausedFrames < 39 ? new[] { Key.Z } : Array.Empty<Key>());
        lastStageAt = 0;
        if (pausedFrames > 150) throw new InvalidOperationException("Continue input did not resume the real pause menu.");
    }

    private void Script(int step)
    {
        var keys = new List<Key>();
        if (step % 16 < 8) keys.Add(Key.Z);
        if (step % 120 < 60) keys.Add(Key.Left); else keys.Add(Key.Right);
        if (step % 80 < 40) keys.Add(Key.ShiftLeft);
        SetControls(keys.ToArray());
    }

    private void SetControls(params Key[] keys)
    {
        foreach (Key key in Controls) input!.PublishKey(null!, key, keys.Contains(key));
    }

    private void Finish()
    {
        game!.GlobalData.Rep.CloseRpy();
        if (!playback) File.Copy(Path.Combine(Environment.CurrentDirectory, "Replay", "AutoSave.rpy"), Path.Combine(output, "keys.rpy"), true);
        if (maxEntities == 0 || maxMyBullets == 0 || playerPositions.Count < 2)
            throw new InvalidOperationException($"Gameplay/input coverage missing: entities={maxEntities}, mybullets={maxMyBullets}, positions={playerPositions.Count}.");
        if (!playback && (!resumed || pausedFrames < 30)) throw new InvalidOperationException("Pause/resume coverage missing.");
        intervals.Sort();
        var summary = new
        {
            backend = SlimDX.Compat.LegacyFloat.Backend,
            mode = playback ? "playback" : "record", captured, maxEntities, maxMyBullets,
            distinctPlayerX = playerPositions.Count, pausedFrames, resumed,
            elapsedSeconds = clock.Elapsed.TotalSeconds,
            medianFrameMs = intervals[intervals.Count / 2], p95FrameMs = intervals[(int)(intervals.Count * .95)],
            maxFrameMs = intervals[^1], meanFrameMs = intervals.Average(),
            effectiveFps = 1000.0 / intervals.Average(), measuredIntervals = intervals.Count
        };
        File.WriteAllText(Path.Combine(output, playback ? "playback-summary.json" : "record-summary.json"), JsonSerializer.Serialize(summary));
        completed = true;
        window!.Close();
    }

    public void VerifyCompleted()
    {
        if (failure != null) throw new InvalidOperationException($"Live harness failed after {captured} captures; trace: {output}", failure);
        if (!completed || Environment.ExitCode != 0)
            throw new InvalidOperationException($"Production exited before full cycle: captured={captured}/{target}, exit={Environment.ExitCode}; trace: {output}");
    }

    public void Dispose() => trace.Dispose();
}
