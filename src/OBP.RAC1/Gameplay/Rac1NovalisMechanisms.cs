using System.Buffers.Binary;
using System.Numerics;
using OBP.RAC1.Level;

namespace OBP.RAC1.Gameplay;

/// <summary>
/// Novalis mechanisms recovered from the original level-1 updaters. Positions,
/// camera and player facts use native Z-up coordinates; one Step is 1/60 second.
/// Collision selection and scene presentation belong to the host.
/// </summary>
public abstract class Rac1NovalisMechanism
{
    protected Rac1NovalisMechanism(Rac1Instances.MobyInstance source, int size)
    {
        if (source.PVar?.Length != size) throw new InvalidDataException("Invalid Novalis mechanism PVar size.");
        InstanceIndex = source.Index;
        NativeClassId = source.OClass;
        Position = Home = new(source.Position.X, source.Position.Y, source.Position.Z);
        if (!Finite(Position)) throw new InvalidDataException("Non-finite mechanism position.");
    }

    public int InstanceIndex { get; }
    public int NativeClassId { get; }
    public Vector3 Home { get; }
    public Vector3 Position { get; protected set; }
    public int State { get; protected set; }
    public int? Sound { get; protected set; }
    public abstract void Step(Vector3 player, Vector3 camera);

    protected static int I(byte[] p, int at) => BinaryPrimitives.ReadInt32LittleEndian(p.AsSpan(at));
    protected static float F(byte[] p, int at) => BinaryPrimitives.ReadSingleLittleEndian(p.AsSpan(at));
    protected static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
    protected static float PlanarDistance(Vector3 a, Vector3 b) => new Vector2(a.X - b.X, a.Y - b.Y).Length();

    // EE ADD/SUB/MUL round toward zero; DIV.S rounds to nearest. Restricted
    // to the finite ordinary-range cells used here, not a general PS2 FPU.
    internal static float Cell(double value)
    {
        float f = (float)value;
        return Math.Abs(f) > Math.Abs(value) ? (f > 0 ? MathF.BitDecrement(f) : MathF.BitIncrement(f)) : f;
    }
}

/// <summary>Class 768/769 approach doors, original updater 0x002fed68.</summary>
public sealed class Rac1NovalisProximityDoor : Rac1NovalisMechanism
{
    public const int LeftClass = 768, RightClass = 769;
    public float NativeYaw { get; }
    public float Reach { get; }
    public float Progress { get; private set; }

    public Rac1NovalisProximityDoor(Rac1Instances.MobyInstance source) : base(source, 0x20)
    {
        if (source.OClass is not (LeftClass or RightClass)) throw new ArgumentException("Not a Novalis approach door.");
        Reach = F(source.PVar!, 0x14);
        NativeYaw = source.Rotation.Z;
        if (!float.IsFinite(Reach) || Reach <= 0 || !float.IsFinite(NativeYaw)) throw new InvalidDataException("Invalid door reach/yaw.");
    }

    public override void Step(Vector3 player, Vector3 camera)
    {
        Sound = null;
        bool near = PlanarDistance(Home, player) < Reach && MathF.Abs(Position.Z - player.Z) < 2f;
        switch (State)
        {
            case 0: State = 1; break;
            case 1: if (near) { State = 2; MovingSound(); } break;
            case 2:
                Progress = Cell((double)Progress + 1f / 20f);
                if (Progress >= 1f) { Progress = 1; State = 3; }
                break;
            case 3:
                float hysteresis = Cell((double)Reach * 1.1f);
                if (PlanarDistance(Home, player) > hysteresis && PlanarDistance(Home, camera) > hysteresis)
                { State = 4; MovingSound(); }
                break;
            case 4:
                if (near) { State = 2; break; }
                Progress = Cell((double)Progress - 1f / 20f);
                if (Progress <= 0) { Progress = 0; State = 1; }
                break;
        }
        float sign = NativeClassId == LeftClass ? 2f : -2f;
        Position = new(Home.X + MathF.Cos(NativeYaw) * Progress * sign,
            Home.Y + MathF.Sin(NativeYaw) * Progress * sign, Home.Z);
    }

    private void MovingSound() { if (NativeClassId == LeftClass) Sound = 0; }
}

/// <summary>Class 703/715, original updater 0x002f95c0; 120-tick rests and authored travel.</summary>
public sealed class Rac1NovalisPiston : Rac1NovalisMechanism
{
    public const int SmallClass = 703, LargeClass = 715;
    public int InitialEnd { get; }
    public int End { get; private set; }
    public int Timer { get; private set; }
    public int TravelTicks { get; }
    public float Height { get; }
    public float Rate { get; private set; }

    public Rac1NovalisPiston(Rac1Instances.MobyInstance source) : base(source, 0xc0)
    {
        if (source.OClass is not (SmallClass or LargeClass)) throw new ArgumentException("Not a Novalis piston.");
        InitialEnd = source.PVar![0xa4];
        Height = F(source.PVar!, 0xa8);
        float period = F(source.PVar!, 0xb0);
        if (InitialEnd > 1 || !float.IsFinite(Height) || Height <= 0 || !float.IsFinite(period) || period <= 0 || period > 100)
            throw new InvalidDataException("Invalid piston end, height or period.");
        TravelTicks = (int)Cell((double)period * 60f);
        if (TravelTicks < 1) throw new InvalidDataException("Piston travel rounds to zero frames.");
    }

    public override void Step(Vector3 player, Vector3 camera)
    {
        Sound = null;
        Timer = Math.Max(0, Timer - 1);
        if (State == 0) { State = 1; End = InitialEnd; Timer = 120; }
        if (End is 0 or 1 && Timer == 0)
        {
            Rate = (End == 1 ? -Height : Height) / TravelTicks;
            Sound = End == 1 ? 1 : 0;
            End = 2; Timer = TravelTicks;
        }
        else if (End == 2)
        {
            Position = Position with { Z = Cell((double)Position.Z - Rate) };
            if (Timer == 0)
            {
                End = Rate < 0 ? 0 : 1;
                if (End == InitialEnd) Position = Home;
                Timer = 120;
            }
        }
    }
}

/// <summary>
/// Class 701 staircase rocks, updater 0x002f9080. The trigger is the centre and
/// height of PVar +0x80's clip volume, not standing on the rock. Native camera,
/// dust, camera release/forced hero-state reset and persistent command-bit
/// ownership remain unbound.
/// </summary>
public sealed class Rac1NovalisFallingRock : Rac1NovalisMechanism
{
    public const int ClassId = 701;
    public Vector3 Trigger { get; }
    public int DelayTicks { get; }
    public int Timer { get; private set; }
    public int FallTimer { get; private set; }
    public float FallVelocity { get; private set; }
    public int PlayerRunMode { get; set; }
    public bool RestoredFallen { get; init; }

    public Rac1NovalisFallingRock(Rac1Instances.MobyInstance source, IReadOnlyList<Rac1ClipVolume> volumes) : base(source, 0x90)
    {
        if (source.OClass != ClassId) throw new ArgumentException("Not a Novalis falling rock.");
        int clip = I(source.PVar!, 0x80);
        if (clip < 0 || clip >= volumes.Count) throw new InvalidDataException("Missing rock trigger volume.");
        Trigger = volumes[clip].Centre;
        DelayTicks = BinaryPrimitives.ReadInt16LittleEndian(source.PVar!.AsSpan(0x88));
        if (DelayTicks < 0) throw new InvalidDataException("Negative rock delay.");
    }

    public override void Step(Vector3 player, Vector3 camera)
    {
        Sound = null;
        switch (State)
        {
            case 0:
                State = RestoredFallen ? 4 : 1;
                if (RestoredFallen) Position = Home with { Z = Cell((double)Home.Z - 20f) };
                break;
            case 1:
                if (player.Z >= Trigger.Z && PlanarDistance(player, Trigger) < 10 && PlayerRunMode is 0 or 1 or 9)
                { State = 2; Timer = DelayTicks; Sound = 1; }
                break;
            case 2:
                Timer = Math.Max(0, Timer - 1);
                if (Timer == 0) { State = 3; FallTimer = 45; }
                break;
            case 3:
                float gravity = Cell((double)((1f / 60f) / 60f) * 30f);
                FallVelocity = Cell((double)FallVelocity - gravity);
                Position = Position with { Z = Cell((double)Position.Z + FallVelocity) };
                FallTimer = Math.Max(0, FallTimer - 1);
                if (FallTimer == 0 && Cell((double)Home.Z - Position.Z) > 20f)
                {
                    Position = Home with { Z = Cell((double)Home.Z - 20f) };
                    FallVelocity = 0; State = 4; Sound = 0;
                }
                break;
        }
    }
}
