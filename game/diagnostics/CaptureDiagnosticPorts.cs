using System.Collections.Immutable;
using System.Numerics;
using OBP.Runtime.Player;

namespace OBP.Game.Diagnostics;

public enum CaptureKey { W, A, S, D, Q, E, Space, F7, F8, F9, F12 }
public enum CaptureKeyOrigin { Both, Logical, Physical }
public enum CaptureButton { A, B, X, Y, Start, Back, Down, Right, Up }
public enum CaptureAxis { LeftY, RightX }
public interface ICaptureDiagnosticInput : IDisposable
{
    void Key(CaptureKey key, bool pressed, CaptureKeyOrigin origin = CaptureKeyOrigin.Both, bool shift = false, char? unicode = null);
    void Pad(CaptureButton button, bool pressed);
    void Axis(CaptureAxis axis, float value);
    void Mouse(bool pressed, Vector2 position);
}
public interface ICaptureDiagnosticInputFactory { ICaptureDiagnosticInput Acquire(); }
public interface ICaptureDiagnosticTime { ulong Milliseconds { get; } Task DelayAsync(int milliseconds, CancellationToken token); }
public sealed record CaptureRuntimeDiagnostic
{
    public bool RecorderPresent { get; init; }
    public bool WaitingForEntry { get; init; }
    public bool PreparingWorld { get; init; }
    public bool Finished { get; init; }
    public bool PlayerPresent { get; init; }
    public int? LevelId { get; init; }
    public string? Destination { get; init; }
    public PlayerSessionConfiguration? Configuration { get; init; }
    public string ProfileJson { get; init; } = "";
    public bool GenuineAvatar { get; init; }
    public bool NativeProfile { get; init; }
    public int PhysicsTicksPerSecond { get; init; }
    public bool HudHealth { get; init; }
    public bool HudBolts { get; init; }
    public bool HudVisible { get; init; }
    public bool QuickSelectOpen { get; init; }
    public bool PauseOpen { get; init; }
    public bool TreePaused { get; init; }
    public long PhysicsTick { get; init; }
    public ulong? PlayerNode { get; init; }
    public int Shots { get; init; }
}
public interface ICaptureBookmarkDiagnostic { bool Exists { get; } string? ReadDestination(); }
public interface ICaptureRecorderDiagnosticFixture
{
    CaptureRuntimeDiagnostic Read();
    void ResizeWindow(int width, int height);
    ICaptureBookmarkDiagnostic RequestBookmark(int visit, string destination);
    void Save();
}
internal sealed class CaptureEquipmentActions(Func<int, string, bool> assign, Func<string, bool> select) : ICaptureEquipmentActions
{
    public bool AssignQuickSelect(int slot, string item) => assign(slot, item);
    public bool Select(string item) => select(item);
}
internal static class CaptureDiagnosticIsolation
{
    internal static bool Isolated(string? path, string captures)
    {
        if (path is null) return true;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        string full = Path.GetFullPath(path);
        return new[] { captures, Path.GetTempPath() }.Any(root => full.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar, comparison));
    }
}
public sealed record CaptureDiagnosticContext(DiagnosticRunContext Run, ICaptureDiagnosticInput Input,
    ICaptureDiagnosticTime Time, Action<string> Log);
public abstract class CaptureScenarioSupport(CaptureDiagnosticContext context)
{
    protected CaptureDiagnosticContext Context { get; } = context;
    protected CancellationToken Lifetime => Context.Run.CancellationToken;
    protected Task Physics(int count) => Context.Run.WaitAsync(DiagnosticFramePhase.Physics, count);
    protected Task Frames(int count = 1) => Context.Run.WaitAsync(DiagnosticFramePhase.Process, count);
    protected void Key(CaptureKey key, bool pressed, CaptureKeyOrigin origin = CaptureKeyOrigin.Both, bool shift = false) => Context.Input.Key(key, pressed, origin, shift);
    protected void Pad(CaptureButton button, bool pressed) => Context.Input.Pad(button, pressed);
    protected void Axis(CaptureAxis axis, float value) => Context.Input.Axis(axis, value);
    protected void Log(string text) => Context.Log(text);
    protected static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    protected async Task WaitProcess(Func<bool> condition, string failure, int maximumMilliseconds = 5000)
    {
        ulong deadline = Context.Time.Milliseconds + (ulong)maximumMilliseconds;
        while (!condition() && Context.Time.Milliseconds < deadline) await Frames();
        Require(condition(), failure);
    }
}
