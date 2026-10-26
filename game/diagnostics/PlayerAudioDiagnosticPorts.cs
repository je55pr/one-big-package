using System.Collections.Immutable;
using System.Numerics;
using OBP.RAC1.Gameplay;

namespace OBP.Game.Diagnostics;

public enum DiagnosticPlayerButton { Jump, Crouch, Wrench, Traversal, MoveRight }
public sealed record DiagnosticAudioVoice(string Name, bool Pcm, bool ForwardLoop, bool Playing, bool Paused, bool CanProcess);
public sealed record DiagnosticAudioHostState(bool BankReady, int? LevelId, int? PackSound, int? ThrownWrenchSound,
    bool WeaponLoop, bool WrenchThrowActive, int DestroyedCrates, int Nanotech, bool Dead);
public sealed record DiagnosticAudioPlayerState(bool PlayerPresent, bool Grounded, bool NativeGrounded, int SurfaceEffectMode, int? TraversalAction,
    int? PreviewPack, bool WrenchActive, bool WrenchBlending, double WrenchAnimationTime,
    bool TreePaused, ImmutableArray<DiagnosticAudioVoice> Voices, DiagnosticAudioHostState Host);
public interface IDiagnosticPlayerAudioFixture
{
    DiagnosticAudioPlayerState Read();
    IDisposable BeginStage();
    void ResetPosition(Vector3 offset);
    void CyclePack();
    void SetAction(DiagnosticPlayerButton button, bool pressed);
    void SetAnalogue(float right, float forward);
    void SetConditioned(double right, double forward);
    void ClearInput();
    bool Back();
    void SelectWeapon(Rac1WeaponId weapon);
    void SetEquipmentPreview(bool disable);
    void StageRewardCrateInFront();
    void ApplyContactHit();
}
