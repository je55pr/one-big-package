using OBP.Core;
using Godot;
using OBP.Fusion;
using OBP.Godot;
using OBP.Runtime.Gameplay;

namespace OneBigPackage;

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
            while (_placedActors.Count + _actorProblems.Count < _actorPlacements.Count && Time.GetTicksMsec() < deadline)
            { await Task.Delay(10); TickActorPreparation(); }
            Require(_actorProblems.Count == 0, "mixed actor preparation: " + string.Join("; ", _actorProblems.Select(pair => pair.Key + ": " + pair.Value)));
            Require(_placedActors.Count == 3 && _actors is { Count: 3 }, "three native source actors did not coexist");
            foreach (var placement in _actorPlacements.Values)
            {
                var library = await _actorPlacementTasks[placement.Id];
                var path = _actorPlacementStamps[placement.Id].Path;
                bool rejected = false;
                try { CreatePlacedActor(placement with { NativeContextId = "unrecovered-context" }, library, path); }
                catch (NotSupportedException) { rejected = true; }
                Require(rejected && _actors!.Count == 3 && _actorContactBodies.Count == 3, "missing native context left a partial actor");
            }
            // This explicit encounter fixture starts after all background IO.
            // Reset every native session, not its recorded gameplay results.
            foreach (string id in _placedActors.Keys.ToArray()) Require(ResetPlacedActor(id).Accepted, "initial encounter reset failed");
            ClearFusionTransients();
            _rac1EffectRandom = OBP.RAC1.Gameplay.Rac1PickupRandom.FromSeed(1440);
            GetTree().Paused = false;
            _player!.PlaytestInputFilter = _ => default;
            _player.ApplyDevelopmentReset(new(0, 1, 14));
            var starts = _actorViews.ToDictionary(pair => pair.Key, pair => pair.Value.Observe().Position);
            double healthBeforeContact = _playerNative!.Life.Capture().Current;
            var nativeTrace = new List<string>();
            for (int sample = 0; sample < 6; sample++)
            {
                await PhysicsFramesAsync(30);
                foreach (var pair in _placedActors.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                {
                    var facts = _actors!.Find(pair.Value.Target)!.Observe();
                    nativeTrace.Add(FormattableString.Invariant($"{sample}/{pair.Key}/{pair.Value.Target.InstanceIndex}/{facts.Position.X:R}/{facts.Position.Y:R}/{facts.Position.Z:R}/{facts.NativeState}/{facts.NativeSequence}/{facts.Health:R}/{facts.Alive}"));
                }
            }
            string digest = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(string.Join("\n", nativeTrace))));
            GD.Print("[trilogy-gameplay-smoke] native encounter trace " + digest);
            foreach (var actor in _actorViews.Values.Where(actor => actor.Node.Source.SourceGame != ObpSourceGame.Rac3))
                Require(System.Numerics.Vector3.Distance(starts[actor.Registration.Instance.Target], actor.Observe().Position) > .1f,
                    "hostile has no live native motion: " + actor.Node.Source.SourceGame);
            Require(_playerNative.Life.Capture().Current < healthBeforeContact,
                $"native actors did not damage the shared player: before={healthBeforeContact}, after={_playerNative.Life.Capture().Current}, player={_player.GlobalPosition}; " +
                string.Join("; ", _actorViews.Values.Select(actor => $"{actor.Node.Source.SourceGame}: {actor.Observe()}")));
            Require(_gcOpeningMsr1.Values.Single().Node.AnimationSample is not null, "GC native animation was not presented");
            await FireAtActor("arena-rac1-toad", "rac3/item/43", 100);
            Require(!Actor("arena-rac1-toad").Observe().Alive, "ordinary charged Cannon did not defeat the native toad");
            Require(ResetPlacedActor("arena-gc-msr1").Accepted, "GC combat reset failed");
            await FireAtActor("arena-gc-msr1", "rac2/item/30", 160);
            Require(!Actor("arena-gc-msr1").Observe().Alive, "ordinary Lancer contacts did not exhaust native GC health");
            await FireAtActor("arena-uya-target", "rac1/item/10", 18);
            Require(!Actor("arena-uya-target").Observe().Alive, "ordinary Bomb Glove did not deactivate the UYA target");
            foreach (string id in _placedActors.Keys.ToArray())
            {
                var stale = _placedActors[id];
                var others = _placedActors.Where(pair => pair.Key != id).ToDictionary();
                Require(ResetPlacedActor(id).Accepted, "independent actor reset failed: " + id);
                Require(_placedActors[id] != stale && _actors!.Find(stale.Target) is null, "reset reused an old actor identity");
                Require(!_actorRewardPickups.ContainsKey(stale.Target) && !_actorAudio.ContainsKey(stale.Target), "reset retained native reward/audio ownership");
                Require(others.All(pair => _placedActors[pair.Key] == pair.Value), "reset changed a different actor");
                await PhysicsFramesAsync(2);
                Require(_actorContactBodies.Count == 3, "actor reset leaked a collision body");
            }
            GD.Print("[trilogy-gameplay-smoke] native motion/animation, shared player damage, ordinary Lancer/Cannon/Bomb contacts and independent resets PASS");
            var toadPlacement = _actorPlacements["arena-rac1-toad"];
            var sourceKey = _actorSourceTasks.Keys.Single(key => key.Game == ObpSourceGame.Rac1);
            var sourceTask = _actorSourceTasks[sourceKey];
            var obsoleteSource = sourceKey; obsoleteSource.Written--;
            _actorSourceTasks.Add(obsoleteSource, sourceTask);
            var animationKey = _actorRac1Animations.Keys.Single();
            var obsoleteAnimation = animationKey; obsoleteAnimation.Written--;
            _actorRac1Animations.Add(obsoleteAnimation, _actorRac1Animations[animationKey]);
            PrepareActorSource(toadPlacement);
            Require(!_actorSourceTasks.ContainsKey(obsoleteSource) && !_actorRac1Animations.ContainsKey(obsoleteAnimation) &&
                ReferenceEquals(sourceTask, _actorSourceTasks[sourceKey]), "source version pruning discarded current data or retained obsolete data");
            var oldActors = _actors; var oldRoots = _actorViews.Values.Select(actor => actor.Node.Root).ToArray();
            var ammoBeforeTransit = _fusion!.Status(FusionWeaponSession.Lancer).Ammo;
            Require(OnDestinationChosen(TrilogyWorldProviders.Resolve("rac2:LEVEL1")!), "cleanup destination entry failed");
            await PhysicsFramesAsync(2);
            Require(oldActors!.Count == 0 && oldRoots.All(root => !IsInstanceValid(root)) && _actorContactBodies.Count == 0,
                "world transition retained actors or their physics");
            Require(_fusion.Status(FusionWeaponSession.Lancer).Ammo == ammoBeforeTransit && _fusionProjectiles.Count == 0 && _fusionAudioPlayers.Count == 0,
                "world transition leaked effects/audio or reset native ammunition");
            Require(ReferenceEquals(sourceTask, _actorSourceTasks[sourceKey]) && _actorRac1Animations.Count == 1 && _actorGcAnimations.Count == 1,
                "world transition discarded reusable attached-source actor data");
            ResetSharedWeaponAssets();
            Require(_actorSourceTasks.Count == 0 && _actorRac1Animations.Count == 0 && _actorGcAnimations.Count == 0,
                "source invalidation retained decoded actor caches");
            GD.Print("[trilogy-gameplay-smoke] current source-cache reuse, obsolete version pruning and source invalidation PASS");
            GD.Print("[trilogy-gameplay-smoke] lease cleanup and persistent ammunition PASS");
            ApplicationLifecycle.RequestQuit(this, "trilogy-gameplay-smoke-pass", 0);

            HostedActor Actor(string id) => _actorViews[_placedActors[id].Target];
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
