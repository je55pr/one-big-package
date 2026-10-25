using System.Text;
using Godot;
using OBP.RAC1.Presentation;

namespace OBP.Game;

/// <summary>
/// A source glyph-quad painter for the original ordinary pause root.
/// The original R&C1 large font is 16x16 pixels per glyph; preserve native
/// advances, offsets, and the one-entry text-list centering rule. We retain
/// the previous host 25-pixel display size as an explicit adaptation until
/// PCSX2 output dimensions and raster scaling are frame matched.
/// </summary>
public sealed partial class Rac1RetailRootText : Control
{
    public const float HostGlyphScale = 25f / Rac1MenuRootTextLayout.GlyphCellSize;
    private bool? _selected;
    private Vector2 _lastSize;

    public override void _Ready()
    {
        Name = "NativeRootGlyphs";
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        SetProcess(true);
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        if (GetParent() is not Rac1RetailMenuButton button) return;
        bool selected = button.HasFocus() || button.IsHovered();
        if (_selected != selected || _lastSize != Size)
        {
            _selected = selected;
            _lastSize = Size;
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        if (GetParent() is not Rac1RetailMenuButton button ||
            Rac1RetailUiAssets.NativeRootFont is not { } font ||
            Rac1RetailUiAssets.NativeRootGlyphAtlas() is not { } atlas ||
            Size.X <= 0 || Size.Y <= 0)
            return;
        int nativeWidth = Math.Max(1, (int)MathF.Round(Size.X / HostGlyphScale));
        int nativeHeight = Math.Max(1, (int)MathF.Round(Size.Y / HostGlyphScale));
        var native = Rac1MenuRootTextLayout.Layout(
            Encoding.Latin1.GetBytes(button.Text), font.Glyphs,
            nativeWidth, nativeHeight);
        Vector2 offset = (Size - new Vector2(nativeWidth, nativeHeight) * HostGlyphScale) / 2;
        var color = button.HasFocus() || button.IsHovered()
            ? Rac1RetailUiSkin.NativeSelected : Rac1RetailUiSkin.NativeNormal;
        const float cell = Rac1MenuRootTextLayout.GlyphCellSize * HostGlyphScale;
        foreach (var glyph in native.Quads)
        {
            DrawTextureRectRegion(atlas,
                new Rect2(offset + new Vector2(glyph.X, glyph.Y) * HostGlyphScale,
                    new Vector2(cell, cell)),
                new Rect2(glyph.U, glyph.V,
                    Rac1MenuRootTextLayout.GlyphCellSize,
                    Rac1MenuRootTextLayout.GlyphCellSize),
                color);
        }
    }
}
