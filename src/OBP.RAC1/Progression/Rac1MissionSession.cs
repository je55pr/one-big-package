using System.Buffers.Binary;
using System.Numerics;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Level;

namespace OBP.RAC1.Progression;

public sealed record Rac1InfobotAuthored(
    bool InitiallyActive, int Destination, int CheckpointCuboid,
    sbyte BeforeScene, sbyte Movie, sbyte AfterScene,
    int AttachmentCuboid, int LinkedActorIndex, int EscapePath,
    IReadOnlyList<int> PatrolPaths, bool AlternateActivation)
{
    public static Rac1InfobotAuthored Read(Rac1Instances.MobyInstance actor)
    {
        if (actor.OClass != 750 || actor.PVar is not { Length: 0xd0 } p)
            throw new InvalidDataException("Class-750 Infobot requires its native 0xd0-byte PVar.");
        int I(int offset) => BinaryPrimitives.ReadInt32LittleEndian(p.AsSpan(offset));
        int destination = I(4);
        if ((uint)destination >= Rac1CampaignState.NativeDestinationCount)
            throw new InvalidDataException("Infobot has an unsupported native destination.");
        return new(I(0) != 0, destination, I(8), unchecked((sbyte)p[12]), unchecked((sbyte)p[13]),
            unchecked((sbyte)p[14]), I(0x50), I(0x54), I(0x58),
            Enumerable.Range(0, 8).Select(i => I(0x70 + i * 4)).ToArray(), I(0xb0) != 0);
    }
}

public sealed record Rac1MissionActorProbe(
    Rac1MissionActorKey Key, Vector3 NativePosition, int NativeState,
    bool Visible, bool Retired, bool UnsupportedMotion, int? Destination,
    int? TalkRecord, int? LastPlayedRecord);
public sealed record Rac1MissionAdmission(Rac1MissionActorKey Owner, int NativeState, int Destination);
public sealed record Rac1MissionLevelExit(Rac1MissionActorKey Owner, int Destination);
public sealed record Rac1MissionTick(
    IReadOnlyList<Rac1MissionAdmission> Admissions, Rac1MissionLevelExit? LevelExit, bool WalletChanged);

/// <summary>
/// Compiled Moby-owned mission producers. This does not consume the cheat-code
/// router's 0x25..0x36 action numbers or turn arbitrary host triggers into unlocks.
/// Admission remains the shared, persistent 0x2607d0 operation on campaign state.
/// </summary>
public sealed class Rac1MissionSession(int nativeLevelId, Rac1CampaignState campaign, Rac1BoltWallet wallet,
    IReadOnlyList<Rac1ClipVolume>? clipVolumes = null)
{
    private sealed class Actor(Rac1MissionActorKey key, Rac1Instances.MobyInstance source)
    {
        public Rac1MissionActorKey Key { get; } = key;
        public Vector3 Position { get; } = new(source.Position.X, source.Position.Y, source.Position.Z);
        public double Yaw { get; } = source.Rotation.Z;
        public Rac1InfobotAuthored? Infobot;
        public Rac1TalkSession? Talk;
        public int State;
        public bool Visible = true;
        public bool Retired;
        public bool UnsupportedMotion;
        public Rac1MissionSceneRequest? Scene;
        public long SceneSerial;
        public double TalkReach;
        public Rac1ClipVolume? TalkVolume;
    }

    private readonly List<Actor> _actors = [];
    private bool _exitIssued;
    public int NativeLevelId { get; } = nativeLevelId;
    public int RegisteredCount => _actors.Count;
    public Rac1MissionSceneRequest? PendingScene => _actors.Select(a => a.Talk?.PendingScene ?? a.Scene).FirstOrDefault(s => s is not null);
    public IReadOnlyList<Rac1MissionActorProbe> Probe() => _actors.Select(a => new Rac1MissionActorProbe(
        a.Key, a.Position, a.State, a.Visible && !a.Retired, a.Retired, a.UnsupportedMotion,
        a.Infobot?.Destination, a.Talk?.RecordIndex, a.Talk?.LastPlayed)).ToArray();

    public void RegisterInfobot(Rac1Instances.MobyInstance source)
    {
        var actor = Add(source);
        actor.Infobot = Rac1InfobotAuthored.Read(source);
    }

    public void RegisterTalkActor(Rac1Instances.MobyInstance source, Rac1TalkProgram program, bool postOpeningIntro = false)
    {
        if (!(NativeLevelId == 0 && source.OClass == 834 || NativeLevelId == 1 && source.OClass == 774))
            throw new ArgumentException("This compiled talk-actor variant has not been recovered.", nameof(source));
        var actor = Add(source);
        actor.Talk = new(actor.Key, program, wallet, talkedBefore: postOpeningIntro);
        if (source.OClass == 834)
        {
            actor.TalkReach = postOpeningIntro ? 3d : 255d;
            int cuboid = source.PVar is { Length: >= 0x64 } p ? BinaryPrimitives.ReadInt32LittleEndian(p.AsSpan(0x60)) : -1;
            if (postOpeningIntro && clipVolumes is not null && cuboid >= 0)
            {
                if (cuboid >= clipVolumes.Count) throw new InvalidDataException("Veldin talk actor cuboid is out of range.");
                actor.TalkVolume = clipVolumes[cuboid];
            }
        }
        else
        {
            if (source.PVar is not { Length: >= 0x40 } p) throw new InvalidDataException("Plumber lacks its native talk PVar.");
            actor.TalkReach = BinaryPrimitives.ReadSingleLittleEndian(p.AsSpan(0xc));
            if (!double.IsFinite(actor.TalkReach) || actor.TalkReach <= 0) throw new InvalidDataException("Invalid native talk reach.");
        }
        // OBP's existing controllable Veldin start is after the opening scene.
        // Revisited completed Veldin has no serialized talker owner in schema 2;
        // suppressing that finished opening encounter is a conservative host policy.
        if (source.OClass == 834 && campaign.GetLevelState(0) == Rac1LevelVisitState.Completed)
            actor.Retired = true;
    }

    private Actor Add(Rac1Instances.MobyInstance source)
    {
        var key = new Rac1MissionActorKey(NativeLevelId, source.OClass, source.Index);
        if (_actors.Any(a => a.Key == key)) throw new InvalidOperationException("Duplicate native mission actor.");
        var actor = new Actor(key, source);
        _actors.Add(actor);
        return actor;
    }

    public bool CanInteract(Rac1MissionPlayerFacts facts) => PendingScene is null && _actors.Any(a =>
        !a.Retired && a.State == 1 && a.Talk is { } talk &&
        talk.CanInteract(facts, a.Position, a.Yaw, TalkReach(a, facts)));

    private static double TalkReach(Actor actor, Rac1MissionPlayerFacts player) =>
        actor.TalkVolume?.Contains(player.NativePosition) == true ? 16d : actor.TalkReach;

    public bool CompleteScene(Rac1MissionSceneRequest request)
    {
        foreach (var actor in _actors)
        {
            if (actor.Talk?.CompleteScene(request) == true) return true;
            if (actor.Scene is null || !ReferenceEquals(actor.Scene, request)) continue;
            actor.Scene = null;
            return true;
        }
        return false;
    }

    public Rac1MissionTick Step(Rac1MissionPlayerFacts player)
    {
        var admissions = new List<Rac1MissionAdmission>();
        Rac1MissionLevelExit? exit = null;
        int before = wallet.Balance;
        if (PendingScene is not null || player.GameplayInhibited)
            return new(admissions, null, false);
        foreach (var actor in _actors)
        {
            if (actor.Retired) continue;
            if (actor.Infobot is { } infobot)
                StepInfobot(actor, infobot, player, admissions);
            else if (actor.Talk is { } talk)
            {
                if (actor.State == 0)
                {
                    actor.State = 1;
                    if (actor.Key.NativeClassId == 774 && campaign.CanSelectDestination(2)) actor.Retired = true;
                }
                else if (actor.State == 1)
                {
                    if (talk.TryStart(player, actor.Position, actor.Yaw, TalkReach(actor, player)))
                    {
                        actor.State = 2;
                        if (actor.Key.NativeClassId == 834) actor.TalkReach = 3d;
                    }
                    // Veldin class 834 branches after talk_update, not in a generic dialogue engine.
                    if (actor.Key.NativeClassId == 834 && talk.LastPlayed >= 4 && !_exitIssued)
                    {
                        _exitIssued = true;
                        exit = new(actor.Key, 1); // 0x2d9f40: level_exit_to(1).
                    }
                }
                else if (actor.State == 2)
                {
                    actor.State = 1;
                    if (actor.Key.NativeClassId == 774 && talk.LastPlayed == 4)
                    {
                        // L1 0x2ff2f4: level_mark_visited(2), then record state 3 and retire.
                        Admit(actor, 2, admissions);
                        actor.State = 3;
                    }
                }
                else if (actor.State == 3 && actor.Key.NativeClassId == 774)
                    actor.Retired = true;
            }
            if (PendingScene is not null || exit is not null) break;
        }
        return new(admissions, exit, wallet.Balance != before);
    }

    private void StepInfobot(Actor actor, Rac1InfobotAuthored infobot, Rac1MissionPlayerFacts player, List<Rac1MissionAdmission> admissions)
    {
        switch (actor.State)
        {
            case 0:
                actor.State = infobot.InitiallyActive || infobot.AlternateActivation ? 2 : 1;
                actor.Visible = infobot.InitiallyActive;
                if (infobot.AttachmentCuboid != -1) actor.State = 5;
                else if (infobot.PatrolPaths[0] != -1) actor.State = 3;
                actor.UnsupportedMotion = actor.State is 3 or 5;
                break;
            case 2:
                if (campaign.CanSelectDestination(infobot.Destination)) { actor.Retired = true; break; }
                double reach = infobot.Destination == 12 || infobot.AlternateActivation ? 3d : 1d;
                if (player.Nanotech != 0 && Vector3.Distance(actor.Position, player.NativePosition) < reach)
                    actor.State = 8;
                break;
            case 8:
                Admit(actor, infobot.Destination, admissions);
                actor.Visible = false;
                actor.State = 9;
                RequestInfobotScene(actor, infobot.BeforeScene, Rac1MissionSceneKind.Cutscene);
                break;
            case 9:
                actor.State = 10;
                RequestInfobotScene(actor, infobot.Movie, Rac1MissionSceneKind.Movie);
                break;
            case 10:
                actor.State = 11;
                RequestInfobotScene(actor, infobot.AfterScene, Rac1MissionSceneKind.Cutscene);
                break;
            case 11:
                actor.Retired = true;
                break;
        }
    }

    private void Admit(Actor actor, int destination, List<Rac1MissionAdmission> admissions)
    {
        if (campaign.AdmitDestination(destination)) admissions.Add(new(actor.Key, actor.State, destination));
    }

    private static void RequestInfobotScene(Actor actor, sbyte scene, Rac1MissionSceneKind kind)
    {
        if (scene != -1) actor.Scene = new(actor.Key, ++actor.SceneSerial, kind, scene);
    }
}
