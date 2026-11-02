using Godot;
using OBP.Core;
using OBP.Godot;
using OBP.RAC2;
using OBP.Runtime;

namespace OBP.Game;

/// <summary>
/// Read-only world debug presentation. No world, source or player lifetime is owned here.
/// The application supplies a copied frame of the currently selected world.
/// </summary>
internal sealed record WorldHudFrame(
    RuntimeWorld World,
    RuntimeWorldScene.Result Scene,
    ObpDestination? Destination,
    int Switches,
    Vector3? PlayerPosition,
    bool PlayerOnFloor,
    string OverlayStatus,
    bool HasAudioDiagnostics,
    string AudioStatus,
    string CrateStatus,
    string Rac2Status,
    string Rac1Status,
    string TaxiStatus,
    string Rac3Status);

internal partial class WorldHud : Label
{
    internal WorldHud()
    {
        Name = "WorldHud";
        Position = new Vector2(16, 12);
        MouseFilter = MouseFilterEnum.Ignore;
        AddThemeColorOverride("font_color", new Color(0.85f, 0.92f, 1f));
    }

    internal void Render(WorldHudFrame? frame, bool enabled)
    {
        Visible = enabled;
        if (!enabled)
        {
            Text = string.Empty;
            return;
        }
        if (frame is null) return;

        var w = frame.World;
        var r = frame.Scene;
        var rac2Entry = w.Game == ObpSourceGame.Rac2 ? Rac2PlanetCatalogue.Find(w.LevelId) : null;
        string gameLabel = w.Game.SourceGame switch
        {
            ObpSourceGame.Rac1 => "Ratchet & Clank",
            ObpSourceGame.Rac2 => "Going Commando",
            ObpSourceGame.Rac3 => "Up Your Arsenal",
            _ => w.Game.ToString(),
        };
        string destinationName = w.PlanetName is { Length: > 0 }
            ? w.DisplayName
            : frame.Destination?.DisplayName ?? "Unknown destination";
        string container = frame.Destination?.NativeContainer ?? rac2Entry?.ContainerFile ?? "(native container unresolved)";
        string pos = "-";
        string ground = "-";
        if (frame.PlayerPosition is { } gp)
        {
            pos = $"{gp.X:0.0} {gp.Y:0.0} {gp.Z:0.0}";
            ground = frame.PlayerOnFloor ? "grounded" : "air";
        }

        Text =
            $"Game: {gameLabel}   Build: {w.BuildId}\n" +
            $"Destination: {destinationName}\n" +
            $"Level id: {w.LevelId}   Container: {container}   (switch #{frame.Switches})\n" +
            $"Player XYZ: {pos}   {ground}\n" +
            $"Render tris: {r.Triangles:N0}   Collision tris: {r.CollisionTriangles:N0}\n" +
            $"TIE meshes: {r.TieInstances}   Shrub meshes: {r.ShrubInstances}   Moby meshes: {r.MobyInstances}" +
            (r.AnimatedMobies > 0 ? $"   Animated mobies: {r.AnimatedMobies}" : "") +
            (r.DynamicObjects > 0 ? $"   Dynamic objects: {r.DynamicObjects}" : "") +
            $"\n{frame.OverlayStatus}   (F1 isolate · F2 tint · F3 collision · F4 bounds · F5 lights · F6 sky · F7 clear)" +
            (frame.HasAudioDiagnostics ? $"\n{frame.AudioStatus}" : "") +
            (string.IsNullOrEmpty(frame.CrateStatus) ? "" : $"\n{frame.CrateStatus}") +
            (string.IsNullOrEmpty(frame.Rac2Status) ? "" : $"\n{frame.Rac2Status}") +
            (string.IsNullOrEmpty(frame.Rac1Status) ? "" : $"\n{frame.Rac1Status}") +
            (string.IsNullOrEmpty(frame.TaxiStatus) ? "" : $"\n{frame.TaxiStatus}") +
            (string.IsNullOrEmpty(frame.Rac3Status) ? "" : $"\n{frame.Rac3Status}");
    }
}
