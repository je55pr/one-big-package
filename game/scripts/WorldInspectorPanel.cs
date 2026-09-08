using Godot;
using OBP.Godot;
using OBP.Runtime;
using OBP.Runtime.Presentation;

namespace OneBigPackage;

/// <summary>
/// The interactive world inspector: a toggle-able panel that shows the neutral
/// <see cref="WorldObjectDescriptor"/> for whatever is under the crosshair (press
/// <b>I</b>) or the mouse (left-click while the cursor is free). All native-format
/// interpretation stays in <c>OBP.Runtime</c> — this only renders text and draws
/// a selection highlight.
/// </summary>
public sealed partial class WorldInspectorPanel : CanvasLayer
{
    private readonly RuntimeWorldScene.Result _result;
    private readonly RuntimeWorld _world;
    private readonly WorldHost _host;

    private PanelContainer _panel = null!;
    private RichTextLabel _text = null!;
    private ColorRect _crosshair = null!;
    private MeshInstance3D? _highlight;

    public WorldInspectorPanel(RuntimeWorldScene.Result result, RuntimeWorld world, WorldHost host)
    {
        _result = result;
        _world = world;
        _host = host;
        Name = "WorldInspector";
    }

    public override void _Ready()
    {
        _crosshair = new ColorRect
        {
            Color = new Color(1f, 1f, 1f, 0.6f),
            Size = new Vector2(10, 10),
            Visible = false,
        };
        AddChild(_crosshair);

        _panel = new PanelContainer { Position = new Vector2(14, 220), Visible = false };
        _panel.CustomMinimumSize = new Vector2(430, 0);
        var margin = new MarginContainer();
        foreach (var s in new[] { "margin_left", "margin_right", "margin_top", "margin_bottom" })
        {
            margin.AddThemeConstantOverride(s, 10);
        }

        _panel.AddChild(margin);
        _text = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = true,
            CustomMinimumSize = new Vector2(410, 0),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        margin.AddChild(_text);
        AddChild(_panel);
    }

    public bool IsOpen => _panel.Visible;

    public void Toggle()
    {
        bool show = !_panel.Visible;
        _panel.Visible = show;
        _crosshair.Visible = show;
        if (show)
        {
            LayoutCrosshair();
            PickAt(GetViewport().GetVisibleRect().Size * 0.5f);
        }
        else
        {
            ClearHighlight();
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!_panel.Visible)
        {
            return;
        }

        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click
            && Input.MouseMode == Input.MouseModeEnum.Visible)
        {
            PickAt(click.Position);
            GetViewport().SetInputAsHandled();
        }
    }

    private void LayoutCrosshair()
    {
        var c = GetViewport().GetVisibleRect().Size * 0.5f;
        _crosshair.Position = c - (_crosshair.Size * 0.5f);
    }

    private void PickAt(Vector2 screenPoint)
    {
        var cam = GetViewport().GetCamera3D();
        if (cam is null)
        {
            _text.Text = "[i]no active camera[/i]";
            return;
        }

        var hit = WorldPicker.Pick(cam, screenPoint, _result, _world, _host.AnimationStateByName);
        if (hit is null)
        {
            _text.Text = "[i]nothing under the " + (screenPoint == GetViewport().GetVisibleRect().Size * 0.5f ? "crosshair" : "cursor") + "[/i]";
            ClearHighlight();
            return;
        }

        var d = WorldObjectDescriptorBuilder.Build(_world, hit);
        _text.Text = Format(d);
        Highlight(d);
    }

    private static string Format(WorldObjectDescriptor d)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"[b]{d.Category}[/b]   [color=#9cf]{d.AssetKind}[/color] · tex {d.TextureId}");

        if (d.TextureWidth is { } w && d.TextureHeight is { } h)
        {
            string a = d.TextureAlpha is { } ap
                ? $"  alpha op {ap.OpaqueFraction:P0} / cut {ap.CutoutFraction:P0} / trans {ap.TranslucentFraction:P0}"
                : "";
            sb.AppendLine($"texture {w}×{h}{a}");
        }

        sb.AppendLine($"material: {(d.BackFaceCulled ? "back-face culled" : "2-sided")}{(d.VertexColoured ? " · vertex-coloured" : "")}");

        if (d.HitPoint is { } p)
        {
            sb.AppendLine($"hit ({p.X:0.0}, {p.Y:0.0}, {p.Z:0.0}) OBP" + (d.TriangleIndex is { } ti ? $"  tri #{ti}" : ""));
        }

        if (d.NativeClassId is { } cls)
        {
            sb.AppendLine();
            sb.AppendLine($"[b]native[/b] class {cls} · instance {d.InstanceIndex} · uid {(d.NativeUid?.ToString() ?? "—")}");
            sb.AppendLine($"model {d.ModelRef} · interaction {d.InteractionId}");
            if (d.Transform is { } t)
            {
                sb.AppendLine($"pos ({t.Translation.X:0.0}, {t.Translation.Y:0.0}, {t.Translation.Z:0.0})  " +
                              $"scale ({t.Scale.X:0.00}, {t.Scale.Y:0.00}, {t.Scale.Z:0.00})");
                sb.AppendLine($"quat ({t.Rotation.X:0.000}, {t.Rotation.Y:0.000}, {t.Rotation.Z:0.000}, {t.Rotation.W:0.000})");
            }

            if (d.LocalBounds is { } lb)
            {
                var s = lb.Max - lb.Min;
                sb.AppendLine($"local bounds {s.X:0.0} × {s.Y:0.0} × {s.Z:0.0}  ({d.TriangleCount} tris)");
            }
        }

        if (d.Payloads.Count > 0)
        {
            sb.Append("payloads: ");
            sb.AppendLine(string.Join(", ", d.Payloads.Select(pl => $"{pl.Format} ({pl.ByteLength} B)")));
        }

        if (d.Animation is { } an)
        {
            sb.AppendLine();
            sb.AppendLine($"[b]animation[/b] {an.Name} · {an.FramesPerSecond:0.#} fps · frame {an.CurrentFrame}/{an.FrameCount} · " +
                          $"{(an.Playing ? "playing" : "paused")}{(an.HasSkeleton ? " · has skeleton" : "")}");
        }

        sb.AppendLine();
        sb.AppendLine($"[color=#888]{d.Game} · {d.BuildId} · level {d.LevelId} · {d.WorldName}[/color]");
        return sb.ToString();
    }

    // --- selection highlight ---------------------------------------------

    private void Highlight(WorldObjectDescriptor d)
    {
        ClearHighlight();
        if (d.LocalBounds is not { } lb || d.Transform is null)
        {
            return;
        }

        // A wire box at the picked dynamic object's local bounds, under the
        // world root (X-mirrored into scene space).
        var lo = RuntimeWorldScene.ToScene(lb.Min.X, lb.Min.Y, lb.Min.Z);
        var hi = RuntimeWorldScene.ToScene(lb.Max.X, lb.Max.Y, lb.Max.Z);
        var min = new Vector3(Mathf.Min(lo.X, hi.X), Mathf.Min(lo.Y, hi.Y), Mathf.Min(lo.Z, hi.Z));
        var max = new Vector3(Mathf.Max(lo.X, hi.X), Mathf.Max(lo.Y, hi.Y), Mathf.Max(lo.Z, hi.Z));

        Vector3[] c =
        {
            new(min.X, min.Y, min.Z), new(max.X, min.Y, min.Z), new(max.X, min.Y, max.Z), new(min.X, min.Y, max.Z),
            new(min.X, max.Y, min.Z), new(max.X, max.Y, min.Z), new(max.X, max.Y, max.Z), new(min.X, max.Y, max.Z),
        };
        int[] e = { 0, 1, 1, 2, 2, 3, 3, 0, 4, 5, 5, 6, 6, 7, 7, 4, 0, 4, 1, 5, 2, 6, 3, 7 };
        var verts = new Vector3[e.Length];
        for (int i = 0; i < e.Length; i++)
        {
            verts[i] = c[e[i]];
        }

        var arrays = new global::Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Lines, arrays);

        _highlight = new MeshInstance3D
        {
            Name = "obp_inspect_highlight",
            Mesh = mesh,
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                AlbedoColor = new Color(0.2f, 1f, 0.4f),
                DisableFog = true,
                RenderPriority = 3,
            },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        if (_result.Root is { } root && GodotObject.IsInstanceValid(root))
        {
            root.AddChild(_highlight);
        }
    }

    private void ClearHighlight()
    {
        if (_highlight is { } h && GodotObject.IsInstanceValid(h))
        {
            h.QueueFree();
        }

        _highlight = null;
    }
}
