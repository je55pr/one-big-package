using System.Numerics;

namespace OBP.RAC1.Gameplay;

public sealed partial class Rac1ParticleSession
{
    // 0x25e450: separate type15 sprays, paired type11 fireballs/white flashes,
    // and the ten-frame texture program of type8 puffs. Not the Bomb recipe.
    public void WeaponExplosion(Rac1WeaponExplosion recipe, Vector3 origin, Vector3 camera)
    {
        uint[] start = [0x4f008fffu, 0x4f008fffu, 0x4f007fffu, 0x4f006fffu, 0x2fffffffu, 0x2fffffffu];
        uint[] end = [0x2f005f7fu, 0x2f004f7fu, 0x2f003f7fu, 0x2f00004fu, 0x2f000000u, 0x3f000000u];
        for (int i = 0; i < recipe.Sparks; i++)
            Spark(origin, Rac1WeaponExplosion.Spray(random, 1.3962634f) + Vector3.UnitZ * (3 * Dt),
                0x4f007fff, 0x1f00007f, random.Inclusive(60, 120), true, 40000);
        float distance = Vector3.Distance(origin, camera);
        int count = distance < recipe.Fireballs * 2 ? (int)distance / 2 : recipe.Fireballs;
        for (int i = 0; i < count; i++)
        {
            float speed = (random.Range(8, 10) - Math.Max(0, 7 - distance)) * Dt;
            uint a = start[random.Below(6)], b = end[random.Below(6)];
            ExplosionSpark(origin, Vector3.UnitZ * (8 * Dt), a, b, random.Inclusive(15, 20),
                random.Inclusive(30, 45), 0, 0, 400000, speed);
            ExplosionSpark(origin, Vector3.UnitZ * (8 * Dt), 0x7fffffff, 0x00ffffff, random.Inclusive(5, 10),
                random.Inclusive(15, 20), 0, 0, 400000, speed * .5f);
        }
        for (int i = 0; i < recipe.Puffs; i++)
        {
            Vector3 direction = new(random.Range(-1, 1), random.Range(-1, 1), random.Range(-1, 1));
            Glow(origin, SetLength(direction, random.Range(0, 3) * Dt), start[random.Below(6)], end[random.Below(6)],
                random.Inclusive(30, 45), 200000);
        }
    }
}
