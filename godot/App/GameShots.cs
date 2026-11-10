// --shot: pictures of the running game (Game is partial: App/Game.cs lists the parts).
//   --shot=path.png --frames=N    one picture after N frames, then quit
//   --shot-at=5,10                one picture per listed tick (path-5.png, path-10.png): the sim only moves to the next tick listed
//   --shot-scale=F --shot-width=W smaller pictures (a fraction, or W pixels wide)
// Frames are counted on the tree's signal, not in _Process, so a frame that throws still gets its picture and the
// run still quits (with exit code 1).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Godot;
using LoonieRoo.Game.Render;
using LoonieRoo.Game.Ui;
using LoonieRoo.Sim;
using static LoonieRoo.Game.Render.CameraMath;
using Time = LoonieRoo.Sim.Time;

namespace LoonieRoo.Game;

public partial class Game
{
    /// <summary>--shot=path.png: where the picture goes; --frames=N: after how many frames.</summary>
    string? shotPath;
    int shotFrames = 30;

    /// <summary>--shot: frames counted on the tree's signal (it comes before _Process each frame), so the picture is
    /// taken on time whatever the game's code does. Frames whose _Process didn't get to the end (it threw) are counted,
    /// and make the exit code 1.</summary>
    void ShotFrame()
    {
        if (shotFrameCount > 0 && frame == lastProcessed) shotBroken++;
        lastProcessed = frame;
        ++shotFrameCount;
        if (shotAt != null) { ShotAtFrame(); return; }
        if (shotFrameCount == shotFrames) _ = SaveShot();
    }
    int shotFrameCount, lastProcessed = -1, shotBroken;

    async System.Threading.Tasks.Task SaveShot()
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        bool ok = WriteShot(shotPath!);
        Finish(ok && shotBroken == 0 ? 0 : 1);
    }

    // --- --shot-at, --shot-scale, --shot-width: several pictures from one launch, smaller ones ---

    /// <summary>--shot-at: the ticks to take a picture at (ascending), and how many are done.</summary>
    List<int>? shotAt;
    int shotAtDone;
    /// <summary>The frame count when the sim got to the tick being waited for (-1: not yet), and frames to wait there so the picture catches up.</summary>
    int shotReached = -1;
    const int SHOT_SETTLE_FRAMES = 3;
    bool shotCapturing, shotAllOk = true;
    double? shotScale;
    int? shotWidth;

    void ParseShotOptions(Dictionary<string, string> args)
    {
        if (args.TryGetValue("shot-at", out var at))
        {
            var ticks = at.Split(',', StringSplitOptions.RemoveEmptyEntries).Select((x) => int.Parse(x, CultureInfo.InvariantCulture)).Distinct().OrderBy((x) => x).ToList();
            if (shotPath == null) GD.Print("--shot-at needs --shot=out.png (the pictures are out-<tick>.png)");
            else if (ticks.Count > 0) shotAt = ticks;
        }
        if (args.TryGetValue("shot-scale", out var sc)) shotScale = double.Parse(sc, CultureInfo.InvariantCulture);
        if (args.TryGetValue("shot-width", out var sw)) shotWidth = int.Parse(sw, CultureInfo.InvariantCulture);
    }

    /// <summary>--shot-at: each frame, run ticks (a few at a time, one by one) towards the next listed tick. Nothing else moves the sim.</summary>
    void AdvanceToShotTick()
    {
        if (shotCapturing || shotAtDone >= shotAt!.Count) return;
        for (int i = 0; i < 20 && state.tick < shotAt[shotAtDone]; i++) StepOne();
    }

    /// <summary>On the frame signal: at the listed tick, wait out the first --frames and a few frames for the picture to catch up, then take it.</summary>
    void ShotAtFrame()
    {
        if (shotCapturing || shotAtDone >= shotAt!.Count) return;
        if (state.tick < shotAt[shotAtDone]) { shotReached = -1; return; }
        if (shotReached < 0) shotReached = shotFrameCount;
        if (shotFrameCount >= shotFrames && shotFrameCount - shotReached >= SHOT_SETTLE_FRAMES) { shotCapturing = true; _ = SaveShotAt(shotAt[shotAtDone]); }
    }

    async System.Threading.Tasks.Task SaveShotAt(int tick)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        string ext = System.IO.Path.GetExtension(shotPath!);
        shotAllOk &= WriteShot(shotPath![..^ext.Length] + "-" + tick + (ext == "" ? ".png" : ext));
        shotAtDone++;
        shotReached = -1;
        shotCapturing = false;
        if (shotAtDone >= shotAt!.Count) Finish(shotAllOk && shotBroken == 0 ? 0 : 1);
    }

    /// <summary>Save the viewport as a picture, scaled down if --shot-scale or --shot-width say so. Says where, how big, and how many bytes.</summary>
    bool WriteShot(string path)
    {
        if (DisplayServer.GetName() == "headless") { GD.Print("shot failed: --headless draws nothing; run without it (the text options work headless)"); return false; }
        var img = GetViewport().GetTexture().GetImage();
        int w = img.GetWidth(), h = img.GetHeight();
        double f = shotWidth is { } sw ? (double)sw / w : shotScale ?? 1;
        if (f > 0 && f < 1)
        {
            w = Math.Max(1, (int)Math.Round(w * f));
            h = Math.Max(1, (int)Math.Round(h * f));
            img.Resize(w, h, Image.Interpolation.Lanczos);
        }
        var err = img.SavePng(path);
        long bytes = err == Error.Ok ? new System.IO.FileInfo(path).Length : 0;
        GD.Print(err == Error.Ok ? $"shot: {path} {w}x{h} {bytes / 1024} KB" : $"shot failed: {err}");
        if (shotBroken > 0) GD.Print($"shot: {shotBroken} of {shotFrameCount} frames threw in _Process (the exception is above)");
        return err == Error.Ok;
    }

    /// <summary>The end of a --shot run: the text options' final map, then quit with the exit code.</summary>
    void Finish(int code)
    {
        TextFinish();
        GetTree().Quit(code);
    }
}
