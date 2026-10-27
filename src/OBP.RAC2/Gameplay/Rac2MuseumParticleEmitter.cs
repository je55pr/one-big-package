using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using OBP.Core;
using OBP.RAC2.Level;

namespace OBP.RAC2.Gameplay;

/// <summary>Class-4887's source-qualified emitter. Type-85 records copy inputs and drain independently of this owner.</summary>
public sealed class Rac2MuseumParticleEmitter
{
    public const int NativeClassId = 4887;
    public const int FileLevelId = 21;
    public const int EngineLevelId = 30;
    public ObpSourceRef Source { get; }
    public string AuthoredParametersSha256 { get; }
    public Vector3 Position { get; private set; }
    public Rac2MuseumParticleParameters Parameters { get; private set; }
    public bool EmissionEnabled { get; private set; } = true;
    public float Countdown { get; internal set; }
    internal bool Initialized;

    public Rac2MuseumParticleEmitter(ObpSourceRef source, Rac2MuseumParticleParameters parameters, Vector3 position)
    {
        if (source.Game != ObpSourceGame.Rac2 || source.BuildId != Rac2Authority.Primary.BuildId || source.LevelId != "LEVEL21")
            throw new ArgumentException("Museum emission requires the admitted GC original Museum source.", nameof(source));
        parameters.RequireSupported(); RequireFinite(position);
        Source = source; Parameters = parameters; Position = position;
        AuthoredParametersSha256 = Convert.ToHexString(SHA256.HashData(parameters.CopyNativeParameters())).ToLowerInvariant();
        Countdown = parameters.AuthoredCountdown;
    }

    public static Rac2MuseumParticleEmitter FromAuthored(ObpSourceRef levelSource, Rac2Instances.MobyInstance instance)
    {
        if (instance.OClass != NativeClassId || instance.PVarData is null)
            throw new NotSupportedException("This Moby is not an authored Museum class-4887 emitter.");
        return new(levelSource with { AssetKind = "moby", OriginalId = instance.Index.ToString(CultureInfo.InvariantCulture) },
            Rac2MuseumParticleParameters.Parse(instance.PVarData),
            new(instance.Position.X, instance.Position.Y, instance.Position.Z));
    }

    /// <summary>Native owner/menu writes affect future emissions; existing type-85 records retain their copied fields.</summary>
    public void ApplyNativeFrame(Rac2MuseumParticleParameters parameters, Vector3 position)
    {
        parameters.RequireSupported(); RequireFinite(position); Parameters = parameters; Position = position;
    }

    /// <summary>OBP admission/lifetime control; existing copied particles continue their native expiry.</summary>
    public void StopEmission() => EmissionEnabled = false;

    internal void NormalizeSpawnTimers() => Parameters = Parameters.NormalizeSpawnTimers();

    private static void RequireFinite(Vector3 value)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z))
            throw new ArgumentException("Museum particle positions must be finite.", nameof(value));
    }
}
