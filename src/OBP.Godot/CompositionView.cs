using Godot;
using OBP.Composition;

namespace OBP.Godot;

/// <summary>
/// Presentation-side helpers that let a Godot host place several
/// <see cref="RuntimeWorldScene"/> sub-trees under independent
/// <see cref="CompositionTransform"/> roots and apply non-destructive comparison
/// state (per-world visibility, per-asset-kind toggles, overlay opacity, debug
/// tint). Nothing here mutates a decoded <c>RuntimeWorld</c>, an
/// <see cref="ArrayMesh"/>, or a source <see cref="StandardMaterial3D"/> — the
/// tint/opacity go on the <see cref="GeometryInstance3D"/> and a detachable
/// <see cref="MeshInstance3D.MaterialOverlay"/>, both fully reversible.
/// </summary>
public static class CompositionView
{
    /// <summary>
    /// A <see cref="CompositionTransform"/> (OBP space: scale → +Y rotation →
    /// translation) expressed as the Godot <see cref="Transform3D"/> for a world
    /// transform root, in the same X-mirrored frame
    /// <see cref="RuntimeWorldScene.ToScene"/> puts the geometry in. The X flip
    /// is a reflection, so it conjugates the transform: translation X negates and
    /// the Y rotation reverses sign; uniform scale is unchanged.
    /// </summary>
    public static Transform3D ToGodotTransform(CompositionTransform t)
    {
        float yaw = Mathf.DegToRad((float)-CompositionTransform.NormalizeDegrees(t.RotationYDegrees));
        var basis = new Basis(Vector3.Up, yaw).Scaled(new Vector3((float)t.Scale, (float)t.Scale, (float)t.Scale));
        var origin = new Vector3((float)-t.TranslationX, (float)t.TranslationY, (float)t.TranslationZ);
        return new Transform3D(basis, origin);
    }

    /// <summary>Asset kinds the lab can toggle per world. Neutral <c>RuntimeMesh.AssetKind</c> values plus the two synthetic groups.</summary>
    public static readonly string[] ToggleableKinds =
        { "tfrag", "tie", "shrub", "moby", "sky", "moby-marker", "collision" };

    /// <summary>
    /// Apply a <see cref="WorldPlacement"/>'s display state to an already-built
    /// scene. Idempotent — safe to call every time the placement changes.
    /// </summary>
    public static void ApplyDisplayState(RuntimeWorldScene.Result result, WorldPlacement placement)
    {
        var root = result.Root;
        if (root is null || !GodotObject.IsInstanceValid(root))
        {
            return;
        }

        bool worldVisible = placement.Visible;
        root.Visible = worldVisible;

        float transparency = Mathf.Clamp(1f - (float)placement.Opacity, 0f, 1f);
        Color? tint = placement.DebugTint is { Length: >= 3 } c
            ? new Color((float)c[0], (float)c[1], (float)c[2], 0.28f)
            : null;

        WalkMeshInstances(root, mi =>
        {
            string kind = KindOf(mi);
            mi.Visible = placement.CategoryVisible(kind);
            mi.Transparency = transparency;
            ApplyTint(mi, tint);
        });

        // Collision bodies: a StaticBody3D holds a CollisionShape3D (+ optional
        // debug mesh). Toggle the whole body so physics follows visibility.
        bool collisionVisible = placement.CategoryVisible("collision");
        foreach (var child in root.GetChildren())
        {
            if (child is StaticBody3D body)
            {
                body.Visible = collisionVisible;
                foreach (var bc in body.GetChildren())
                {
                    if (bc is CollisionShape3D cs)
                    {
                        cs.Disabled = !collisionVisible;
                    }
                }
            }
        }

        if (result.SkyRoot is { } sky && GodotObject.IsInstanceValid(sky))
        {
            sky.Visible = worldVisible && placement.CategoryVisible("sky");
        }
    }

    private static void ApplyTint(MeshInstance3D mi, Color? tint)
    {
        const string overlayName = "obp_composition_tint";
        if (tint is null)
        {
            if (mi.MaterialOverlay is StandardMaterial3D existing && existing.ResourceName == overlayName)
            {
                mi.MaterialOverlay = null;
            }

            return;
        }

        if (mi.MaterialOverlay is StandardMaterial3D m && m.ResourceName == overlayName)
        {
            m.AlbedoColor = tint.Value;
            return;
        }

        mi.MaterialOverlay = new StandardMaterial3D
        {
            ResourceName = overlayName,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoColor = tint.Value,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
    }

    private static void WalkMeshInstances(Node node, System.Action<MeshInstance3D> visit)
    {
        foreach (var child in node.GetChildren())
        {
            if (child is MeshInstance3D mi)
            {
                visit(mi);
            }

            if (child.GetChildCount() > 0)
            {
                WalkMeshInstances(child, visit);
            }
        }
    }

    /// <summary>The <see cref="RuntimeWorldScene"/> names every mesh instance <c>"{kind}_{textureId}"</c>; collision-debug meshes start "CollisionDebug".</summary>
    private static string KindOf(MeshInstance3D mi)
    {
        string n = mi.Name;
        if (n.StartsWith("CollisionDebug", System.StringComparison.Ordinal))
        {
            return "collision";
        }

        int underscore = n.LastIndexOf('_');
        return underscore > 0 ? n[..underscore] : n;
    }
}
