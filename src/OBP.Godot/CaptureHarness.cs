using System.Text.Json;
using Godot;

namespace OBP.Godot;

/// <summary>
/// The deterministic screenshot primitive: settle a fixed number of frames, wait
/// one <c>FramePostDraw</c>, grab the viewport, and write a PNG plus a JSON
/// sidecar of whatever metadata the caller supplies. A watchdog quits the process
/// if a grab wedges. Shared by the single <c>--capture-frame</c> path and the
/// <c>--shots</c> list runner.
/// </summary>
public static class CaptureHarness
{
    public readonly record struct Result(string PngPath, string JsonPath, int Width, int Height, Error SaveResult)
    {
        public bool Ok => SaveResult == Error.Ok;
    }

    /// <summary>
    /// Capture the current frame of <paramref name="node"/>'s viewport after
    /// <paramref name="settleFrames"/> render frames. <paramref name="metadata"/>
    /// is serialised next to the PNG; <c>width</c>/<c>height</c>/<c>savePngResult</c>
    /// are merged in.
    /// </summary>
    public static System.Threading.Tasks.Task<Result> CaptureAsync(
        Node node, string outPath, int settleFrames,
        System.Collections.Generic.IDictionary<string, object?> metadata, CancellationToken cancellationToken = default) =>
        CaptureAsync(node, outPath, settleFrames, () => metadata, cancellationToken);

    public static async System.Threading.Tasks.Task<Result> CaptureAsync(
        Node node, string outPath, int settleFrames,
        System.Func<System.Collections.Generic.IDictionary<string, object?>> metadataFactory, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var tree = node.GetTree();
        double seconds = System.Math.Max(0.25, settleFrames / 60.0);

        // Guard so a leftover watchdog from an already-finished capture (e.g. an
        // earlier shot in a --shots run) can't later Quit(2) over the real exit.
        bool finished = false;
        var watchdog = tree.CreateTimer(seconds + 20.0);
        void Watchdog()
        {
            if (finished || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            GD.PrintErr("[CaptureHarness] watchdog fired — quitting");
            ApplicationLifecycle.RequestQuit(tree, "capture-watchdog", 2);
        }
        watchdog.Timeout += Watchdog;

        try
        {
            await GodotAsyncWait.SignalAsync(tree.CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout, cancellationToken);
            await GodotAsyncWait.SignalAsync(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            var metadata = metadataFactory();
            var image = node.GetViewport().GetTexture().GetImage();
            string pngPath = outPath.StartsWith("res://") || outPath.StartsWith("user://")
                ? ProjectSettings.GlobalizePath(outPath)
                : System.IO.Path.GetFullPath(outPath);
            string jsonPath = System.IO.Path.ChangeExtension(pngPath, ".json");
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(pngPath)!);

            Error err = image.SavePng(pngPath);

            metadata["width"] = image.GetWidth();
            metadata["height"] = image.GetHeight();
            metadata["savePngResult"] = err.ToString();
            System.IO.File.WriteAllText(jsonPath, JsonSerializer.Serialize(metadata, JsonOptions));

            GD.Print($"[CaptureHarness] {pngPath} ({image.GetWidth()}x{image.GetHeight()}, {err}) + {System.IO.Path.GetFileName(jsonPath)}");
            return new Result(pngPath, jsonPath, image.GetWidth(), image.GetHeight(), err);
        }
        finally
        {
            finished = true;
            if (GodotObject.IsInstanceValid(watchdog)) watchdog.Timeout -= Watchdog;
        }
    }

    public static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string ActiveRenderer() =>
        RenderingServer.GetRenderingDevice() is null ? "gl_compatibility" : "rendering_device";
}
