using OBP.Core;
using Godot;
using OBP.Fusion;
using OBP.Godot;
using OBP.Runtime.Gameplay;

namespace OBP.Game;

public partial class OBPGame
{
    private async Task RunTrilogyGameplaySmokeAsync()
    {
        try
        {
            Require(OrdinaryPlayerSession && _playerNative is not null, "ordinary selected player required");
            var session = _playerSession!; var initial = session.Capture();
            MovementReproPlayer? canonical = null;
            string[]? audioBaseline = null;
            foreach (string destination in new[] { "rac1:LEVEL0", "rac2:LEVEL1", "rac3:TABLE1", "obp:fusion-lab" })
            {
                session.Restore(initial);
                Require(destination == "obp:fusion-lab" ? EnterFusionLab() : OnDestinationChosen(TrilogyWorldProviders.Resolve(destination)!), "entry failed: " + destination);
                var player = _player!; player.PlaytestInputFilter = _ => default;
                var deck = new StaticBody3D { Name = "ExplicitEquipmentContactFixture" };
                deck.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(200, 2, 200) } });
                _sceneResult!.Root.AddChild(deck); deck.GlobalPosition = new(4096, 4095, 4096);
                await PhysicsFramesAsync(2); player.ApplyDevelopmentReset(new(4096, 4097, 4096));
                await PhysicsFramesAsync(40);
                Require(PlayerPresentationAssets is not null && _rac1PlayerAudio is not null, "selected source bank missing in " + destination);
                canonical ??= player.CaptureMovementRepro();
                player.RestoreMovementRepro(canonical);
                _rac1PlayerAudioRandom = OBP.RAC1.Gameplay.Rac1PickupRandom.FromSeed(1440);
                _selectedPlayerAudioTrace.Clear();
                await CaptureCapabilityTrialAsync(player);
                var cues = _selectedPlayerAudioTrace.Select(cue => $"{cue.Source}/{cue.Build}/{cue.Level}/{cue.Class}/{cue.Sound}/{cue.LevelSound}/{cue.Loop}@{cue.Tick}").ToArray();
                Require(_selectedPlayerAudioTrace.Any(cue => cue.Class == 0) && cues.Length > 0, "movement/jump/wrench audio is silent in " + destination);
                Require(_rac1HeldItem.Hand.WrenchHeld, "wrench attack did not retain hand ownership in " + destination);
                if (audioBaseline is null) audioBaseline = cues;
                else Require(audioBaseline.SequenceEqual(cues), "same physics-clock player actions changed audio intents in " + destination);
                Require(TryOpenPauseMenu() && _pauseMenu is { UniversalEnabled: true, RetailEnabled: false }, "ordinary pause is not universal");
                long before = _playerPhysicsClock!.Tick; int shots = _fusion!.ShotsFired;
                _pauseMenu!.ShowUniversalSection("Quick Select");
                Require(AssignPlayerQuickSelect(7, "rac3/item/43").Accepted, "mixed slot assignment failed");
                _pauseMenu.ShowUniversalSection("Gadgets"); _pauseMenu.ShowUniversalSection("Appearance");
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                Require(_playerPhysicsClock.Tick == before && _fusion.ShotsFired == shots, "pause advanced weapon/player clock");
                TryClosePauseMenu();
                foreach (string catalogId in new[] { "rac1/item/10", "rac1/item/16", "rac2/item/30", "rac3/item/43" })
                {
                    var entry = ReadPlayerEquipment().Single(entry => entry.Definition.Id == catalogId);
                    Require(entry.Selectable && SelectPlayerEquipment(catalogId).Accepted, "weapon unavailable in " + destination + ": " + entry.Reason);
                    int ammo = _fusion.Status(entry.Definition.Item!.Value).Ammo;
                    player.PlaytestInputFilter = _ => new RawPlayerInputFrame { TraversalHeld = true };
                    await PhysicsFramesAsync(catalogId == "rac3/item/43" ? 90 : 18);
                    if (catalogId == "rac1/item/16")
                        Require(_rac1PyroEmitting && _rac1Particles?.Live.Any(particle => particle.Type == 12) == true &&
                            _rac1ParticleView is { RenderedCount: > 0 }, "Pyrocitor has no renderable flame in " + destination);
                    player.PlaytestInputFilter = _ => default;
                    await PhysicsFramesAsync(65);
                    Require(_fusion.Status(entry.Definition.Item.Value).Ammo < ammo, "weapon did not consume native ammo in " + destination + ": " + catalogId);
                    if (catalogId == "rac1/item/16")
                        Require(!_rac1PyroEmitting && _rac1Particles?.Live.All(particle => particle.Type != 12) == true,
                            "Pyrocitor flame survived release in " + destination);
                }
                Require(_selectedPlayerAudioTrace.Count > 0, "selected Ratchet/equipment went silent in " + destination);
                Require(_selectedPlayerAudioTrace.All(cue => cue.Source == "rac1" && cue.Build == OBP.RAC1.Rac1Authority.Primary.BuildId && cue.Level == 0), "player voice/surface bank followed destination");
                GD.Print($"[trilogy-gameplay-smoke] {destination}: universal menu, held models, native firing/ammo and {cues.Length} matching player audio intents PASS");
            }
            GetTree().Paused = true;
            ulong deadline = Time.GetTicksMsec() + 180_000;
            while (_actorWorld.Placed.Count + _actorWorld.Problems.Count < _actorWorld.Placements.Count && Time.GetTicksMsec() < deadline)
            { await Task.Delay(10); TickActorPreparation(); }
            Require(_actorWorld.Problems.Count == 0, "mixed actor preparation: " + string.Join("; ", _actorWorld.Problems.Select(pair => pair.Key + ": " + pair.Value)));
            Require(_actorWorld.Placed.Count == 3 && _actors is { Count: 3 }, "three native source actors did not coexist");
            foreach (var placement in _actorWorld.Placements.Values)
            {
                var library = await _actorWorld.Preparation[placement.Id];
                var source = _actorWorld.Sources[placement.Id];
                bool rejected = false;
                try { _actorWorld.Factories!.Resolve(placement with { NativeContextId = "unrecovered-context" }).Create(placement, library, source); }
                catch (NotSupportedException) { rejected = true; }
                Require(rejected && _actors!.Count == 3 && _actorWorld.Bodies.Count == 3, "missing native context left a partial actor");
            }
            // This explicit encounter fixture starts after all background IO.
            // Reset every native session, not its recorded gameplay results.
            foreach (string id in _actorWorld.Placed.Keys.ToArray()) Require(ResetPlacedActor(id).Accepted, "initial encounter reset failed");
            ClearFusionTransients();
            _rac1EffectRandom = OBP.RAC1.Gameplay.Rac1PickupRandom.FromSeed(1440);
            GetTree().Paused = false;
            _player!.PlaytestInputFilter = _ => default;
            _player.ApplyDevelopmentReset(new(0, 1, 14));
            var starts = _actorWorld.Views.ToDictionary(pair => pair.Key, pair => pair.Value.Observe().Position);
            double healthBeforeContact = _playerNative!.Life.Capture().Current;
            var nativeTrace = new List<string>();
            for (int sample = 0; sample < 6; sample++)
            {
                await PhysicsFramesAsync(30);
                foreach (var pair in _actorWorld.Placed.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                {
                    var facts = _actors!.Find(pair.Value.Target)!.Observe();
                    nativeTrace.Add(FormattableString.Invariant($"{sample}/{pair.Key}/{pair.Value.Target.InstanceIndex}/{facts.Position.X:R}/{facts.Position.Y:R}/{facts.Position.Z:R}/{facts.NativeState}/{facts.NativeSequence}/{facts.Health:R}/{facts.Alive}"));
                }
            }
            string digest = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(string.Join("\n", nativeTrace))));
            GD.Print("[trilogy-gameplay-smoke] native encounter trace " + digest);
            foreach (var actor in _actorWorld.Views.Values.Where(actor => actor.Node.Source.SourceGame != ObpSourceGame.Rac3))
                Require(System.Numerics.Vector3.Distance(starts[actor.Registration.Instance.Target], actor.Observe().Position) > .1f,
                    "hostile has no live native motion: " + actor.Node.Source.SourceGame);
            Require(_playerNative.Life.Capture().Current < healthBeforeContact,
                $"native actors did not damage the shared player: before={healthBeforeContact}, after={_playerNative.Life.Capture().Current}, player={_player.GlobalPosition}; " +
                string.Join("; ", _actorWorld.Views.Values.Select(actor => $"{actor.Node.Source.SourceGame}: {actor.Observe()}")));
            Require(_rac2OpeningMsr1.Values.Single().Node.AnimationSample is not null, "GC native animation was not presented");
            await FireAtActor("arena-rac1-toad", "rac3/item/43", 100);
            Require(!Actor("arena-rac1-toad").Observe().Alive, "ordinary charged Cannon did not defeat the native toad");
            Require(ResetPlacedActor("arena-gc-msr1").Accepted, "GC combat reset failed");
            await FireAtActor("arena-gc-msr1", "rac2/item/30", 160);
            Require(!Actor("arena-gc-msr1").Observe().Alive, "ordinary Lancer contacts did not exhaust native GC health");
            await FireAtActor("arena-uya-target", "rac1/item/10", 18);
            Require(!Actor("arena-uya-target").Observe().Alive, "ordinary Bomb Glove did not deactivate the UYA target");
            foreach (string id in _actorWorld.Placed.Keys.ToArray())
            {
                var stale = _actorWorld.Placed[id];
                var others = _actorWorld.Placed.Where(pair => pair.Key != id).ToDictionary();
                Require(ResetPlacedActor(id).Accepted, "independent actor reset failed: " + id);
                Require(_actorWorld.Placed[id] != stale && _actors!.Find(stale.Target) is null, "reset reused an old actor identity");
                Require(!_actorWorld.Rewards.ContainsKey(stale.Target) && !_actorWorld.Audio.ContainsKey(stale.Target), "reset retained native reward/audio ownership");
                Require(others.All(pair => _actorWorld.Placed[pair.Key] == pair.Value), "reset changed a different actor");
                await PhysicsFramesAsync(2);
                Require(_actorWorld.Bodies.Count == 3, "actor reset leaked a collision body");
            }
            GD.Print("[trilogy-gameplay-smoke] native motion/animation, shared player damage, ordinary Lancer/Cannon/Bomb contacts and independent resets PASS");
            var toadPlacement = _actorWorld.Placements["arena-rac1-toad"];
            var sourceKey = _actorWorld.Sources[toadPlacement.Id];
            var sourceTask = ActorSources.Library(sourceKey, toadPlacement.LevelId);
            PrepareActorSource(toadPlacement);
            Require(ReferenceEquals(sourceTask, _actorWorld.Preparation[toadPlacement.Id]), "current source version was not reused");
            // Fail after native registration, animation and contact-body construction.
            // The factory must roll back its own resources before RuntimeSet takes ownership.
            int rootsBeforeFailure = _sceneResult!.Root.GetChildCount();
            int enemiesBeforeFailure = _rac1Enemies.Count, nodesBeforeFailure = _rac1HostileNodes.Count;
            var failureBindings = new Rac1ActorBindings(_rac1Hostiles, _rac1HostileNodes, _rac1Enemies, ObserveRac1Actor,
                ApplyRegisteredRac1Damage, ClearRegisteredActorTransients,
                index => _rac1Captures.RemoveAll(capture => capture.Key == new OBP.RAC1.Gameplay.Rac1MobyRuntimeKey(749, index)),
                _ => throw new InvalidDataException("actor construction fixture"), _ => { });
            var failureFactory = new Rac1ToadActorFactory(new(_actors!, _sceneResult.Root, _actorWorld.Bodies), ActorSources, failureBindings);
            bool rolledBack = false;
            try { failureFactory.Create(toadPlacement, await sourceTask, sourceKey); }
            catch (InvalidDataException error) when (error.Message == "actor construction fixture") { rolledBack = true; }
            Require(rolledBack && _actors!.Count == 3 && _actorWorld.Bodies.Count == 3 &&
                _rac1Enemies.Count == enemiesBeforeFailure && _rac1HostileNodes.Count == nodesBeforeFailure &&
                _sceneResult.Root.GetChildCount() == rootsBeforeFailure, "failed factory leaked native registrations, scene or physics");
            GD.Print("[trilogy-gameplay-smoke] partial actor construction rollback PASS");
            GetTree().Paused = true;
            var detachedActors = _actorWorld.Placed.Values.ToArray();
            ClearActorSourceCaches();
            Require(_actors!.Count == 0 && _actorWorld.Bodies.Count == 0 && _actorWorld.Views.Count == 0 &&
                ActorSources.LibraryCount == 0 && ActorSources.AssetCount == 0 &&
                detachedActors.All(actor => !_playerDamageActors.Contains(actor)), "source invalidation retained live actors or player attack registrations");
            foreach (string id in _actorWorld.Placements.Keys) _ = ResetPlacedActor(id);
            deadline = Time.GetTicksMsec() + 180_000;
            while (_actorWorld.Placed.Count + _actorWorld.Problems.Count < _actorWorld.Placements.Count && Time.GetTicksMsec() < deadline)
            { await Task.Delay(10); TickActorPreparation(); }
            Require(_actorWorld.Problems.Count == 0 && _actorWorld.Placed.Count == 3 && _actorWorld.Bodies.Count == 3 &&
                _actorWorld.Placed.Values.All(actor => !detachedActors.Contains(actor)), "source re-admission failed or revived retired target identities");
            sourceTask = ActorSources.Library(sourceKey, toadPlacement.LevelId);
            GetTree().Paused = false;
            GD.Print("[trilogy-gameplay-smoke] live source invalidation and re-admission PASS");
            var oldActors = _actors; var oldRoots = _actorWorld.Views.Values.Select(actor => actor.Node.Root).ToArray();
            var ammoBeforeTransit = _fusion!.Status(FusionWeaponSession.Lancer).Ammo;
            Require(OnDestinationChosen(TrilogyWorldProviders.Resolve("rac2:LEVEL1")!), "cleanup destination entry failed");
            await PhysicsFramesAsync(2);
            Require(oldActors!.Count == 0 && oldRoots.All(root => !IsInstanceValid(root)) && _actorWorld.Bodies.Count == 0,
                "world transition retained actors or their physics");
            Require(_fusion.Status(FusionWeaponSession.Lancer).Ammo == ammoBeforeTransit && _fusionProjectiles.Count == 0 && _fusionAudioPlayers.Count == 0,
                "world transition leaked effects/audio or reset native ammunition");
            Require(ReferenceEquals(sourceTask, ActorSources.Library(sourceKey, toadPlacement.LevelId)) && ActorSources.AssetCount == 2,
                "world transition discarded reusable attached-source actor data");
            ResetSharedWeaponAssets();
            Require(ActorSources.LibraryCount == 0 && ActorSources.AssetCount == 0,
                "source invalidation retained decoded actor caches");
            GD.Print("[trilogy-gameplay-smoke] current source-cache reuse and source invalidation PASS");
            GD.Print("[trilogy-gameplay-smoke] lease cleanup and persistent ammunition PASS");
            ApplicationLifecycle.RequestQuit(this, "trilogy-gameplay-smoke-pass", 0);

            HostedActor Actor(string id) => _actorWorld.Views[_actorWorld.Placed[id].Target];
            async Task FireAtActor(string id, string weapon, int heldTicks)
            {
                _playerNative.Life.Respawn();
                var target = Actor(id); var player = _player!;
                player.ApplyDevelopmentReset(target.Node.Root.GlobalPosition + new Vector3(5, 1, 0));
                player.Rac1CurrentYaw = 0;
                Require(SelectPlayerEquipment(weapon).Accepted, "combat weapon could not be selected: " + weapon);
                player.PlaytestInputFilter = _ => new RawPlayerInputFrame { TraversalHeld = true };
                await PhysicsFramesAsync(heldTicks);
                player.PlaytestInputFilter = _ => default;
                await PhysicsFramesAsync(100);
                GD.Print($"[trilogy-gameplay-smoke] ordinary {weapon} -> {id}: alive={target.Observe().Alive}, health={target.Observe().Health}, player={_playerNative.Life.Capture().Current}, yaw={player.Rac1CurrentYaw}, hits={_fusionAcceptedHits}");
            }
        }
        catch (Exception error)
        {
            GetTree().Paused = false;
            GD.PrintErr("[trilogy-gameplay-smoke] FAIL: " + error);
            ApplicationLifecycle.RequestQuit(this, "trilogy-gameplay-smoke-fail", 1);
        }
    }
}
