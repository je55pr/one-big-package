using OBP.Core.Math;
using OBP.Runtime;

namespace OBP.Tests;

public sealed class RuntimePlayerStartTests
{
    [Fact]
    public void PreferredPlayerStart_PrefersExplicitPlayerStart()
    {
        var ship = new RuntimeSpawn(1, 2, 3, 0.25);
        var playerStart = new RuntimeSpawn(10, 20, 30, 0.75);

        var world = CreateWorld(ship, playerStart);

        Assert.Same(playerStart, world.PreferredPlayerStart);
        Assert.Same(ship, world.Ship);
    }

    [Fact]
    public void PreferredPlayerStart_FallsBackToShip()
    {
        var ship = new RuntimeSpawn(1, 2, 3, 0.25);

        var world = CreateWorld(ship, playerStart: null);

        Assert.Same(ship, world.PreferredPlayerStart);
    }

    [Fact]
    public void PreferredPlayerStart_IsNullWithoutPlayerStartOrShip()
    {
        var world = CreateWorld(ship: null, playerStart: null);

        Assert.Null(world.PreferredPlayerStart);
    }

    private static RuntimeWorld CreateWorld(RuntimeSpawn? ship, RuntimeSpawn? playerStart) =>
        new(
            Game: "test",
            BuildId: "test-build",
            LevelId: 0,
            PlanetName: null,
            LocationName: null,
            Meshes: [],
            Textures: [],
            MaterialCount: 0,
            CollisionMeshes: [],
            Bounds: new ObpBounds(Vec3.Zero, new Vec3(100, 100, 100)),
            Environment: null,
            Ship: ship,
            PlayerStart: playerStart);
}
