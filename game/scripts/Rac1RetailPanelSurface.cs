using Godot;
using OBP.RAC1.Presentation;

namespace OneBigPackage;

/// <summary>
/// Draws the menu panel in one CanvasItem shader pass. Unlike overlapping
/// Godot rectangles, the shader uses native GS source-over/additive equations
/// in source order and samples the already-rendered scene as the destination.
/// The rectangle is the host's projected panel bounds until the original
/// animated menu Moby meshes/camera are imported.
/// </summary>
public sealed partial class Rac1RetailPanelSurface : Control
{
    private ShaderMaterial? _material;
    private readonly RandomNumberGenerator _random = new();
    private Rac1RetailPanelBlend.Flash _flash;
    private double _tickFraction;
    private Vector2 _lastSize;

    public override void _Ready()
    {
        Name = "NativePanelComposite";
        MouseFilter = MouseFilterEnum.Ignore;
        ShowBehindParent = true;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        SetProcess(true);

        var baseFrame = Rac1RetailUiAssets.Sprite(0xE99E, 7);
        var streak = Rac1RetailUiAssets.Effect(0x1C);
        var surface = Rac1RetailUiAssets.Effect(0x19);
        var flash = Rac1RetailUiAssets.Effect(0x1A);
        var shader = GD.Load<Shader>("res://shaders/Rac1RetailPanel.gdshader");
        if (baseFrame is null || streak is null || surface is null || flash is null || shader is null)
        {
            GD.PushWarning("[retail-ui] Native panel compositor cannot load all source textures; disabling the panel.");
            Visible = false;
            return;
        }
        _material = new ShaderMaterial { Shader = shader };
        _material.SetShaderParameter("base_frame", baseFrame);
        _material.SetShaderParameter("streak_tex", streak);
        _material.SetShaderParameter("surface_tex", surface);
        _material.SetShaderParameter("flash_tex", flash);
        _material.SetShaderParameter("flash_enabled", false);
        Material = _material;
        _lastSize = Size;
        _material.SetShaderParameter("projected_size", Size);
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        if (_material is null) return;
        if (Size != _lastSize)
        {
            _lastSize = Size;
            _material.SetShaderParameter("projected_size", Size);
            QueueRedraw();
        }

        _tickFraction += Math.Clamp(delta, 0, .2) * 60.0;
        int ticks = Math.Min(12, (int)_tickFraction);
        _tickFraction -= ticks;
        for (int i = 0; i < ticks; i++)
        {
            bool trigger = !_flash.Active && _random.RandiRange(0, 1999) == 0;
            _flash = _flash.Step(trigger);
            if (trigger)
                _material.SetShaderParameter("flash_uv_start", new Vector2(
                    _random.RandiRange(0, 199), _random.RandiRange(0, 199)));
            // Retail samples new random UV offsets on every flashing update.
            else if (_flash.Active)
                _material.SetShaderParameter("flash_uv_start", new Vector2(
                    _random.RandiRange(0, 199), _random.RandiRange(0, 199)));
        }
        if (ticks == 0) return;
        _material.SetShaderParameter("flash_enabled", _flash.Active);
        _material.SetShaderParameter("flash_fix", _flash.AdditiveGain);
        if (_flash.Active || ticks > 0) QueueRedraw();
    }

    public override void _Draw()
    {
        if (_material is not null)
            DrawRect(new Rect2(Vector2.Zero, Size), Colors.White);
    }
}
