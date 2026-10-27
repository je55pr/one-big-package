using Godot;
using OBP.RAC1.Player;
using OBP.Runtime.Player;
using NVector3 = System.Numerics.Vector3;

namespace OBP.Game.Diagnostics;

internal sealed record GodotRac1WrenchDiagnosticBindings(Func<PlayerHost?> Player, Func<Rac1WrenchView?> Wrench,
    Func<PlayerAvatar?> Avatar, Func<IPlayerAnimationPresentationController?> Animation, Func<long> Generation,
    Func<bool> ThrowActive, Func<Vector3> WorldPosition, Func<bool> GloveVisible);
internal sealed class GodotRac1WrenchDiagnosticFacts(GodotRac1WrenchDiagnosticBindings bindings) : IRac1WrenchDiagnosticFacts
{
    public Rac1WrenchDiagnostic ReadWrench()
    {
        var player = bindings.Player();
        if (player is null || !GodotObject.IsInstanceValid(player)) throw new DiagnosticUnavailableException("The wrench fixture player is unavailable.");
        var view = bindings.Wrench();
        if (view is not null && !GodotObject.IsInstanceValid(view)) view = null;
        var hand = (bindings.Animation() as Rac1PlayerAnimationPresentationController)?.CurrentAttachmentPose(Rac1WrenchAttachment.SocketKey);
        var avatar = bindings.Avatar();
        NVector3? expected = hand is null || avatar is null ? null : GodotMovementPlayer.Copy(
            OBP.Godot.Player.PlayerAvatarView.ToGodotLocal(hand.Origin, avatar.Axes, avatar.BaseHeight, true));
        return new(player.Rac1WrenchFrame, player.Rac1WrenchAnimationBlending, bindings.Generation(), bindings.ThrowActive(),
            GodotMovementPlayer.Copy(bindings.WorldPosition()), view is not null, view?.Visible ?? false, bindings.GloveVisible(),
            view?.NativeSequence ?? -1, view is null ? NVector3.Zero : GodotMovementPlayer.Copy(view.HandLocal), expected);
    }
}
