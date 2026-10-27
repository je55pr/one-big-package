using System.Collections.Immutable;
using System.Numerics;
using OBP.Core;
using OBP.Runtime.Presentation;

namespace OBP.Runtime.Effects;

/// <summary>A registered source-owned caller/parameter path, never a source-local type number alone.</summary>
public readonly record struct RuntimeEffectRecipeKey(ObpSourceRef Source, string Recipe)
{
    public void Validate()
    {
        if (Source is null || Source.Game is not (ObpSourceGame.Rac1 or ObpSourceGame.Rac2 or ObpSourceGame.Rac3) ||
            string.IsNullOrWhiteSpace(Source.BuildId) || string.IsNullOrWhiteSpace(Source.LevelId) ||
            string.IsNullOrWhiteSpace(Source.AssetKind) || string.IsNullOrWhiteSpace(Source.OriginalId) || string.IsNullOrWhiteSpace(Recipe))
            throw new ArgumentException("An effect recipe needs its source, build, bank and registered caller identity.");
    }
}

public enum RuntimeEffectAvailability { Ready, MissingSource, WrongBuild, MissingAssets, UnsupportedRecipe, UnsupportedContext }
public sealed record RuntimeEffectProblem(RuntimeEffectAvailability Availability, string Reason);
public readonly record struct RuntimeEffectControlResult(bool Accepted, string Reason);

/// <summary>OBP Y-up placement facts. End is a supplied endpoint, not an instruction to trace or damage anything.</summary>
public readonly record struct RuntimeEffectPose(Vector3 Position, Vector3 End)
{
    public void Validate()
    {
        if (!float.IsFinite(Position.X) || !float.IsFinite(Position.Y) || !float.IsFinite(Position.Z) ||
            !float.IsFinite(End.X) || !float.IsFinite(End.Y) || !float.IsFinite(End.Z))
            throw new ArgumentException("Effect placement must be finite.");
    }
}

/// <summary>Explicit presentation observations, independent of destination identity. Vectors use OBP Y-up.</summary>
public readonly record struct RuntimeEffectView(Vector3 Eye, Vector3 Forward, Vector3 Right, Vector3 Up,
    float TanX, float TanY, float ProjectionY, float Aspect)
{
    public void Validate()
    {
        if (!Finite(Eye) || !Finite(Forward) || !Finite(Right) || !Finite(Up) ||
            !float.IsFinite(TanX) || !float.IsFinite(TanY) || !float.IsFinite(ProjectionY) || !float.IsFinite(Aspect) ||
            TanX <= 0 || TanY <= 0 || ProjectionY <= 0 || Aspect <= 0 ||
            MathF.Abs(Forward.LengthSquared() - 1) > .0001f || MathF.Abs(Right.LengthSquared() - 1) > .0001f ||
            MathF.Abs(Up.LengthSquared() - 1) > .0001f || MathF.Abs(Vector3.Dot(Forward, Right)) > .0001f ||
            MathF.Abs(Vector3.Dot(Forward, Up)) > .0001f || MathF.Abs(Vector3.Dot(Right, Up)) > .0001f)
            throw new ArgumentException("Effect presentation requires an orthonormal camera and positive projection factors.");
    }
    private static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}

public enum RuntimeEffectSpriteProjection { WorldBillboard, ProjectedBillboard }
public readonly record struct RuntimeEffectDrawState(RuntimeBlendEquation Blend, bool DepthTest = true,
    bool DepthWrite = false, float NearFade = 0, float FarFade = 0, bool DepthFade = false);
public sealed record RuntimeEffectTexture(ObpSourceRef Source, RuntimeTexture Texture);

/// <summary>Already interpreted source output. No gravity, random curves, lifetimes or type dispatch live here.</summary>
public readonly record struct RuntimeEffectSprite(long Allocation, int Slot, ObpSourceRef Texture,
    Vector3 Position, float Size, Vector4 Color, float Rotation, RuntimeEffectDrawState Draw,
    RuntimeEffectSpriteProjection Projection = RuntimeEffectSpriteProjection.WorldBillboard,
    Vector2 ProjectedAxes = default);
public sealed record RuntimeEffectMesh(ObpSourceRef Texture, RuntimeObjectMesh Surface);
public readonly record struct RuntimeEffectLight(ObpSourceRef Source, Vector3 Position, Vector3 Color, float Energy, float Range);
public sealed record RuntimeEffectFrame(ImmutableArray<RuntimeEffectSprite> Sprites,
    ImmutableArray<RuntimeEffectMesh> Meshes, ImmutableArray<RuntimeEffectLight> Lights)
{
    public static RuntimeEffectFrame Empty { get; } = new([], [], []);
}

public readonly record struct RuntimeEffectState(long NativeTick, int ParticleCount, bool Emitting,
    long Attempted, long Emitted, long RandomSamples, string? Limitation = null);

/// <summary>Implemented in RAC modules. Each Step is exactly one source-declared native tick.</summary>
public interface IRuntimeEffectInstance : IDisposable
{
    RuntimeEffectState State { get; }
    void Step(RuntimeEffectView view);
    void SetPose(RuntimeEffectPose pose);
    void StopEmission();
    void RetireOwner();
    RuntimeEffectFrame Capture(RuntimeEffectView view);
}

/// <summary>Assets and validated typed recipe parameters are bound by the source adapter before registration.</summary>
public interface IRuntimeEffectFactory
{
    RuntimeEffectRecipeKey Key { get; }
    int NativeTicksPerSecond { get; }
    int? MaximumEmissionTicks => null;
    IReadOnlyList<RuntimeEffectTexture> Textures { get; }
    IRuntimeEffectInstance Create(uint seed, RuntimeEffectPose pose);
}
