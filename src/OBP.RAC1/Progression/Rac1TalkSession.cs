using System.Numerics;
using OBP.RAC1.Gameplay;

namespace OBP.RAC1.Progression;

public readonly record struct Rac1MissionActorKey(int LevelId, int NativeClassId, int InstanceIndex);
public enum Rac1MissionSceneKind { Cutscene, Movie }
public sealed record Rac1MissionSceneRequest(
    Rac1MissionActorKey Owner, long Serial, Rac1MissionSceneKind Kind, int NativeSceneId);
public readonly record struct Rac1MissionPlayerFacts(
    Vector3 NativePosition, double NativeYaw, int Nanotech, int NativePlayerState = 0,
    bool InteractPressed = false, bool GameplayInhibited = false);

/// <summary>
/// 0x2668a0 / 0x266448 / 0x266970. Actor admission, talk records and scene
/// completion are separate owners. The consumer must acknowledge the exact
/// outstanding request; an old/duplicate callback cannot advance the program.
/// </summary>
public sealed class Rac1TalkSession
{
    private readonly Rac1TalkProgram _program;
    private readonly Rac1BoltWallet _wallet;
    private readonly Func<short, short, bool?>? _otherCondition;
    private long _serial;

    public Rac1TalkSession(Rac1MissionActorKey owner, Rac1TalkProgram program, Rac1BoltWallet wallet,
        bool talkedBefore = false, Func<short, short, bool?>? otherCondition = null)
    {
        if (owner.LevelId != program.NativeLevelId || !program.Records.ContainsKey(0))
            throw new ArgumentException("Talk program does not belong to this native actor's level.");
        Owner = owner; _program = program; _wallet = wallet; _otherCondition = otherCondition;
        TalkedBefore = talkedBefore;
        RecordIndex = talkedBefore && program.Records[0].Condition == 0 ? program.Records[0].Next : 0;
        LastPlayed = talkedBefore ? 0 : -1;
    }

    public Rac1MissionActorKey Owner { get; }
    public int RecordIndex { get; private set; }
    public int LastPlayed { get; private set; }
    public bool TalkedBefore { get; private set; }
    public Rac1MissionSceneRequest? PendingScene { get; private set; }
    public int CurrentTextId => RecordIndex == -1 ? 0 : _program.Records[RecordIndex].TextId;

    public bool CanInteract(Rac1MissionPlayerFacts facts, Vector3 actorPosition, double actorYaw, double reach)
    {
        if (PendingScene is not null || facts.GameplayInhibited || facts.Nanotech == 0 ||
            facts.NativePlayerState == 0x1d || RecordIndex == -1 ||
            Vector3.Distance(facts.NativePosition, actorPosition) > reach + reach)
            return false;
        if (!RefreshCondition()) return false;
        var record = _program.Records[RecordIndex];
        if (record.Automatic) return true;
        var toHero = facts.NativePosition - actorPosition;
        double towardHero = Math.Atan2(toHero.Y, toHero.X);
        return AngleDistance(towardHero, actorYaw) <= reach &&
            AngleDistance(towardHero + Math.PI, facts.NativeYaw) <= 1.5700000524520874d;
    }

    public bool TryStart(Rac1MissionPlayerFacts facts, Vector3 actorPosition, double actorYaw, double reach)
    {
        if (!CanInteract(facts, actorPosition, actorYaw, reach)) return false;
        var record = _program.Records[RecordIndex];
        if (!record.Automatic && !facts.InteractPressed) return false;
        if (record.PayConditionCost)
        {
            // Other condition kinds retain their records but are not admitted
            // until their native inventory/payment owner is supplied.
            if (record.Condition != 1 || !_wallet.TrySpend(_program.BoltPrices[record.Argument])) return false;
        }
        TalkedBefore = true;
        if (record.Scene == -1) Advance();
        else RequestScene(record.Scene);
        return true;
    }

    public bool CompleteScene(Rac1MissionSceneRequest request)
    {
        if (PendingScene is null || !ReferenceEquals(PendingScene, request)) return false;
        PendingScene = null;
        Advance();
        return true;
    }

    private bool RefreshCondition()
    {
        var seen = new HashSet<int>();
        while (RecordIndex != -1)
        {
            if (!seen.Add(RecordIndex)) throw new InvalidDataException("Cyclic R&C1 talk condition graph.");
            var record = _program.Records[RecordIndex];
            if (record.Condition == 0) return true;
            bool? holds = record.Condition == 1
                ? _wallet.Balance >= _program.BoltPrices[record.Argument]
                : _otherCondition?.Invoke(record.Condition, record.Argument);
            if (holds is null) return false;
            int next = holds.Value ? record.WhenTrue : record.WhenFalse;
            if (next == RecordIndex) return true;
            if (next != -1 && !_program.Records.ContainsKey(next)) throw new InvalidDataException("Missing R&C1 talk condition record.");
            RecordIndex = next;
        }
        return false;
    }

    private void Advance()
    {
        var record = _program.Records[RecordIndex];
        LastPlayed = RecordIndex;
        RecordIndex = record.Next;
        if (RecordIndex == -1) return;
        if (!_program.Records.TryGetValue(RecordIndex, out var next)) throw new InvalidDataException("Missing R&C1 next talk record.");
        if (record.ChainNextScene && next.Scene != -1) RequestScene(next.Scene);
        else _ = RefreshCondition();
    }

    private void RequestScene(short scene) => PendingScene = new(Owner, ++_serial,
        (scene & 0x4000) == 0 ? Rac1MissionSceneKind.Cutscene : Rac1MissionSceneKind.Movie,
        (scene & 0x4000) == 0 ? scene : scene ^ 0x4000);

    private static double AngleDistance(double a, double b) => Math.Abs(Math.Atan2(Math.Sin(a - b), Math.Cos(a - b)));
}
