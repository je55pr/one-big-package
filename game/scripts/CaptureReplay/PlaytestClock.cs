using Godot;

namespace OBP.Game;

/// <summary>Recorded host gesture time. Ordinary sessions still use the live monotonic clock.</summary>
internal static class PlaytestClock
{
    internal static bool Active { get; set; }
    internal static ulong RecordedMilliseconds { get; set; }
    internal static ulong Milliseconds => Active ? RecordedMilliseconds : Time.GetTicksMsec();
}
