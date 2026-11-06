using System.Buffers.Binary;
using System.Numerics;
using OBP.Core;
using OBP.Runtime;

namespace OBP.RAC2.Gameplay;

/// <summary>
/// Loaded NTSC-U class-505 states 1/5/3. Oozla 0x391240..0x391878;
/// authored Maktar uses the same updater at 0x3A52B8. One Step is one 60 Hz tick.
/// Contact geometry and conditioned RNG samples are supplied by the host.
/// </summary>
public sealed class Rac2ExplosiveCrateSession
{
    public const int NativeClass = 505;
    public const int BurstState = 3;
    public const float BurstCentreOffset = .5f;
    public static Vector3 FlashTint { get; } = new(200 / 128f, 64 / 128f, 64 / 128f);
    public const int MobyDamage = 2, PlayerDamage = 4;
    public const uint MobyDamageFlags = 1, PlayerDamageFlags = 0x10000, FastFuseDamageFlag = 0x20000;
    public int NativeState { get; private set; } = 1;
    public int FuseTicks { get; private set; }
    public int Uid { get; }
    public int AuthoredBolts { get; }
    public bool Bursting => NativeState == BurstState;
    public bool Terminal => NativeState == 0xfd;
    private int _burstStep;
    private bool _detonationPending;
    private readonly Func<int> _shortFuse;

    /// <summary>Conditioned newlib samples, not a replay of retail global RNG history. Loaded 0x310580 then 0x2E45B8.</summary>
    public static Func<int> CreateFuseSampler(uint seed)
    {
        var random = Rac2ParticleRandom.FromSeed(seed);
        return () => (int)MathF.Round(Rac2ParticleMath.Add(5,
            Rac2ParticleMath.Multiply(Rac2ParticleMath.Multiply(random.Below(32768), 10), 1f / 32768)));
    }

    public Rac2ExplosiveCrateSession(RuntimeDynamicObject source, Func<int> shortFuse)
    {
        ArgumentNullException.ThrowIfNull(shortFuse);
        if (source.SourceGame != ObpSourceGame.Rac2 || source.NativeClassId != NativeClass)
            throw new ArgumentException("Expected an original RAC2 explosive crate.", nameof(source));
        var raw = source.NativePayloads?.SingleOrDefault(payload => payload.Format == "rac2-moby-instance-0x88")?.Data;
        var pvar = source.NativePayloads?.SingleOrDefault(payload => payload.Format == "rac2-pvar")?.Data;
        if (raw is not { Length: 0x88 } || pvar is not { Length: 0x110 })
            throw new InvalidDataException("Class 505 requires its original Moby record and 0x110-byte PVar.");
        if (pvar[0xc8] != 0) throw new NotSupportedException("Repeatable RAC2 explosive crates need recovered respawn admission.");
        Uid = BinaryPrimitives.ReadInt32LittleEndian(raw.AsSpan(0x10));
        AuthoredBolts = BinaryPrimitives.ReadInt32LittleEndian(raw.AsSpan(0x14));
        if (Uid < 0 || AuthoredBolts < 0 || source.NativeUid is { } uid && uid != Uid ||
            BinaryPrimitives.ReadInt32LittleEndian(raw.AsSpan(0x28)) != NativeClass)
            throw new InvalidDataException("Invalid explosive crate reward identity or original class.");
        _shortFuse = shortFuse;
    }

    /// <summary>Native standing/side/ledge/touch contacts start the long fuse; an underside break detonates directly.</summary>
    public bool Touch(bool undersideBreak = false)
    {
        if (NativeState != 1 && !(undersideBreak && NativeState == 5)) return false;
        if (undersideBreak) BeginBurst();
        else { NativeState = 5; FuseTicks = 0; }
        return true;
    }

    public bool ApplyDamage(in Rac2CollisionDamage damage)
    {
        if ((damage.DamageFlags & Rac2CrateInteraction.QueriedDamageMask) == 0 || !float.IsFinite(damage.DamageHp)) return false;
        if (damage.DamageFlags == Rac2CrateInteraction.IgnoredExactDamageFlags) return Touch();
        if (NativeState == 1 && damage.DamageHp > 0)
        {
            FuseTicks = (damage.DamageFlags & FastFuseDamageFlag) != 0 ? 1 : ShortFuse();
            NativeState = 5;
            return true;
        }
        // The state-5 query does not repeat state 1's positive-HP check.
        if (NativeState != 5 || FuseTicks < 16) return false;
        FuseTicks = ShortFuse();
        return true;
    }

    public Rac2ExplosiveCrateFrame Step()
    {
        bool warning = false, flash = false;
        if (NativeState == 5)
        {
            if (FuseTicks == 0) FuseTicks = 179;
            warning = FuseTicks % 60 == 57 || FuseTicks == 13;
            flash = FuseTicks % 60 > 40 || FuseTicks < 15;
            if (FuseTicks == 1) { BeginBurst(); flash = false; }
            FuseTicks--;
        }
        bool detonated = _detonationPending;
        _detonationPending = false;
        float mobyRadius = 0, playerRadius = 0;
        if (NativeState == BurstState && !detonated)
        {
            if (_burstStep < 21)
            {
                // +CA starts at one. The native sphere radii are linear, not squared.
                mobyRadius = _burstStep * .05f * 2;
                playerRadius = _burstStep * .05f * 3;
                _burstStep++;
            }
            else NativeState = 0xfd;
        }
        return new(NativeState, FuseTicks, warning, flash, detonated, mobyRadius, playerRadius, Terminal);
    }

    private int ShortFuse()
    {
        int ticks = _shortFuse();
        return ticks is >= 5 and <= 15 ? ticks : throw new InvalidOperationException("Explosive fuse sample must be between 5 and 15 ticks.");
    }
    private void BeginBurst() { NativeState = BurstState; _burstStep = 1; _detonationPending = true; }
}

public readonly record struct Rac2ExplosiveCrateFrame(int NativeState, int FuseTicks, bool WarningSound,
    bool FlashRed, bool Detonated, float MobyRadius, float PlayerRadius, bool Terminal);
