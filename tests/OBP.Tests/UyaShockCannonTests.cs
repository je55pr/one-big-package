using OBP.Core;
using OBP.RAC3.Gameplay;

namespace OBP.Tests;

public sealed class UyaShockCannonTests
{
    [Fact]
    public void V5IsTheCannonUpgradeAndNeverAnUnqualifiedBlasterId()
    {
        var family = UyaShockCannonProfile.UpgradeFamily;
        Assert.Equal("Shock Blaster V4", family[3].Name);
        Assert.Equal(43, family[3].NextDefinition);
        Assert.Equal("Shock Cannon", family[4].Name);
        Assert.Equal(4, family[4].NativeLevel);
        Assert.Equal(0x1882, family[4].HeldMobyClass);
        Assert.Equal("rac3/rac3-ntscu-original/item:43", UyaShockCannonProfile.Identity.Key);
        var session = new UyaShockCannonSession();
        Assert.Throws<ArgumentOutOfRangeException>(() => session.Equip(39));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.Equip(44));
        Assert.Equal(UyaShockCannonState.Unequipped, session.State);
        Assert.Empty(session.Advance(new(true)).Shots);
    }

    [Fact]
    public void TapSpendsAtAdmissionThenEmitsMuzzleAndDamageOnSeparateNativeUpdates()
    {
        var session = Equipped();
        UyaShockCannonFrame admission = session.Advance(new(true));
        Assert.Equal(49, admission.Ammo);
        Assert.Equal(1, Assert.Single(admission.Shots).AmmoSpent);
        Assert.Empty(admission.Queries);
        Assert.Equal(UyaShockCannonState.Windup, admission.State);
        var intervening = Run(session, 3, held: false);
        Assert.All(intervening, frame => Assert.Empty(frame.Queries));
        UyaShockCannonFrame muzzle = session.Advance(new(false));
        Assert.Equal(5, muzzle.Update);
        Assert.Equal(UyaShockCannonState.Discharge, muzzle.State);
        Assert.Contains(muzzle.Effects, effect => effect.Kind == UyaShockCannonEffectKind.Muzzle && effect.NativeFunctionAddress == 0x003877d8);
        Assert.Contains(muzzle.Audio, sound => sound.LocalSoundIndex == 4 && sound.MobyClass == 0x1882);
        Assert.Empty(muzzle.Queries);
        UyaShockCannonFrame hits = session.Advance(new(false));
        Assert.Equal(6, hits.Update);
        Assert.Equal(4, hits.Queries.Count(q => q.Kind == UyaShockCannonQueryKind.CloseSphere));
        Assert.Equal(23, hits.Queries.Count(q => q.Kind == UyaShockCannonQueryKind.RandomRay));
        Assert.Equal(3, hits.Queries.Count(q => q.Kind == UyaShockCannonQueryKind.FixedRay));
        Assert.Equal(UyaShockCannonState.Recovering, hits.State);
        Assert.Equal(49, hits.Ammo);
    }

    [Fact]
    public void CooldownDoesNotQueueATapAndAlternatePoseChangesWindupOnly()
    {
        var session = Equipped();
        Assert.Single(session.Advance(new(true, PrimaryTiming: false)).Shots);
        UyaShockCannonFrame muzzle = session.Advance(new(false));
        Assert.Equal(UyaShockCannonState.Discharge, muzzle.State);
        UyaShockCannonFrame hits = session.Advance(new(false, FullCloseSweep: false));
        Assert.Equal(1, hits.Queries.Count(q => q.Kind == UyaShockCannonQueryKind.CloseSphere));
        Assert.Empty(session.Advance(new(true)).Shots);
        Run(session, 18, held: false);
        Assert.Equal(UyaShockCannonState.Recovering, session.State);
        UyaShockCannonFrame ready = session.Advance(new(false));
        Assert.Equal(23, ready.Update);
        Assert.Equal(UyaShockCannonState.Ready, ready.State);
        Assert.Contains(ready.Audio, a => a.LocalSoundIndex == 2 && a.Operation == UyaShockCannonAudioOperation.Play);
        Assert.Empty(ready.Shots);
        Assert.Single(session.Advance(new(true)).Shots);
        Assert.Equal(48, session.Ammo);
    }

    [Fact]
    public void SustainedHoldChargesABeamRatherThanSpammingSpreadShots()
    {
        var session = Equipped();
        UyaShockCannonFrame[] frames = Run(session, 78, held: true);
        Assert.Single(frames.SelectMany(f => f.Shots));
        Assert.Equal(49, session.Ammo);
        UyaShockCannonFrame charged = session.Advance(new(true));
        UyaShockCannonShot shot = Assert.Single(charged.Shots);
        Assert.Equal(79, charged.Update);
        Assert.Equal(UyaShockCannonShotKind.ChargedBeam, shot.Kind);
        Assert.Equal(10, shot.AmmoSpent);
        Assert.Equal(39, charged.Ammo);
        Assert.Equal(29, charged.BeamUpdatesRemaining);
        Assert.Equal(20, charged.Queries.Count);
        Assert.All(charged.Queries, q =>
        {
            Assert.Equal(UyaShockCannonQueryKind.BeamSphere, q.Kind);
            Assert.Equal(1.5f, q.Radius);
            Assert.Equal(shot.Id, q.Shot);
            Assert.Equal(ObpSourceGame.Rac3, q.Weapon.SourceGame);
        });
        Assert.Equal(2, charged.Queries[0].ForwardOffset);
        Assert.Equal(40, charged.Queries[^1].ForwardOffset);
        var tail = Run(session, 29, held: false);
        Assert.All(tail, frame => Assert.Equal(20, frame.Queries.Count));
        Assert.Contains(tail.SelectMany(f => f.Audio), a => a.LocalSoundIndex == 0 && a.Operation == UyaShockCannonAudioOperation.Play);
        Assert.Contains(tail[^1].Effects, e => e.Kind == UyaShockCannonEffectKind.BeamStopped);
        Assert.DoesNotContain(tail[^1].Effects, e => e.Kind == UyaShockCannonEffectKind.Beam);
        Assert.Empty(session.Advance(new(false)).Queries);
    }

    [Fact]
    public void SpreadRequestsRetainNativeRotationArgumentsRatherThanAssumedEulerAxes()
    {
        var session = Equipped();
        session.Advance(new(true));
        UyaShockCannonFrame frame = Run(session, 5, held: false)[^1];
        var random = frame.Queries.First(q => q.Kind == UyaShockCannonQueryKind.RandomRay);
        Assert.Equal(32, random.Length);
        Assert.Equal(0, random.Rotation1From);
        Assert.Equal(-17 * .017453292f, random.Rotation1To);
        Assert.Equal(-170 * .017453292f, random.Rotation2From);
        Assert.Equal(170 * .017453292f, random.Rotation2To);
        var fixedRays = frame.Queries.Where(q => q.Kind == UyaShockCannonQueryKind.FixedRay).ToArray();
        Assert.All(fixedRays, ray => Assert.Equal(-.2617994f, ray.Rotation1From));
        Assert.Equal(new[] { -.34906584f, 0f, .34906584f }, fixedRays.Select(ray => ray.Rotation2From));
    }

    [Theory]
    [InlineData(2, 1)]
    [InlineData(6, 5)]
    [InlineData(11, 10)]
    public void ChargedShotAttemptsTenIndependentSingleAmmoSpends(int startingAmmo, int chargedCost)
    {
        var session = Equipped(startingAmmo);
        Run(session, 78, held: true);
        UyaShockCannonFrame beam = session.Advance(new(true));
        Assert.Equal(chargedCost, Assert.Single(beam.Shots).AmmoSpent);
        Assert.Equal(0, session.Ammo);
        UyaShockCannonDamageIntent damage = Assert.Single(session.ResolveContacts(beam,
            [new(0, Target(), 2, true, NativeMobyModeBits: 0x4000)]));
        Assert.Equal(50, damage.NativeDamage);
    }

    [Fact]
    public void HeldBeamCanRepeatAfterRecoveryWithoutReleasingTrigger()
    {
        var session = Equipped();
        var frames = Run(session, 200, held: true);
        var shots = frames.SelectMany(f => f.Shots).ToArray();
        Assert.Equal(new long[] { 1, 79, 104, 129, 154, 179 }, shots.Select(s => s.Update));
        Assert.Equal(0, session.Ammo);
        Assert.Equal(9, shots[^1].AmmoSpent);
        Assert.Equal(1, shots.Count(s => s.Kind == UyaShockCannonShotKind.Spread));
    }

    [Fact]
    public void DryFireNeverEmitsDamageOrSpendsAndPickupClampsToNativeCapacity()
    {
        var session = Equipped(0);
        var frames = Run(session, 200, held: true);
        Assert.Empty(frames.SelectMany(f => f.Shots));
        Assert.Empty(frames.SelectMany(f => f.Queries));
        Assert.Single(frames.SelectMany(f => f.Audio));
        Assert.Equal(1, frames[0].Audio[0].LocalSoundIndex);
        Assert.Equal(50, session.AddAmmo(int.MaxValue));
        Assert.Equal(0, session.AddAmmo(4));
        session.Advance(new(false));
        Assert.Single(session.Advance(new(true)).Shots);
        Assert.Equal(49, session.Ammo);
    }

    [Fact]
    public void NativeChargeLockCannotChargeOrStartAShot()
    {
        var session = Equipped();
        var blocked = Enumerable.Range(0, 100).Select(_ => session.Advance(new(true, ActionAdmitted: false, ChargeAdmitted: false))).ToArray();
        Assert.Empty(blocked.SelectMany(f => f.Shots));
        Assert.Equal(50, session.Ammo);
        Assert.Equal(0, blocked[^1].ChargeUpdates);
        Assert.Equal(UyaShockCannonShotKind.Spread, Assert.Single(session.Advance(new(true)).Shots).Kind);
    }

    [Fact]
    public void ChargingCanFinishWhilePlayerActionAdmissionIsBlocked()
    {
        var session = Equipped();
        for (int i = 0; i < 79; i++)
            Assert.Empty(session.Advance(new(true, ActionAdmitted: false)).Shots);
        UyaShockCannonFrame admitted = session.Advance(new(true));
        Assert.Equal(UyaShockCannonShotKind.ChargedBeam, Assert.Single(admitted.Shots).Kind);
        Assert.Equal(40, admitted.Ammo);
    }

    [Theory]
    [InlineData(0, 5)]
    [InlineData(9.999f, 5)]
    [InlineData(10, 3.75f)]
    [InlineData(13.999f, 3.75f)]
    [InlineData(14, 2.5f)]
    [InlineData(19.999f, 2.5f)]
    [InlineData(20, .625f)]
    [InlineData(32, .625f)]
    public void RayDistanceUsesNativeStrictBoundaries(float distance, float damage)
    {
        Assert.Equal(damage, UyaShockCannonProfile.RayDamage(distance));
    }

    [Fact]
    public void OverlappingContactsUseMaximumDamageAndRespectObstructionAndOwner()
    {
        var session = Equipped();
        session.Advance(new(true));
        UyaShockCannonFrame frame = Run(session, 5, held: false)[^1];
        int ray = frame.Queries.First(q => q.Kind == UyaShockCannonQueryKind.RandomRay).Index;
        var contacts = new UyaShockCannonContact[]
        {
            new(ray, Target(), 22, true),
            new(ray + 1, Target(), 10, true),
            new(0, Target(), 3, true),
            new(ray, Target(runtimeId: 20), 2, false),
            new(0, Target(runtimeId: 21), 1, true, IsOwner: true),
        };
        var intent = Assert.Single(session.ResolveContacts(frame, contacts));
        Assert.Equal(5, intent.NativeDamage);
        Assert.Equal(0x00410000u, intent.NativeFlags);
        Assert.Equal(3, intent.NativeRecordKind);
        Assert.Equal(2, intent.NativeOwnerKind);
        Assert.True(UyaClass500Destructible.ShouldBreak(intent.NativeFlags, intent.NativeDamage));
        UyaGameplayDamageEvent native = intent.ToUyaDamageEvent(123);
        Assert.Equal(0x1882, native.Source.NativeClassId);
        Assert.Equal(123, native.Source.RuntimeId);
        Assert.Equal(500, native.Target.NativeClassId);
        Assert.Throws<InvalidOperationException>(() => session.ResolveContacts(frame, []));
    }

    [Fact]
    public void BeamRequiresNativeDamageableModeAndDoesNotMultiplyByTwentySamples()
    {
        var session = Equipped();
        UyaShockCannonFrame frame = Run(session, 79, held: true)[^1];
        var contacts = frame.Queries.Select(q => new UyaShockCannonContact(q.Index, Target(), 5, true, 0x4000)).ToList();
        contacts.Add(new(0, Target(runtimeId: 2), 2, true));
        contacts.Add(new(0, Target(runtimeId: 3), 2, false, 0x4000));
        UyaShockCannonDamageIntent intent = Assert.Single(session.ResolveContacts(frame, contacts));
        Assert.Equal(50, intent.NativeDamage);
        Assert.Equal(0x00210000u, intent.NativeFlags);
        Assert.True(UyaClass500Destructible.ShouldBreak(intent.NativeFlags, intent.NativeDamage));
        UyaShockCannonFrame next = session.Advance(new(false));
        Assert.Single(session.ResolveContacts(next, [contacts[0]]));
    }

    [Fact]
    public void OverlappingBeamAndSpreadMergeFlagsWithoutAddingDamage()
    {
        var session = Equipped();
        Run(session, 79, held: true);
        Run(session, 24, held: false);
        Assert.Equal(UyaShockCannonShotKind.Spread,
            Assert.Single(session.Advance(new(true, PrimaryTiming: false)).Shots).Kind);
        session.Advance(new(false));
        UyaShockCannonFrame frame = session.Advance(new(false));
        int sphere = frame.Queries.First(q => q.Kind == UyaShockCannonQueryKind.CloseSphere).Index;
        var intent = Assert.Single(session.ResolveContacts(frame,
            [new(0, Target(), 4, true, 0x4000), new(sphere, Target(), 1, true)]));
        Assert.Equal(50, intent.NativeDamage);
        Assert.Equal(0x00610000u, intent.NativeFlags);
        Assert.Equal(frame.Queries[0].Shot, intent.Shot);
    }

    [Fact]
    public void CrossGameTargetCannotBeSilentlyReinterpretedAsAUyaNativeClass()
    {
        var session = Equipped();
        UyaShockCannonFrame frame = Run(session, 79, held: true)[^1];
        var target = new UyaShockCannonTarget(ObpSourceGame.Rac1, 500, 7);
        var intent = Assert.Single(session.ResolveContacts(frame, [new(0, target, 5, true, 0x4000)]));
        Assert.Equal(ObpSourceGame.Rac3, intent.Weapon.SourceGame);
        Assert.Equal(ObpSourceGame.Rac1, intent.Target.SourceGame);
        Assert.Throws<InvalidOperationException>(() => intent.ToUyaDamageEvent(5));
    }

    [Fact]
    public void ResetAndUnequipStopEffectsDiscardPendingWorkAndRejectOldContacts()
    {
        var session = Equipped();
        UyaShockCannonFrame old = Run(session, 79, held: true)[^1];
        UyaShockCannonFrame stopped = session.Unequip();
        Assert.Contains(stopped.Audio, a => a.Operation == UyaShockCannonAudioOperation.StopLoop);
        Assert.Contains(stopped.Effects, e => e.Kind == UyaShockCannonEffectKind.BeamStopped);
        Assert.Equal(39, stopped.Ammo);
        Assert.Empty(session.Advance(new(true)).Queries);
        Assert.Throws<InvalidOperationException>(() => session.ResolveContacts(old, []));
        session.Reset(12);
        Assert.Equal(12, session.Ammo);
        Assert.Equal(UyaShockCannonState.Unequipped, session.State);
        session.Equip();
        UyaShockCannonShot fresh = Assert.Single(session.Advance(new(true)).Shots);
        Assert.NotEqual(old.Queries[0].Shot.Generation, fresh.Id.Generation);
        Assert.Equal(1, fresh.Id.Sequence);
        Assert.Equal(11, session.Ammo);
    }

    [Fact]
    public void FramesCannotBeForgedMutatedOrResolvedByADifferentSession()
    {
        var session = Equipped();
        UyaShockCannonFrame frame = Run(session, 79, held: true)[^1];
        Assert.Throws<NotSupportedException>(() => ((IList<UyaShockCannonQuery>)frame.Queries).Clear());
        Assert.Throws<InvalidOperationException>(() => session.ResolveContacts(frame with { }, []));
        Assert.Throws<InvalidOperationException>(() => Equipped().ResolveContacts(frame, []));
        Assert.Throws<ArgumentException>(() => session.ResolveContacts(frame, [new(-1, Target(), 2, true)]));
        Assert.Throws<ArgumentException>(() => session.ResolveContacts(frame, [new(0, Target(), float.NaN, true)]));
        Assert.Single(session.ResolveContacts(frame, [new(0, Target(), 2, true, 0x4000)]));
    }

    [Fact]
    public void SameInputsAndHostFactsProduceIdenticalEventAndDamageHistory()
    {
        static string[] Record()
        {
            var session = Equipped();
            return Enumerable.Range(0, 220).Select(i =>
            {
                UyaShockCannonFrame frame = session.Advance(new(i < 115 || i >= 140));
                var damage = session.ResolveContacts(frame, frame.Queries.Select(q =>
                    new UyaShockCannonContact(q.Index, Target(), 12, true, 0x4000)).ToArray());
                return System.Text.Json.JsonSerializer.Serialize(new { frame, damage });
            }).ToArray();
        }
        Assert.Equal(Record(), Record());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(51)]
    public void InvalidAmmoCannotCorruptASession(int ammo)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new UyaShockCannonSession(ammo));
        var session = Equipped();
        Assert.Throws<ArgumentOutOfRangeException>(() => session.Reset(ammo));
        Assert.Equal(50, session.Ammo);
        Assert.Equal(UyaShockCannonState.Ready, session.State);
        Assert.Throws<ArgumentOutOfRangeException>(() => session.AddAmmo(-1));
    }

    private static UyaShockCannonSession Equipped(int ammo = 50)
    {
        var session = new UyaShockCannonSession(ammo);
        session.Equip();
        return session;
    }

    private static UyaShockCannonFrame[] Run(UyaShockCannonSession session, int updates, bool held) =>
        Enumerable.Range(0, updates).Select(_ => session.Advance(new(held))).ToArray();

    private static UyaShockCannonTarget Target(long runtimeId = 1) => new(ObpSourceGame.Rac3, 500, runtimeId);
}
