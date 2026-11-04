using Godot;
using OBP.Godot;
using OBP.Fusion;
using OBP.Runtime.Presentation;

namespace OBP.Game.Diagnostics;

/// <summary>Source-free engine proof of the screenshot adapter's post-draw metadata phase and bad-output propagation.</summary>
public partial class CliToolEngineSmoke : Node3D
{
    private readonly HostOperationScope _hostOperations = new();
    public override void _Ready() => _hostOperations.Start("cli-tool-engine-proof", RunAsync, lifetime: CancellationToken.None);
    public override void _ExitTree() => _hostOperations.Dispose();
    private async Task RunAsync(CancellationToken token)
    {
        try
        {
            GD.Print("[cli-tool-engine-smoke] started");
            var args = CommandLineArgs.Parse(OS.GetCmdlineUserArgs());
            string output = args.ShotsOut ?? throw new ArgumentException("Choose an isolated --shots-out directory.");
            var lab = FusionLabDefinition.Parse(File.ReadAllText(ProjectSettings.GlobalizePath("res://data/fusion_lab.json")));
            var world = FusionLabWorldFactory.Build(lab);
            var camera = new Camera3D(); AddChild(camera);
            AddChild(new MeshInstance3D { Mesh = new BoxMesh(), Position = new(0, 0, -5) });
            ulong? drawnFrame = null; int reads = 0;
            void Drawn() => drawnFrame = Engine.GetProcessFrames();
            RenderingServer.FramePostDraw += Drawn;
            try
            {
                var fixture = new GodotShotListCaptureFixture(this, camera, _ => { }, _ => { }, () => world,
                    _ => { }, _ => { }, () => { }, () =>
                    {
                        Require(drawnFrame == Engine.GetProcessFrames(), "metadata was sampled before this frame's post-draw signal");
                        reads++;
                        return new Dictionary<string, object?> { ["postDrawFrame"] = drawnFrame };
                    });
                fixture.Apply(new("phase", ShotCamera.TopDown, SettleFrames: 1));
                Require(reads == 0, "camera application eagerly sampled capture metadata");
                string png = Path.Combine(output, "postdraw.png");
                Require(await fixture.CaptureAsync(png, 1, fixture.ReadMetadata, token), "capture did not save");
                Require(reads == 1 && File.Exists(png) && File.Exists(Path.ChangeExtension(png, ".json")), "capture outputs/metadata were missing");
                string blocked = Path.Combine(output, "blocked"); File.WriteAllText(blocked, "file");
                bool rejected = false;
                try { await fixture.CaptureAsync(Path.Combine(blocked, "child.png"), 1, fixture.ReadMetadata, token); }
                catch (IOException) { rejected = true; }
                Require(rejected, "bad capture path was admitted");
                GD.Print("[cli-tool-engine-smoke] PASS: post-draw metadata, PNG/sidecar and bad-output rejection");
            }
            finally { RenderingServer.FramePostDraw -= Drawn; }
            GetTree().Quit(0);
        }
        catch (OperationCanceledException error) when (error.CancellationToken == token && token.IsCancellationRequested) { }
        catch (Exception error)
        {
            GD.PrintErr("[cli-tool-engine-smoke] FAIL: " + error);
            GetTree().Quit(1);
        }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
