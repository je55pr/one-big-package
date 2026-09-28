using System.Runtime.CompilerServices;
using OBP.RAC3.Level;
using OBP.Runtime;

namespace OBP.RAC3;

/// <summary>
/// RAC3-only gameplay context associated by RuntimeWorld identity. This keeps
/// UYA target-group/cache inputs available to the gameplay host without adding
/// game-specific fields to the neutral RuntimeWorld contract.
/// </summary>
public static class Rac3WorldGameplaySidecar
{
    private static readonly ConditionalWeakTable<RuntimeWorld, Rac3WorldGameplayContext> Contexts = new();

    internal static void Attach(
        RuntimeWorld world,
        Rac3WorldGameplayContext context)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(context);

        if (world.Game != "rac3")
            throw new ArgumentException("World is not RAC3/UYA.", nameof(world));
        if (world.LevelId != context.TableIndex)
        {
            throw new ArgumentException(
                $"RAC3 world table {world.LevelId} does not match gameplay context table {context.TableIndex}.",
                nameof(context));
        }

        Contexts.Add(world, context);
    }

    public static bool TryGet(
        RuntimeWorld world,
        out Rac3WorldGameplayContext? context)
    {
        ArgumentNullException.ThrowIfNull(world);
        return Contexts.TryGetValue(world, out context);
    }

    public static Rac3WorldGameplayContext Require(RuntimeWorld world)
    {
        if (!TryGet(world, out Rac3WorldGameplayContext? context) ||
            context is null)
        {
            throw new InvalidOperationException(
                $"RAC3 world table {world.LevelId} has no provider gameplay sidecar.");
        }

        return context;
    }
}

public sealed record Rac3WorldGameplayContext(
    int TableIndex,
    UyaGameplay.Gameplay Gameplay);
