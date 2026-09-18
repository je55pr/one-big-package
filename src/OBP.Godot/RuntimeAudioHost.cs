using System.Buffers.Binary;
using Godot;
using OBP.Runtime.Audio;

namespace OBP.Godot;

/// <summary>
/// Godot presentation adapter for engine-neutral runtime audio intents.
/// Source-game code must decode retail bytes before they cross this boundary.
/// </summary>
public sealed class RuntimeAudioHost
{
    public enum DiagnosticLevel
    {
        Info,
        Warning,
    }

    public sealed record Diagnostic(DiagnosticLevel Level, string Message);

    public sealed record PreparedClip(
        int SampleRateHz,
        bool Stereo,
        byte[] Pcm16LittleEndian,
        int FrameCount,
        bool Loop,
        float VolumeDb,
        Vector3? ScenePosition);

    private readonly List<Node> _levelPlayers = [];
    private readonly List<Node> _effectPlayers = [];
    private readonly List<Diagnostic> _diagnostics = [];

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics;

    public string StatusLine
    {
        get
        {
            Diagnostic? diagnostic = _diagnostics.LastOrDefault(
                item => item.Level == DiagnosticLevel.Warning)
                ?? _diagnostics.LastOrDefault();
            return diagnostic is null
                ? "audio: ready"
                : $"audio: {diagnostic.Message}";
        }
    }

    /// <summary>
    /// Convert one neutral intent into the exact Godot PCM payload and presentation
    /// values. Returns false for shapes Godot's AudioStreamWav cannot represent.
    /// </summary>
    public static bool TryPrepare(
        RuntimeAudioPlaybackIntent intent,
        out PreparedClip? prepared,
        out string? problem)
    {
        ArgumentNullException.ThrowIfNull(intent);

        var clip = intent.Clip;
        if (clip.ChannelCount is not (1 or 2))
        {
            prepared = null;
            problem = $"unsupported {clip.ChannelCount}-channel PCM clip";
            return false;
        }

        if (clip.FrameCount == 0)
        {
            prepared = null;
            problem = "empty PCM clip";
            return false;
        }

        if (clip.FrameCount > int.MaxValue)
        {
            prepared = null;
            problem = $"PCM clip has {clip.FrameCount:N0} frames, beyond Godot WAV limits";
            return false;
        }

        var bytes = new byte[clip.InterleavedPcm16.Length * sizeof(short)];
        for (int i = 0; i < clip.InterleavedPcm16.Length; i++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(
                bytes.AsSpan(i * sizeof(short), sizeof(short)),
                clip.InterleavedPcm16[i]);
        }

        Vector3? scenePosition = intent.Position is { } position
            ? RuntimeWorldScene.ToScene(position.X, position.Y, position.Z)
            : null;

        prepared = new PreparedClip(
            clip.SampleRateHz,
            clip.ChannelCount == 2,
            bytes,
            checked((int)clip.FrameCount),
            intent.Loop,
            GainToDecibels(intent.Gain),
            scenePosition);
        problem = null;
        return true;
    }

    /// <summary>Convert linear runtime gain to a finite Godot dB value.</summary>
    public static float GainToDecibels(double gain)
    {
        if (!double.IsFinite(gain) || gain < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(gain));
        }

        if (gain <= 0d)
        {
            return -80f;
        }

        return (float)Math.Max(-80d, 20d * Math.Log10(gain));
    }

    /// <summary>
    /// Start the level-scoped music/ambience set, replacing the previous set.
    /// Unsupported intents are skipped and retained in diagnostics.
    /// </summary>
    public int StartLevelAudio(
        Node parent,
        IEnumerable<RuntimeAudioPlaybackIntent> intents)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(intents);
        StopNodes(_levelPlayers);

        int started = 0;
        foreach (var intent in intents)
        {
            if (intent.Category is not (RuntimeAudioCategory.Music or RuntimeAudioCategory.Ambience))
            {
                Report(DiagnosticLevel.Warning,
                    $"level audio skipped non-level category {intent.Category}");
                continue;
            }

            if (TryStart(parent, intent, _levelPlayers, levelScoped: true))
            {
                started++;
            }
        }

        if (started == 0)
        {
            Report(DiagnosticLevel.Info, "no playable level music/ambience intent");
        }
        else
        {
            Report(DiagnosticLevel.Info, $"playing {started} level audio source(s)");
        }

        return started;
    }

    /// <summary>
    /// Present one ordinary sound effect. Positional intents become
    /// AudioStreamPlayer3D nodes; non-positional intents use AudioStreamPlayer.
    /// </summary>
    public bool PlayEffect(Node parent, RuntimeAudioPlaybackIntent intent)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(intent);

        if (intent.Category != RuntimeAudioCategory.SoundEffect)
        {
            Report(DiagnosticLevel.Warning,
                $"effect skipped category {intent.Category}");
            return false;
        }

        return TryStart(parent, intent, _effectPlayers, levelScoped: false);
    }

    /// <summary>Stop and release all level music and ambience players.</summary>
    public void StopLevelAudio() => StopNodes(_levelPlayers);

    /// <summary>Stop and release every player owned by this adapter.</summary>
    public void StopAll()
    {
        StopNodes(_levelPlayers);
        StopNodes(_effectPlayers);
        _diagnostics.Clear();
    }

    private bool TryStart(
        Node parent,
        RuntimeAudioPlaybackIntent intent,
        List<Node> owner,
        bool levelScoped)
    {
        if (!TryPrepare(intent, out var prepared, out string? problem))
        {
            Report(DiagnosticLevel.Warning,
                $"{intent.Category} unavailable: {problem}");
            return false;
        }

        Node? player = null;
        try
        {
            AudioStreamWav stream = BuildStream(prepared!);
            player = prepared!.ScenePosition is { } position
                ? BuildSpatialPlayer(stream, prepared.VolumeDb, position)
                : BuildPlayer(stream, prepared.VolumeDb);

            parent.AddChild(player);
            owner.Add(player);
            ConnectFinished(player, owner);
            Play(player);

            string scope = levelScoped ? "level" : "effect";
            string spatial = prepared.ScenePosition is null ? "2D" : "3D";
            Report(DiagnosticLevel.Info,
                $"{scope} {intent.Category} started ({spatial}, {prepared.SampleRateHz} Hz)");
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            if (player is not null)
            {
                owner.Remove(player);
                if (GodotObject.IsInstanceValid(player))
                {
                    player.QueueFree();
                }
            }

            Report(DiagnosticLevel.Warning,
                $"{intent.Category} could not start: {ex.Message}");
            return false;
        }
    }

    private static AudioStreamWav BuildStream(PreparedClip prepared) => new()
    {
        Format = AudioStreamWav.FormatEnum.Format16Bits,
        MixRate = prepared.SampleRateHz,
        Stereo = prepared.Stereo,
        Data = prepared.Pcm16LittleEndian,
        LoopMode = prepared.Loop
            ? AudioStreamWav.LoopModeEnum.Forward
            : AudioStreamWav.LoopModeEnum.Disabled,
        LoopBegin = 0,
        LoopEnd = prepared.FrameCount,
    };

    private static AudioStreamPlayer BuildPlayer(AudioStream stream, float volumeDb) => new()
    {
        Name = "RuntimeAudio2D",
        Stream = stream,
        VolumeDb = volumeDb,
    };

    private static AudioStreamPlayer3D BuildSpatialPlayer(
        AudioStream stream,
        float volumeDb,
        Vector3 position) => new()
        {
            Name = "RuntimeAudio3D",
            Stream = stream,
            VolumeDb = volumeDb,
            Position = position,
        };

    private static void Play(Node player)
    {
        switch (player)
        {
            case AudioStreamPlayer twoD:
                twoD.Play();
                break;
            case AudioStreamPlayer3D threeD:
                threeD.Play();
                break;
            default:
                throw new InvalidOperationException(
                    $"Unsupported runtime audio player {player.GetType().Name}.");
        }
    }

    private static void ConnectFinished(Node player, List<Node> owner)
    {
        void Release()
        {
            owner.Remove(player);
            if (GodotObject.IsInstanceValid(player))
            {
                player.QueueFree();
            }
        }

        switch (player)
        {
            case AudioStreamPlayer twoD:
                twoD.Finished += Release;
                break;
            case AudioStreamPlayer3D threeD:
                threeD.Finished += Release;
                break;
        }
    }

    private static void StopNodes(List<Node> nodes)
    {
        foreach (var node in nodes.ToArray())
        {
            if (GodotObject.IsInstanceValid(node))
            {
                switch (node)
                {
                    case AudioStreamPlayer twoD:
                        twoD.Stop();
                        break;
                    case AudioStreamPlayer3D threeD:
                        threeD.Stop();
                        break;
                }

                node.QueueFree();
            }
        }

        nodes.Clear();
    }

    private void Report(DiagnosticLevel level, string message)
    {
        _diagnostics.Add(new Diagnostic(level, message));
        const int maxDiagnostics = 12;
        if (_diagnostics.Count > maxDiagnostics)
        {
            _diagnostics.RemoveRange(0, _diagnostics.Count - maxDiagnostics);
        }

        if (level == DiagnosticLevel.Warning)
        {
            GD.PrintErr($"[audio] {message}");
        }
        else
        {
            GD.Print($"[audio] {message}");
        }
    }
}
