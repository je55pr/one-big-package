namespace OBP.RAC1.Gameplay;

/// <summary>Class230 ground-stage emission, 0x2c9080..2c91c4. World contact is host supplied.</summary>
public sealed class Rac1DoomCanisterSession
{
    public const int MaximumAgents = 8;
    public const int FullPopulationRetryTicks = 15;
    public int Emitted { get; private set; }
    public int Countdown { get; private set; }
    public bool Finished => Emitted == Rac1WeaponProfiles.DoomAgentsPerCanister;
    public bool Step(int activeAgents)
    {
        if (activeAgents < 0) throw new ArgumentOutOfRangeException(nameof(activeAgents));
        if (Finished) return false;
        if (Countdown > 0 && --Countdown > 0) return false;
        if (activeAgents >= MaximumAgents) { Countdown = FullPopulationRetryTicks; return false; }
        Emitted++;
        Countdown = Rac1WeaponProfiles.DoomAgentSpawnTicks;
        return true;
    }
}
