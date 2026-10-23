using System.Numerics;

namespace OBP.RAC1.Presentation;

/// <summary>
/// Native page transition playback for all 14 original class-0x472 panels.
/// Recovered FUN_002192a8 sets the next animation at frame 0 and speed +1,
/// or the outgoing animation at its last frame and speed -1, then keeps a
/// 12-update transition timer. The source clip rate is 0.5 per update for
/// its seven-frame panel sequences. Camera projection belongs to the host.
/// </summary>
public sealed class Rac1MenuPanelMotion
{
    public readonly record struct PanelPose(
        int Slot, int NativeSequence, int Frame, Vector3 Min, Vector3 Max,
        Vector3 FirstCorner, Vector3 OppositeCorner);

    private readonly Rac1MenuPanelModel _model;
    private readonly Rac1MenuPanelPages _pages;
    private readonly double[] _phases = new double[Rac1MenuPanelPages.PanelSlots];
    private readonly int[] _sequences = new int[Rac1MenuPanelPages.PanelSlots];
    private int _direction;
    private int _remaining;
    private Rac1MenuPanelPages.Page _pendingPage;

    public Rac1MenuPanelPages.Page Page { get; private set; }
    public int RemainingUpdates => _remaining;

    public Rac1MenuPanelMotion(Rac1MenuPanelModel model, Rac1MenuPanelPages pages,
        Rac1MenuPanelPages.Page initialPage = Rac1MenuPanelPages.Page.OrdinaryPause)
    {
        _model = model;
        _pages = pages;
        Page = initialPage;
        _pendingPage = initialPage;
        for (int i = 0; i < _sequences.Length; i++)
        {
            int seq = pages[Page].ForSlot(i);
            _sequences[i] = seq;
            // The native initialiser opens at sequence's final frame.
            _phases[i] = _model.Sequences[seq].Frames.Count - 1;
        }
    }

    /// <summary>
    /// Back/return transitions play the outgoing animation in reverse,
    /// while ordinary navigation plays the incoming page's new animation
    /// from zero. No assumptions about which OBP page owns a native address.
    /// </summary>
    public void BeginTransition(Rac1MenuPanelPages.Page incoming, bool backwards)
    {
        if (_remaining != 0)
            throw new InvalidOperationException("Native menu transition is already playing.");
        _pendingPage = incoming;
        _direction = backwards ? -1 : 1;
        _remaining = 12;
        for (int i = 0; i < _sequences.Length; i++)
        {
            int seq = backwards ? _pages[Page].ForSlot(i) : _pages[incoming].ForSlot(i);
            _sequences[i] = seq;
            _phases[i] = backwards ? _model.Sequences[seq].Frames.Count - 1 : 0;
        }
    }

    public void Tick()
    {
        if (_remaining == 0) return;
        for (int i = 0; i < _sequences.Length; i++)
        {
            var clip = _model.Sequences[_sequences[i]];
            double rate = clip.Rate > 0 ? clip.Rate : 0;
            _phases[i] = Math.Clamp(_phases[i] + _direction * rate, 0, clip.Frames.Count - 1);
        }
        _remaining--;
        if (_remaining == 0)
        {
            Page = _pendingPage;
            _direction = 0;
        }
    }

    public PanelPose Pose(int slot)
    {
        if ((uint)slot >= Rac1MenuPanelPages.PanelSlots)
            throw new ArgumentOutOfRangeException(nameof(slot));
        int seq = _sequences[slot];
        var clip = _model.Sequences[seq];
        int frame = Math.Clamp((int)Math.Round(_phases[slot], MidpointRounding.AwayFromZero),
            0, clip.Frames.Count - 1);
        var coords = clip.Frames[frame];
        if (coords.Length != _model.RestPositions.Count * 3)
            throw new InvalidDataException("Native panel animation's posed vertex count changed.");
        var min = new Vector3(float.PositiveInfinity);
        var max = new Vector3(float.NegativeInfinity);
        for (int v = 0; v < coords.Length; v += 3)
        {
            var point = new Vector3((float)coords[v], (float)coords[v + 1], (float)coords[v + 2]);
            min = Vector3.Min(min, point);
            max = Vector3.Max(max, point);
        }
        // Unlike textured mesh extrema, these are the original four
        // transformed semantic frame points from Moby+0x78. The menu
        // projector consumes only opposite points 0 and 3.
        var corners = clip.AttachmentFrameCorners[frame];
        if (corners.Length != 4 || corners.Any(point =>
            !float.IsFinite(point.X) || !float.IsFinite(point.Y) || !float.IsFinite(point.Z)))
            throw new InvalidDataException("Native menu frame has invalid attachment corners.");
        return new PanelPose(slot, seq, frame, min, max, corners[0], corners[3]);
    }
}
