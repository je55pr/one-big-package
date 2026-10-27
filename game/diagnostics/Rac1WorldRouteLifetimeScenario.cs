using OBP.Core;

namespace OBP.Game.Diagnostics;

public sealed record Rac1WorldRouteResources(DiagnosticWorldSnapshot World, int Mechanisms, int TaxiNodes, int TaxiHulls,
    int MusicTracks, int DecodedMusicClips, bool MusicReader, bool TaxiCameraOwned, bool TaxiPassenger, bool SharedProfileActive = false);
public sealed record Rac1WorldRouteRetirement(bool SceneRetired, int LiveTaxiHulls, int LiveAudioPlayers, bool CameraRetired);
public interface IRac1WorldRouteRetirementWitness { Rac1WorldRouteRetirement Read(); }
public interface IRac1WorldRouteLifetimeFixture
{
    Rac1WorldRouteResources Read();
    IRac1WorldRouteRetirementWitness Capture();
}
public sealed class Rac1WorldRouteLifetimeScenario(IRac1WorldRouteLifetimeFixture resources, IDiagnosticMenuFixture menus,
    IDiagnosticNavigationActions navigation, IDiagnosticSourceAttachmentFixture sources, Action<string> log)
{
    public async Task RunAsync(MovementDiagnosticContext context)
    {
        var initial = resources.Read();
        RequireIsolated(initial);
        string destination = initial.World.DestinationId ?? throw new DiagnosticUnavailableException("The source route destination is unavailable.");
        for (int visit = 0; visit < 2; visit++)
        {
            var before = resources.Read(); var retired = resources.Capture();
            menus.ShowTitle();
            using (visit == 0 ? sources.Detach(ObpSourceGame.Rac1, refreshOnDetach: false) : null)
            {
                await context.Driver.ProcessAsync(4);
                var empty = resources.Read(); var witness = retired.Read();
                Require(empty.World.Content is null && empty.Mechanisms == 0 && empty.TaxiNodes == 0 && empty.TaxiHulls == 0 &&
                    empty.MusicTracks == 0 && empty.DecodedMusicClips == 0 && !empty.MusicReader && !empty.TaxiCameraOwned && !empty.TaxiPassenger,
                    "Retired route retained world mechanisms, music, taxi hulls or camera/passenger state.");
                Require(witness.SceneRetired && witness.LiveTaxiHulls == 0 && witness.LiveAudioPlayers == 0 && witness.CameraRetired,
                    "The old route lease retained scene bodies, camera or music players.");
            }
            Require((await navigation.NavigateAsync(destination, context.Driver.Lifetime)).Accepted, "Could not re-enter the source route.");
            await context.Driver.PhysicsAsync(6);
            var current = resources.Read();
            Require(current.World.DestinationId == destination && current.World.Content == initial.World.Content && current.World.Generation > before.World.Generation &&
                !string.IsNullOrWhiteSpace(current.World.WorldInstanceId), "Route re-entry reused a retired world identity.");
            Require(current.Mechanisms == initial.Mechanisms && current.TaxiNodes == initial.TaxiNodes && current.TaxiHulls == initial.TaxiHulls &&
                current.MusicTracks == initial.MusicTracks && current.MusicReader == initial.MusicReader && !current.TaxiCameraOwned && !current.TaxiPassenger,
                "Route re-entry duplicated or lost its source resources.");
        }
        log("[rac1-world-route-lifetime] source detach, title exit, repeated visits and scene/audio/hull retirement PASS");
    }
    internal static void RequireIsolated(Rac1WorldRouteResources state)
    {
        if (state.SharedProfileActive) throw new DiagnosticUnavailableException("World route diagnostics require --no-profile to protect the ordinary shared save.");
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
