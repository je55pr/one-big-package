namespace OBP.Godot;

/// <summary>
/// Prepare import and presentation before touching the currently playable world.
/// This boundary is also exercised with fault-injected imports/builds in tests.
/// </summary>
public static class WorldTransitionStaging
{
    public static bool TryPrepare<TWorld, TScene>(
        Func<TWorld> import,
        Func<TWorld, TScene> build,
        out TWorld? world,
        out TScene? scene,
        out Exception? error)
        where TWorld : class
        where TScene : class
    {
        ArgumentNullException.ThrowIfNull(import);
        ArgumentNullException.ThrowIfNull(build);
        world = null;
        scene = null;
        error = null;
        try
        {
            world = import();
            scene = build(world);
            return true;
        }
        catch (Exception ex)
        {
            error = ex;
            return false;
        }
    }
}
