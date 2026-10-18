using System.Numerics;
using OBP.RAC1.Player;

namespace OBP.RAC1.Gameplay;

/// <summary>
/// Class806's resource admission and one-point credit. Its eight particles are
/// presentation, not eight health rewards. Native particle splines remain a
/// separate presentation boundary; the host reports an arrival event.
/// </summary>
public sealed class Rac1NanotechCloudSession
{
    public const int ParticleCount = 8;
    public Vector3 Origin { get; }
    public bool Attracting { get; private set; }
    public bool Collected { get; private set; }
    public Rac1NanotechCloudSession(Vector3 origin) => Origin = origin;
    public bool TryAttract(Vector3 ratchet, Rac1RatchetNanotechSnapshot health, int pendingClouds)
    {
        if (Attracting || Collected || health.IsDead || health.Nanotech <= 0 ||
            health.Nanotech + pendingClouds >= health.RespawnNanotech ||
            Vector3.Distance(Origin, ratchet) >= Rac1CrateSupply.NanotechAttractDistance) return false;
        return Attracting = true;
    }
    public bool Arrive(Rac1RatchetNanotechSession health)
    {
        if (!Attracting || Collected) return false;
        Collected = true;
        return health.CollectCrateNanotech();
    }
}
