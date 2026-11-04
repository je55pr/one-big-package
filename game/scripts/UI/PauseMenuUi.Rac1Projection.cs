using Godot;
using OBP.RAC1.Presentation;

namespace OBP.Game;

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
            ("Weapons", Rac1RetailMenuPage.Weapons),
            ("Gadgets", Rac1RetailMenuPage.Gadgets),
            ("Quick Select", Rac1RetailMenuPage.QuickSelect),
            ("Items", Rac1RetailMenuPage.Items),
            ("Help", Rac1RetailMenuPage.Help),
            ("Options", Rac1RetailMenuPage.Options),
        };
        var sourceChoices = Rac1RetailUiAssets.NativeMenuPages?.NativeRootEntries;
        if (sourceChoices is not { Count: 7 } ||
            sourceChoices.Take(names.Length).Any(choice => choice.RequiresNativeAdmission) ||
            !sourceChoices[6].RequiresNativeAdmission)
            throw new InvalidDataException("Original pause root choices or conditional seventh slot are missing.");
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
            var (fallback, target) = names[slot];
            int id = sourceChoices[slot].TextId;
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
