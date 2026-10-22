using System.Buffers.Binary;

namespace OBP.RAC1.Gameplay;

/// <summary>
/// Source-backed admission and select attack transitions for native Aridia
/// class 612 (LEVEL2:0x2d7748). This is deliberately not a translation of
/// every pathfinding, physics, emitter and damage branch of its 18-slot table.
/// </summary>
public static class Rac1AridiaConstructorbotNative
{
    public const int NativeClassId = Rac1AridiaEnemies.Constructorbot;
    public const int InitialModePvarOffset = 0x194;
    public const int RestState = 7;
    public const int ParticleModeState = 8;
    public const int ParticleAlternateState = 9;
    public const int AimState = 10;
    public const int JointContactState = 11;
    public const int FollowupState = 12;
    public const int ContactRecoveryState = 13;

    public readonly record struct Frame(int NativeState, int Sequence, bool JointContactQuery = false);

    public static int ReadInitialMode(ReadOnlySpan<byte> pvar)
    {
        if (pvar.Length < InitialModePvarOffset + sizeof(int))
            throw new InvalidDataException("Aridia Constructorbot has no initial variant PVar.");
        int mode = BinaryPrimitives.ReadInt32LittleEndian(pvar.Slice(InitialModePvarOffset, sizeof(int)));
        if (mode is < 0 or > 3)
            throw new InvalidDataException("Unknown retail Constructorbot startup mode.");
        return mode;
    }

    // L2:0x2d7808..0x2d7998:
    // +0x194==0 -> state 7, sequence 0; ==1 -> state 10, sequence 0;
    // ==2 -> state 8, sequence 2; ==3 -> state 9, sequence 15.
    // This corresponds to four DIFFERENT authored populations, not a
    // universal stationary flamethrower on Aridia level load.
    public static Frame Initialize(ReadOnlySpan<byte> pvar) => ReadInitialMode(pvar) switch
    {
        0 => new(RestState, 0),
        1 => new(AimState, 0),
        2 => new(ParticleModeState, 2),
        3 => new(ParticleAlternateState, 15),
        _ => throw new InvalidDataException("Unknown Constructorbot startup mode.")
    };

    /// <summary>
    /// L2:0x2d8088 moves native state 10 into 12/sequence 9 only after
    /// the original animation-completion bit at Moby+0x70 &amp; 2.
    /// </summary>
    public static Frame StepAim(bool animationCompleted) =>
        animationCompleted ? new(FollowupState, 9) : new(AimState, 0);

    /// <summary>
    /// L2:0x2d812c (the jump table's STATE 11, not state 12) queries a
    /// transformed joint-0 contact volume EVERY update. Completion changes
    /// to state 13 and randomly selects sequence 8 or 10; the actual
    /// source damage solver and host contact result remain external.
    /// </summary>
    public static Frame StepJointContact(bool animationCompleted, bool chooseSequenceTen) =>
        animationCompleted
            ? new(ContactRecoveryState, chooseSequenceTen ? 10 : 8, JointContactQuery: true)
            : new(JointContactState, 8, JointContactQuery: true);
}
