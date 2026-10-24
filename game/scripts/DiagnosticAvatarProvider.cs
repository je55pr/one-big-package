using OBP.Core;
using OBP.Runtime.Player;

namespace OneBigPackage;

/// <summary>An explicit asset-free diagnostic, never labeled as a native Ratchet.</summary>
public sealed class DiagnosticAvatarProvider : IPlayerAvatarProvider
{
    public static DiagnosticAvatarProvider Instance { get; } = new();
    public ObpSourceGame SourceGame => ObpSourceGame.Synthetic;
    public string BuildId => "obp-diagnostic-v1";
    public string DefaultAvatarId => "capsule";
    public bool CanLoad(string avatarId) => avatarId == DefaultAvatarId;
    public PlayerAvatar Load(string sourcePath, string avatarId) => throw new NotSupportedException("The diagnostic capsule is host geometry, not a native avatar asset.");
}
