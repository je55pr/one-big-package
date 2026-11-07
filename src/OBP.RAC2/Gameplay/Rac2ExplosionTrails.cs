using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using OBP.Core;
using OBP.RAC2.Level;
using OBP.Runtime;
using OBP.Runtime.Effects;
using OBP.Runtime.Presentation;
using M = OBP.RAC2.Gameplay.Rac2ParticleMath;

namespace OBP.RAC2.Gameplay;

/// <summary>
/// Class-505's type-90 trails and overridden type-23 expanding sprites.
/// Loaded Oozla constructors/callbacks 0x334640/0x334758 and 0x32A730/0x32A8F8.
/// Camera-facing triangle strips are a host-independent projection of the
/// native head/tail facts, not an emulation of the PS2 line VU program.
/// </summary>
internal sealed class Rac2ExplosionTrails(Rac2ParticleAssets assets, Rac2ParticleRandom random, Func<long> reserveIdentity)
{
    private sealed class Spark
    {
        internal long Identity;
        internal Vector3 Position, Tail, Velocity;
        internal int Remaining, FadeThreshold, Texture;
        internal uint Color = 0x7fffffff;
    }

    private sealed class Cloud
    {
        internal long Identity;
        internal Vector3 Position, Velocity;
        internal int Remaining, Total, FadeTicks, FadeDuration, State = 3;
        internal float Size = 315000, Growth;
        internal byte Angle, Spin;
        // 0x3F9B0C..0x3F9B44 passes this constructor colour literally. The
        // first callback starts the fade envelope; native draw ordering is not yet proved.
        internal uint Color = 0x7f60a0ff;
    }

    private readonly List<Spark> _sparks = [];
    private readonly List<Cloud> _clouds = [];
    public int Count => _sparks.Count + _clouds.Count;

    internal void Emit(Vector3 origin, Vector3 cameraEye, Vector3 playerAnchor, float cameraYaw, int available)
    {
        // The explosion Moby's first basis points towards the camera. Its
        // second basis comes from the camera/player-anchor plane (0x3F88F8).
        Vector3 axis = Unit(cameraEye - origin, Vector3.UnitX);
        Vector3 side = Unit(Vector3.Cross(axis, playerAnchor - origin),
            Unit(Vector3.Cross(axis, Vector3.UnitZ), Vector3.UnitY));
        for (int i = 0; i < 15; i++)
        {
            Vector3 velocity = axis * M.Multiply(random.Range(15, 20), 1f / 60);
            float pitch = random.Symmetric(.26179942f, 1.3962636f);
            float yaw = random.Symmetric(.26179942f, 1.3962636f);
            velocity = Vector3.Transform(velocity, Quaternion.CreateFromAxisAngle(side, pitch));
            velocity = Vector3.Transform(velocity, Quaternion.CreateFromAxisAngle(axis, yaw));
            int life = random.Inclusive(20, 35);
            if (available-- <= 0) continue;
            _sparks.Add(new()
            {
                Identity = reserveIdentity(),
                Position = origin,
                Tail = origin - Unit(velocity, Vector3.Zero) * .9f,
                Velocity = velocity,
                Remaining = life,
                FadeThreshold = life - 15,
                Texture = assets.Definitions.Texture(90, 0)
            });
        }

        int direction = 1;
        for (int i = 0; i < 10; i++)
        {
            direction = -direction;
            Vector3 position = M.Add(origin, new(random.Symmetric(0, 1), random.Symmetric(0, 1), random.Symmetric(0, 1)));
            float speed = random.Range(1f / 60, 1f / 30);
            float pitch = random.Symmetric(0, 1.3962636f);
            float yaw = cameraYaw + random.Symmetric(0, 1.5707965f);
            var velocity = new Vector3(MathF.Cos(yaw) * MathF.Cos(pitch) * speed,
                MathF.Sin(yaw) * MathF.Cos(pitch) * speed, MathF.Sin(pitch) * speed);
            // The type-23 constructor still consumes its three zero-amplitude
            // jitter samples, a sign draw and growth draw before caller overrides.
            for (int component = 0; component < 3; component++) _ = random.Symmetric(0, 0);
            int spin = random.Below(2) != 0 ? direction : -direction;
            float growth = random.Range(1, 1.01f);
            int life = random.Inclusive(40, 70), fade = random.Inclusive(1, 10);
            if (available-- <= 0) continue;
            _clouds.Add(new()
            {
                Identity = reserveIdentity(),
                Position = position,
                Velocity = velocity,
                Spin = unchecked((byte)spin),
                Growth = growth,
                Total = life,
                Remaining = life,
                FadeTicks = fade,
                FadeDuration = life
            });
        }
    }

    internal void Step()
    {
        foreach (var spark in _sparks)
        {
            if (--spark.Remaining <= 0) continue;
            spark.Texture = assets.Definitions.Texture(90, random.Below(2));
            spark.Position = M.Add(spark.Position, spark.Velocity);
            spark.Tail = spark.Position - Unit(spark.Velocity, Vector3.Zero) * .9f;
            spark.Velocity.Z = M.Subtract(spark.Velocity.Z, .004055556f);
            if (spark.Remaining < spark.FadeThreshold)
            {
                int alpha = (int)MathF.Round((float)spark.Remaining / spark.FadeThreshold * 127);
                spark.Color = 0xffffff | (uint)alpha << 24;
            }
        }
        _sparks.RemoveAll(spark => spark.Remaining <= 0);
        foreach (var cloud in _clouds)
        {
            cloud.Angle = unchecked((byte)(cloud.Angle + cloud.Spin));
            cloud.Position = M.Add(cloud.Position, cloud.Velocity);
            cloud.Size = M.Multiply(cloud.Size, cloud.Growth);
            --cloud.Remaining;
            if (cloud.State == 3)
            {
                int elapsed = cloud.Total - cloud.Remaining;
                int alpha = (int)MathF.Round(64 * Math.Min(1, (float)elapsed / cloud.FadeTicks));
                cloud.Color = 0x60a0ff | (uint)alpha << 24;
                if (elapsed >= cloud.FadeTicks) { cloud.State = 2; cloud.FadeDuration = cloud.Total - cloud.FadeTicks; }
            }
            else if (cloud.Remaining > 0)
            {
                int alpha = (int)MathF.Round(64f * cloud.Remaining / cloud.FadeDuration);
                cloud.Color = 0x60a0ff | (uint)alpha << 24;
            }
        }
        _clouds.RemoveAll(cloud => cloud.Remaining <= 0);
    }

    internal void Capture(RuntimeEffectView view, ImmutableArray<RuntimeEffectSprite>.Builder sprites,
        out ImmutableArray<RuntimeEffectMesh> meshes)
    {
        foreach (var cloud in _clouds)
            if (Rac2CrateExplosion.Project(new(cloud.Identity, 0, Texture(assets.Definitions.Texture(23, 0)), cloud.Position,
                cloud.Size, cloud.Color, cloud.Angle, 0x44), view) is { } sprite) sprites.Add(sprite);
        var strips = ImmutableArray.CreateBuilder<RuntimeEffectMesh>();
        foreach (var spark in _sparks)
        {
            var head = Runtime(spark.Position); var tail = Runtime(spark.Tail);
            float depth = Vector3.Dot(head - view.Eye, view.Forward);
            int z = (int)Math.Clamp((double)depth * 4096, int.MinValue, int.MaxValue);
            const int near = 2048, far = 262144; // Native depth byte 0x22.
            if (z <= near || z >= far) continue;
            int fade = Math.Min(4096, Math.Min(z - near, (far - z) >> 4));
            Vector3 side = Unit(Vector3.Cross(head - tail, view.Eye - head), view.Right) * .05f;
            Vector3[] points = [head - side, head + side, tail - side, tail + side];
            var color = new float[] { 255 / 128f, 255 / 128f, 255 / 128f, (fade * (spark.Color >> 24) >> 12) / 128f };
            var surface = new RuntimeObjectMesh(Rac2ParticleAssets.AssetKind, spark.Texture,
                points.SelectMany(p => new double[] { p.X, p.Y, p.Z }).ToArray(), [0, 0, 0, 1, 1, 0, 1, 1],
                [0, 1, 2, 2, 1, 3], Enumerable.Range(0, 4).SelectMany(_ => color).ToArray(),
                new(WrapS: RuntimeTextureWrap.Clamp, WrapT: RuntimeTextureWrap.Clamp,
                    AlphaBlendEnabled: true, BlendEquation: RuntimeBlendEquation.AdditiveSourceAlpha));
            strips.Add(new(Texture(spark.Texture), surface));
        }
        meshes = strips.ToImmutable();
    }

    internal void Clear() { _sparks.Clear(); _clouds.Clear(); }
    private ObpSourceRef Texture(int id) => assets.Source with
    { AssetKind = Rac2ParticleAssets.AssetKind, OriginalId = id.ToString(CultureInfo.InvariantCulture) };
    private static Vector3 Runtime(Vector3 p) => new(p.X, p.Z, p.Y);
    private static Vector3 Unit(Vector3 p, Vector3 fallback) => p.LengthSquared() > 1e-12f ? Vector3.Normalize(p) : fallback;
}
