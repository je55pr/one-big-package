using System.Buffers.Binary;
using OBP.RAC1.Level;
using OBP.Runtime;
using OBP.Runtime.Gameplay;

namespace OBP.RAC1.Gameplay;

/// <summary>
/// R&C1 class-505 timed explosive crate, distinct from the four ordinary
/// crate classes. Owns the recovered native 1 -> 5 -> 3 -> 6/terminal
/// lifecycle and authored repeat flag. Spatial contacts and area queries
/// are supplied by Godot and explicitly remain host contact policy.
/// </summary>
public sealed class Rac1ExplosiveCrateSession : IRac1MobyDamageConsumer
{
    public const int ClassId = 505;
    public const int ActiveState = 1;
    public const int CountdownState = 5;
    public const int BurstState = 3;
    public const int WaitingRespawnState = 6;
    public const int BurstLastStep = 20;
    private readonly Rac1MobyRuntimeSession _runtime;
    private readonly Func<int, int, int> _chooseFrameCount;
    private readonly Dictionary<int, Entry> _entries = [];

    private sealed class Entry(Rac1MobyRuntimeInstance native, Rac1ExplosiveCrateAuthored authored)
    {
        public Rac1MobyRuntimeInstance Native { get; } = native;
        public Rac1ExplosiveCrateAuthored Authored { get; } = authored;
        public int Command;
        public int BurstStep;
        public int RespawnFrames;
    }

    public Rac1ExplosiveCrateSession(Rac1MobyRuntimeSession runtime, Func<int, int, int> chooseFrameCount)
    {
        _runtime = runtime;
        _chooseFrameCount = chooseFrameCount;
        _runtime.RegisterDamageConsumer(this);
    }

    public int NativeClassId => ClassId;
    public int Count => _entries.Count;

    public static Rac1ExplosiveCrateAuthored? ReadAuthored(RuntimeDynamicObject source)
    {
        if (source.SourceGame != "rac1" || source.NativeClassId != ClassId) return null;
        byte[]? raw = source.NativePayloads?.FirstOrDefault(p => p.Format == Rac1BoltCrate.InstancePayloadFormat)?.Data;
        byte[]? pvar = source.NativePayloads?.FirstOrDefault(p => p.Format == Rac1BoltCrate.PVarPayloadFormat)?.Data;
        if (raw is not { Length: >= Rac1Instances.MobyRecordSize } || pvar is not { Length: 0x100 })
            throw new InvalidDataException("Native class-505 requires an authored Moby record and 0x100-byte PVar.");
        int uid = BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(0x0c));
        int reward = BinaryPrimitives.ReadInt32LittleEndian(raw.AsSpan(0x10));
        if (reward < 0 || reward > short.MaxValue)
            throw new InvalidDataException("Native class-505 bolt reward is out of range.");
        if (source.NativeUid.HasValue && source.NativeUid.Value != uid)
            throw new InvalidDataException("Native class-505 UID mismatch.");
        return new(uid, reward, pvar[0xc8] != 0);
    }

    public void Register(RuntimeDynamicObject source, RuntimeEntityState state)
    {
        var authored = ReadAuthored(source) ?? throw new ArgumentException("Expected native explosive crate.", nameof(source));
        state.EnsureMatches(source);
        var raw = source.NativePayloads!.Single(p => p.Format == Rac1BoltCrate.PVarPayloadFormat).Data;
        var native = _runtime.Register(source, state, ActiveState, raw);
        if (!_entries.TryAdd(source.InstanceIndex, new Entry(native, authored)))
            throw new InvalidOperationException("Duplicate native class-505 instance.");
    }

    public bool TryTouch(int instanceIndex)
    {
        if (!_entries.TryGetValue(instanceIndex, out var entry) || !entry.Native.IsActive ||
            entry.Native.State.NativeState != ActiveState) return false;
        entry.Command = 0;
        _runtime.SetNativeState(entry.Native, CountdownState);
        return true;
    }

    public bool CanApplyDamage(RuntimeDynamicObject source, Rac1GameplayDamageEvent damage)
    {
        if (source.SourceGame != "rac1" || source.NativeClassId != ClassId ||
            !_entries.TryGetValue(source.InstanceIndex, out var entry)) return false;
        return entry.Native.IsActive && entry.Native.State.NativeState == ActiveState &&
            damage.Target.MatchesMoby(entry.Native.Key) && damage.NativeDamage > 0;
    }

    bool IRac1MobyDamageConsumer.CanApplyDamage(RuntimeDynamicObject source,
        Rac1GameplayDamageEvent damage) => CanApplyDamage(source, damage);

    object IRac1MobyDamageConsumer.ApplyDamage(RuntimeDynamicObject source,
        Rac1GameplayDamageEvent damage)
    {
        if (!CanApplyDamage(source, damage))
            throw new InvalidOperationException("Explosive crate cannot accept that damage.");
        var entry = _entries[source.InstanceIndex];
        // The native 0x20000 hit starts a one-tick fuse; other damaging
        // contacts choose between five and fifteen game-scaled frames.
        entry.Command = (damage.NativeDamageFlags.GetValueOrDefault() & 0x20000) != 0
            ? 1 : _chooseFrameCount(5, 15);
        if (entry.Command is < 1 or > 15)
            throw new InvalidOperationException("Explosive crate fuse selector returned an unsupported count.");
        _runtime.SetNativeState(entry.Native, CountdownState);
        return new Rac1ExplosiveCrateTriggered(entry.Native.Key, entry.Command);
    }

    public Rac1ExplosiveCrateFrame Step(int instanceIndex, bool beyondNativeDrawRange)
    {
        var entry = _entries[instanceIndex];
        int state = entry.Native.State.NativeState;
        bool detonate = false, warn = false, flash = false, reappeared = false;
        double mobyRadius = 0, playerRadius = 0;
        if (state == CountdownState)
        {
            if (entry.Command == 0) entry.Command = 3 * 60 - 1;
            warn = entry.Command % 60 == 57 || entry.Command == 13;
            flash = entry.Command % 60 > 40 || entry.Command < 15;
            if (entry.Command <= 1)
            {
                _runtime.SetNativeState(entry.Native, BurstState);
                entry.BurstStep = 1; // Native break_crate(hit=1) seeds PVar+0xca with one.
                detonate = true;
                flash = false;
            }
            else entry.Command--;
        }
        else if (state == BurstState)
        {
            if (entry.BurstStep <= BurstLastStep)
            {
                double fraction = entry.BurstStep / 20d;
                mobyRadius = fraction * 2d;
                playerRadius = fraction * 3d;
                entry.BurstStep++;
            }
            else if (entry.Authored.Repeatable)
            {
                // Native repeat crates wait hidden, then reappear only after
                // a source draw-range rejection and the local random delay.
                _runtime.SetNativeState(entry.Native, WaitingRespawnState);
                _runtime.SetPresence(entry.Native, RuntimeEntityPresence.Inactive);
                entry.RespawnFrames = _chooseFrameCount(30, 60);
            }
            else
            {
                _runtime.Terminalize(entry.Native, Rac1MobyRuntime.TerminalNativeStateFd);
            }
        }
        else if (state == WaitingRespawnState && beyondNativeDrawRange)
        {
            if (--entry.RespawnFrames <= 0)
            {
                _runtime.SetNativeState(entry.Native, ActiveState);
                _runtime.SetPresence(entry.Native, RuntimeEntityPresence.Active);
                reappeared = true;
            }
        }

        return new(entry.Native.State.NativeState, entry.Native.EntityState,
            detonate, warn, flash, mobyRadius, playerRadius, reappeared);
    }

    public Rac1ExplosiveCrateAuthored Authored(int instanceIndex) => _entries[instanceIndex].Authored;
}

public sealed record Rac1ExplosiveCrateAuthored(int Uid, int RewardCentre, bool Repeatable);
public readonly record struct Rac1ExplosiveCrateTriggered(Rac1MobyRuntimeKey Target, int FuseTicks);
public sealed record Rac1ExplosiveCrateFrame(
    int NativeState, RuntimeEntityState EntityState, bool Detonated,
    bool WarningSound, bool FlashRed, double MobyBlastRadius, double PlayerBlastRadius,
    bool Reappeared);
