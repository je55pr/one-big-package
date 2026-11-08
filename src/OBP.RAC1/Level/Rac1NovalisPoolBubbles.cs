using System.Numerics;
using OBP.RAC1.Gameplay;

namespace OBP.RAC1.Level;

/// <summary>
/// The bubbles the class-751 pool controller (update 0x2fd0e8) blows along the edge where the river meets the
/// cave tank. While the camera is in pool zone 5 it spawns, every native frame, one large foam sprite at
/// x 177 +-0.6, z 39 whose y sweeps 176 .. 165 with the 20-entry wave table at 0x1fa6a0, and then walks the same
/// edge in 20 steps spawning a small bubble per step with probability 1/32 (x 178 +-0.6, z 39 - 0..0.2).
/// The particle session works in native coordinates directly.
/// </summary>
public sealed class Rac1NovalisPoolBubbles
{
    public const int Zone = 5;
    private const int WaveTableAddress = 0x1fa6a0, WaveLength = 20;
    private const float EdgeStartY = 176f, EdgeLength = 11f, SurfaceZ = 39f;

    private readonly byte[] _wave;

    private Rac1NovalisPoolBubbles(byte[] wave) => _wave = wave;

    public static Rac1NovalisPoolBubbles Read(Rac1ExecutableOverlay overlay)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        var wave = overlay.Read(WaveTableAddress, WaveLength).ToArray();
        if (wave.Any(step => step >= WaveLength)) throw new InvalidDataException("Novalis pool wave table is not a record.");
        return new(wave);
    }

    /// <summary>One native frame of the zone-5 branch.</summary>
    public void Emit(Rac1ParticleSession session, Rac1PickupRandom random, long frame)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(random);
        float along = _wave[(int)(frame % WaveLength)] / (float)WaveLength;
        session.PoolFoam(
            new Vector3(random.Range(-0.6f, 0.6f) + 177f, along * -EdgeLength + EdgeStartY + random.Range(-0.1f, 0.1f), SurfaceZ),
            new Vector3(0.02f, 0f, 0f), 1f, random.Range(0.4f, 0.5f));

        for (float step = 0f; step < 1f; step += 0.05f)
        {
            if (random.Below(32) != 0) continue;
            session.PoolBubble(
                new Vector3(random.Range(-0.6f, 0.6f) + 178f, step * -EdgeLength + EdgeStartY + random.Range(-0.1f, 0.1f),
                    random.Range(-0.2f, 0f) + SurfaceZ),
                random.Range(4f, 10f));
        }
    }
}
