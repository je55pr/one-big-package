namespace OBP.Runtime.Player;

/// <summary>Native victim rules behind a selected OBP policy. No actor/world ownership here.</summary>
public interface IPlayerHealthController
{
    PlayerHealthSnapshot Capture();
    void ApplyDamageUnits(int amount);
    void HealUnits(int amount);
    void TickNative();
    void ResetForObpRespawn();
    void DefeatFromWorldHazard();
}
