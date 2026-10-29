using System.Numerics;

namespace OBP.RAC1.Gameplay;

/// <summary>
/// The first-break ammo constructor and state1 collection path, 0x2c52a8 and
/// 0x2c53c0. Unlike bolts these pieces stop before granting ammo, and wait when
/// the selected item is full. Native vectors are Z-up/per-tick displacement.
/// </summary>
public sealed class Rac1AmmoPickupSession
{
    public Rac1CrateSupply.AmmoDefinition Definition { get; }
    public Vector3 Position { get; private set; }
    public Vector3 Velocity { get; private set; }
    public float Rotation { get; private set; }
    public int DelayTicks { get; private set; }
    public bool Collected { get; private set; }
    public bool GrantedThisTick { get; private set; }
    public Rac1AmmoGrantResult? Grant { get; private set; }
    private float _launchHeight;
    private bool _settled;

    public Rac1AmmoPickupSession(Rac1CrateSupply.AmmoDefinition definition, Vector3 origin, Rac1PickupRandom random)
    {
        Definition = definition;
        Position = origin;
        _launchHeight = origin.Z;
        float angle = random.Angle(), planar = random.Range(0, 2.5f) / 60;
        Velocity = new(MathF.Cos(angle) * planar, MathF.Sin(angle) * planar, random.Range(4, 6) / 60);
        DelayTicks = random.Below(16);
        Rotation = random.Angle();
    }

    public void Step(Vector3 ratchetRoot, Rac1WeaponInventory inventory, bool playerAlive,
        Rac1PickupContact? contact = null, float planarRadius = 3, float verticalRadius = 1.75f)
    {
        GrantedThisTick = false;
        if (Collected) return;
        if (DelayTicks > 0) DelayTicks--;
        if (!_settled)
        {
            Velocity -= new Vector3(0, 0, 4.9f / 3600);
            Position += Velocity;
            Velocity -= new Vector3(0, 0, 4.9f / 3600);
            if (contact is { } hit && Velocity.Z < 0)
            {
                Position = hit.Position;
                Velocity = Vector3.Zero;
                _settled = true;
            }
        }
        // Authored floor rescue still applies to an off-edge pickup.
        bool fallen = Position.Z < _launchHeight - 2;
        Vector3 delta = Position - ratchetRoot;
        if (!playerAlive || DelayTicks > 0 || (!_settled && !fallen)) return;
        if (!fallen && (delta.X * delta.X + delta.Y * delta.Y > planarRadius * planarRadius || MathF.Abs(delta.Z) > verticalRadius)) return;
        int before = inventory.GetAmmo(Definition.Item);
        if (!fallen && before >= Rac1ItemAmmoDescriptors.Get(Definition.Item).MaxAmmo) return;
        Grant = inventory.GrantAmmoClamped(Definition.Item, Definition.Amount);
        GrantedThisTick = Grant.Value.Changed;
        Collected = true;
    }
}
