using Godot;
using OBP.RAC1.Presentation;

namespace OneBigPackage;

public sealed partial class PauseMenuUi
{
    private Vector2 _nativeRootLastViewportSize;

    /// <summary>
    /// The first six original class-0x472 animation panels, projected by the
    /// recovered reset camera and NTSC-U FOV. Only the ordinary pause root
    /// has a verified source-page correspondence; OBP-specific subpages
    /// remain on their existing safe host layouts.
    /// </summary>
    private bool BuildNativeProjectedRoot()
    {
        if (_retailNativeRootOverlay is not { } overlay ||
            Rac1RetailUiAssets.CreateNativePanelMotion() is not { } motion)
            return false;
        var names = new[]
        {
            (20194, "Weapons", Rac1RetailMenuPage.Weapons),
            (20195, "Gadgets", Rac1RetailMenuPage.Gadgets),
            (20196, "Quick Select", Rac1RetailMenuPage.QuickSelect),
            (20197, "Items", Rac1RetailMenuPage.Items),
            (20198, "Help", Rac1RetailMenuPage.Help),
            (20199, "Options", Rac1RetailMenuPage.Options),
        };
        // The original 14 panels initialise at each sequence's last
        // frame. These original six menu choices are displayed at frame 0;
        // their outgoing animations run for the native twelve updates.
        motion.BeginTransition(Rac1MenuPanelPages.Page.OrdinaryPause, backwards: true);
        for (int i = 0; i < Rac1RetailMenuNavigation.TransitionTicks; i++)
            motion.Tick();
        _retailNativeRootMotion = motion;
        overlay.Visible = true;
        for (int slot = 0; slot < names.Length; slot++)
        {
            var (id, fallback, target) = names[slot];
            var button = new Rac1RetailMenuButton
            {
                Name = $"NativePanelSlot_{slot}",
                Text = Rac1RetailUiSkin.NativeText(id, fallback),
                FocusMode = Control.FocusModeEnum.All,
                RetailPauseRoot = true,
            };
            button.FocusEntered += () => Rac1RetailMenuAudio.Play(0);
            button.Pressed += () =>
            {
                Rac1RetailMenuAudio.Play(1);
                NavigateRetail(target);
            };
            overlay.AddChild(button);
            _retailNativeRootButtons.Add(button);
            _retailFocusOrder.Add(button);
        }
        UpdateNativeRootPanels();
        return true;
    }

    private void UpdateNativeRootPanels()
    {
        if (_retailNativeRootMotion is not { } motion ||
            _retailNativeRootOverlay is not { Visible: true })
            return;
        var viewport = GetViewport().GetVisibleRect().Size;
        _nativeRootLastViewportSize = viewport;
        for (int slot = 0; slot < _retailNativeRootButtons.Count; slot++)
        {
            var projected = Rac1MenuPanelCamera.ProjectPanel(motion.Pose(slot),
                new System.Numerics.Vector2(viewport.X, viewport.Y));
            var button = _retailNativeRootButtons[slot];
            button.Position = new Vector2(projected.X, projected.Y);
            button.Size = new Vector2(projected.Width, projected.Height);
        }
    }
}
