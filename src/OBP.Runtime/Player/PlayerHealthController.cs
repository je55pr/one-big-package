namespace OBP.Runtime.Player;

/// <summary>Native victim rules behind a selected OBP policy. No actor/world ownership here.</summary>
public interface IPlayerHealthController
{
    /// <summary>Reads the current native health state without changing it.</summary>
    PlayerHealthSnapshot Capture();

    /// <summary>Applies already-converted native damage units.</summary>
    void ApplyDamageUnits(int amount);

    /// <summary>Restores native health units.</summary>
    void HealUnits(int amount);

    /// <summary>Runs the controller's per-tick native update.</summary>
    void TickNative();

    /// <summary>Resets health for an OBP-authored respawn; the session checks the result against the selected respawn policy.</summary>
    void ResetForObpRespawn();

    /// <summary>Defeats the player from a world hazard such as a kill volume.</summary>
    void DefeatFromWorldHazard();
}
