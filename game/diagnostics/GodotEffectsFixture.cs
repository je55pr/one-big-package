using System.Collections.Immutable;
using Godot;
using OBP.Core;
using OBP.Fusion;
using OBP.Godot;

namespace OBP.Game.Diagnostics;

internal sealed class GodotEffectsFixture(Node owner, Func<WorldEffectHost?> current, Func<Node3D?> worldRoot,
    Func<string> protectedState, string captureDirectory, Func<string?>? unavailable = null) : IDiagnosticEffectsFixture
{
    private WorldEffectHost? _retired;
    private EffectHandle[] _oldHandles = [];
    private byte[]? _baseline;
    private Camera3D? _camera;
    public bool Ready
    {
        get
        {
            var host = current();
            if (host is null && unavailable?.Invoke() is { } reason) throw new DiagnosticUnavailableException(reason);
            host?.Prepare();
            return host?.Ready == true && !ReferenceEquals(host, _retired);
        }
    }
    public string Status => current()?.Status ?? "World unavailable.";
    public int RenderedParticles => current()?.RenderedParticles ?? 0;
    public int TextureCount => current()?.TextureCount ?? 0;
    public int BatchCount => current()?.BatchCount ?? 0;
    public ImmutableArray<EffectObservation> Read() => current()?.Read() ?? [];
    public string ProtectedState() => protectedState();
    public void Start() => RequireControl("start");
    public void Reset() => RequireControl("reset");
    public void StopAll() { foreach (var handle in current()!.Handles) current()!.Stop(handle); }
    public bool Rejects(EffectHandle handle) => current()?.Stop(handle) != true;
    private void RequireControl(string operation)
    {
        var result = current()?.Control(operation) ?? new(false, "No active effects world.");
        if (!result.Accepted) throw new DiagnosticUnavailableException(result.Reason);
    }
    public IDisposable Camera()
    {
        var root = worldRoot() ?? throw new DiagnosticUnavailableException("The effect world was retired.");
        var previous = owner.GetViewport().GetCamera3D();
        var center = current()!.PresentationCenter; var target = RuntimeWorldScene.ToScene(center.X, center.Y + .8f, center.Z);
        var camera = new Camera3D { Name = "EffectsAcceptanceCamera", Fov = 75, Near = .1f, Far = 500 };
        root.AddChild(camera); camera.GlobalPosition = target + new Vector3(0, 2.7f, -6); camera.LookAt(target, Vector3.Up); camera.Current = true; _camera = camera;
        return new Cleanup(() =>
        {
            if (GodotObject.IsInstanceValid(previous)) previous.Current = true;
            if (GodotObject.IsInstanceValid(camera)) camera.QueueFree(); _camera = null;
        });
    }
    public void Baseline()
    {
        using var image = owner.GetViewport().GetTexture().GetImage(); image.Convert(Image.Format.Rgba8); _baseline = image.GetData();
    }
    public EffectImageEvidence Capture(string name)
    {
        if (_camera is null || _baseline is null) throw new InvalidOperationException("Effect image capture lacks its owned camera/baseline.");
        using var image = owner.GetViewport().GetTexture().GetImage(); image.Convert(Image.Format.Rgba8);
        int width = image.GetWidth(), height = image.GetHeight(); byte[] data = image.GetData();
        if (_baseline.Length != data.Length) throw new InvalidOperationException("The render size changed during effect capture.");
        Directory.CreateDirectory(captureDirectory); image.SavePng(Path.Combine(captureDirectory, name));
        var groups = Read().ToDictionary(e => e.Handle, e => e.Recipe.Source.Game);
        var changes = new Dictionary<string, int>();
        foreach (var group in current()!.ReadDraw().SelectMany(d => d.Frame.Sprites.Select(s => (Game: groups[d.Handle], Sprite: s))).GroupBy(p => p.Game))
        {
            var points = group.Select(p => _camera.UnprojectPosition(RuntimeWorldScene.ToScene(p.Sprite.Position.X, p.Sprite.Position.Y, p.Sprite.Position.Z))).ToArray();
            int left = Math.Clamp((int)points.Min(p => p.X) - 40, 0, width - 1), right = Math.Clamp((int)points.Max(p => p.X) + 40, 0, width - 1);
            int top = Math.Clamp((int)points.Min(p => p.Y) - 70, 0, height - 1), bottom = Math.Clamp((int)points.Max(p => p.Y) + 70, 0, height - 1), changed = 0;
            for (int y = top; y <= bottom; y++) for (int x = left; x <= right; x++)
            {
                int at = (y * width + x) * 4;
                if (Math.Abs(data[at] - _baseline[at]) + Math.Abs(data[at + 1] - _baseline[at + 1]) + Math.Abs(data[at + 2] - _baseline[at + 2]) > 12) changed++;
            }
            if (changed < 100) throw new InvalidOperationException($"Insufficient visible rendered pixel evidence for {group.Key} in {name}: {changed} changed pixels.");
            changes.Add(SourceIdentityWire.FormatSource(group.Key), changed);
        }
        if (changes.Count != 3) throw new InvalidOperationException("The rendered capture did not contain all three source families.");
        return new(name, width, height, changes);
    }
    public void RememberWorld() { _retired = current(); _oldHandles = _retired?.Handles ?? []; }
    public bool RetiredWorldClean => _retired is { IsDisposed: true, LiveInstances: 0, TextureCount: 0, BatchCount: 0 } retired &&
        _oldHandles.All(handle => !retired.Stop(handle) && Rejects(handle));
    private sealed class Cleanup(Action action) : IDisposable { public void Dispose() => action(); }
}

internal sealed class EffectDiagnosticNavigation(Func<string, bool> enter) : IDiagnosticNavigationActions
{
    public Task<DiagnosticActionResult> NavigateAsync(string destinationId, CancellationToken cancellationToken)
    { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(new DiagnosticActionResult(enter(destinationId), null)); }
}
