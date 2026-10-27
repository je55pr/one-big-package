using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using OBP.Core;
using OBP.RAC1.Level;

namespace OBP.RAC1.Gameplay;

/// <summary>
/// One source-qualified class-27 owner. The caller runs Emit before the pool's Step,
/// as the native Moby pass precedes UpdateParts. Live parameters also affect existing particles.
/// </summary>
public sealed class Rac1ParticleEmitter
{
    public ObpSourceRef Source { get; }
    public Rac1MobyRuntimeKey Owner { get; }
    public string AuthoredParametersSha256 { get; }
    public Rac1ParticleEmitterParameters Parameters { get; private set; }
    public Vector3 Position { get; private set; }
    public Vector3 Rotation { get; private set; }
    public bool EmissionEnabled { get; private set; } = true;
    public bool IsRetired { get; private set; }
    public int BurstCountdown { get; internal set; }

    public Rac1ParticleEmitter(ObpSourceRef source, Rac1MobyRuntimeKey owner,
        Rac1ParticleEmitterParameters parameters, Vector3 position, Vector3 rotation)
    {
        if (source.Game != ObpSourceGame.Rac1 || source.BuildId != Rac1Authority.Primary.BuildId || owner.NativeClassId != 27)
            throw new ArgumentException("A type-6 emitter needs a class-27 owner from the admitted original R&C1 build.");
        parameters.RequireSupported();
        RequireFinite(position); RequireFinite(rotation);
        Source = source; Owner = owner; Parameters = parameters; Position = position; Rotation = rotation;
        AuthoredParametersSha256 = Convert.ToHexString(SHA256.HashData(parameters.CopyNativeParameters())).ToLowerInvariant();
        BurstCountdown = parameters.InitialCountdown;
    }

    public static Rac1ParticleEmitter FromAuthored(int levelId, Rac1Instances.MobyInstance instance)
    {
        if (levelId != 1 || instance.OClass != 27 || instance.PVar is null)
            throw new NotSupportedException("The admitted authored class-27 fixture is the original Novalis population.");
        return new(new(ObpSourceGame.Rac1, Rac1Authority.Primary.BuildId, "LEVEL1", "moby",
                instance.Index.ToString(CultureInfo.InvariantCulture)),
            new(instance.OClass, instance.Index), Rac1ParticleEmitterParameters.Parse(instance.PVar),
            new(instance.Position.X, instance.Position.Y, instance.Position.Z),
            new(instance.Rotation.X, instance.Rotation.Y, instance.Rotation.Z));
    }

    /// <summary>Native owner writes (for example flyer attachment); simulation-owned countdown is retained.</summary>
    public void ApplyNativeFrame(Rac1ParticleEmitterParameters parameters, Vector3 position, Vector3 rotation)
    {
        if (IsRetired) throw new InvalidOperationException("The particle owner has retired.");
        parameters.RequireSupported(); RequireFinite(position); RequireFinite(rotation);
        Parameters = parameters; Position = position; Rotation = rotation;
    }

    /// <summary>OBP lifetime control: stop new bursts while the existing particles drain.</summary>
    public void StopEmission() => EmissionEnabled = false;

    /// <summary>OBP lifetime control: old owner references are invalidated; pool Step retires their particles.</summary>
    public void Retire() { EmissionEnabled = false; IsRetired = true; }

    internal static void RequireFinite(Vector3 value)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z))
            throw new ArgumentException("Native emitter vectors must be finite.");
    }
}

/// <summary>
/// Source-space observation of the previous rendered view, for FastBSphereCheck(240, radius 20).
/// Host trigonometry/projection is a bounded float projection, not a bit-exact VU culler.
/// A missing view represents the Novalis load pass and culls without RNG consumption.
/// </summary>
public sealed class Rac1ParticleEmitterView
{
    public Vector3 Eye { get; }
    public Vector3 Forward { get; }
    public Vector3 Right { get; }
    public Vector3 Up { get; }
    public float TanX { get; }
    public float TanY { get; }

    public Rac1ParticleEmitterView(Vector3 eye, Vector3 forward, Vector3 right, Vector3 up, float tanX, float tanY)
    {
        Rac1ParticleEmitter.RequireFinite(eye); Rac1ParticleEmitter.RequireFinite(forward);
        Rac1ParticleEmitter.RequireFinite(right); Rac1ParticleEmitter.RequireFinite(up);
        if (!float.IsFinite(tanX) || !float.IsFinite(tanY) || tanX <= 0 || tanY <= 0 ||
            MathF.Abs(forward.LengthSquared() - 1) > .0001f || MathF.Abs(right.LengthSquared() - 1) > .0001f ||
            MathF.Abs(up.LengthSquared() - 1) > .0001f || MathF.Abs(Vector3.Dot(forward, right)) > .0001f ||
            MathF.Abs(Vector3.Dot(forward, up)) > .0001f || MathF.Abs(Vector3.Dot(right, up)) > .0001f)
            throw new ArgumentException("Emitter visibility requires an orthonormal native view and positive finite projection tangents.");
        Eye = eye; Forward = forward; Right = right; Up = up; TanX = tanX; TanY = tanY;
    }

    public bool Sees(Vector3 position)
    {
        Vector3 relative = position - Eye;
        float z = Vector3.Dot(relative, Forward), radius = 20;
        return z - radius <= 240 && z + radius > 0 &&
            TanX * z - (MathF.Abs(Vector3.Dot(relative, Right)) - radius * MathF.Sqrt(1 + TanX * TanX)) >= 0 &&
            TanY * z - (MathF.Abs(Vector3.Dot(relative, Up)) - radius * MathF.Sqrt(1 + TanY * TanY)) >= 0;
    }
}

public enum Rac1ParticleEmissionStatus { Emitted, Countdown, Culled, Stopped, Retired, PoolFull }
public readonly record struct Rac1ParticleEmission(Rac1ParticleEmissionStatus Status, int Requested, int Created);
