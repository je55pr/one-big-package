using Godot;
using OBP.Godot.Player;
using OBP.RAC1.Player;
using OBP.Runtime.Player;
using OBP.Runtime.Presentation;

namespace OneBigPackage;

/// <summary>
/// Godot presentation of the recovered retail class-71 Wrench mesh.
/// The root-relative transform is an explicit host fallback calibrated from a
/// neutral retail witness; it is not claimed as the unresolved native hand socket.
/// </summary>
public sealed partial class Rac1WrenchView : Node3D
{
    public const string AttachmentProvenance = "host-neutral-root-relative-fallback";

    private static readonly Basis HostNeutralRelativeBasis = new(
        new Vector3(0.681012121f, 0.474859610f, 0.557432366f),
        new Vector3(-0.367457419f, 0.880061561f, -0.300776818f),
        new Vector3(-0.633401560f, 0f, 0.773823277f));

    private static readonly PlayerAvatarPoint HostNeutralRelativeNativePosition =
        new(-0.095941262614849, -0.298701485983791, 0.495155334472656);

    public Rac1WrenchView(Rac1WrenchAsset.Asset asset, PlayerAvatar avatar)
    {
        Name = "Rac1RetailWrench_HostAttachmentFallback";
        Basis = HostNeutralRelativeBasis;
        Position = PlayerAvatarView.ToGodotLocal(
            HostNeutralRelativeNativePosition,
            avatar.Axes,
            avatar.BaseHeight,
            alignGeometricBase: true);
        BuildSurfaces(asset, avatar.Axes);
    }

    public static Vector3 HostFallbackPosition(PlayerAvatar avatar) =>
        PlayerAvatarView.ToGodotLocal(
            HostNeutralRelativeNativePosition,
            avatar.Axes,
            avatar.BaseHeight,
            alignGeometricBase: true);

    private void BuildSurfaces(Rac1WrenchAsset.Asset asset, PlayerAvatarAxes axes)
    {
        Vector3[] vertices = PlayerAvatarView.ConvertFrame(asset.Mesh.Positions, axes);
        var textures = asset.Textures.ToDictionary(texture => texture.TextureId);
        foreach (var surface in asset.Surfaces)
        {
            if (!textures.TryGetValue(surface.TextureId, out var texture))
                throw new InvalidDataException(
                    $"R&C1 Wrench surface texture {surface.TextureId} is missing.");

            var image = Image.CreateFromData(
                texture.Width, texture.Height, false, Image.Format.Rgba8, texture.Rgba);
            var imageTexture = ImageTexture.CreateFromImage(image);
            var material = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
                AlbedoColor = Colors.White,
                AlbedoTexture = imageTexture,
            };

            if (AlphaProfile.Analyse(texture.Rgba).AnyBelowHalf)
            {
                material.Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor;
                material.AlphaScissorThreshold = 0.5f;
            }

            var arrays = new global::Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = vertices;
            arrays[(int)Mesh.ArrayType.TexUV] = ToUvs(surface.Uvs);
            arrays[(int)Mesh.ArrayType.Index] = surface.Indices;

            var mesh = new ArrayMesh();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            AddChild(new MeshInstance3D
            {
                Name = $"RetailClass71_Surface_{surface.TextureId}",
                Mesh = mesh,
                MaterialOverride = material,
            });
        }
    }

    private static Vector2[] ToUvs(float[] uv)
    {
        var result = new Vector2[uv.Length / 2];
        for (int i = 0, vertex = 0; i < uv.Length; i += 2, vertex++)
            result[vertex] = new Vector2(uv[i], uv[i + 1]);
        return result;
    }
}
