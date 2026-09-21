using OBP.Core;

namespace OBP.Runtime.Player;

/// <summary>
/// Source-game-neutral route from a user-owned retail source to one playable
/// avatar. Providers own source-specific parsing; consumers receive only the
/// local-space neutral contract.
/// </summary>
public interface IPlayerAvatarProvider
{
    ObpSourceGame SourceGame { get; }
    string BuildId { get; }
    string DefaultAvatarId { get; }

    bool CanLoad(string avatarId);

    PlayerAvatar Load(string sourcePath, string avatarId);
}
