using System.Numerics;
using OBP.RAC3.Level;
using OBP.Runtime;
using OBP.Runtime.Presentation;

namespace OBP.RAC3.Gameplay;

/// <summary>
/// Projection of 0x003873F8 / 0x00409A50. Native coordinates are Z-up;
/// emitted RuntimeObjectMesh coordinates are OBP Y-up. Trigonometry and
/// matrix arithmetic are float host projections, not bit-exact EE/VU math.
/// </summary>
public static class Rac3ShockCannonBeam
{
    public readonly record struct Vertex(Vector3 Position, uint Rgba, Vector2 Uv);
    /// <summary>
    /// Source aim and the two cross-section axes after the native Euler selector.
    /// That selector's local +Z faces against the aim (the pole is at muzzle -0.5).
    /// For aim +X, the original produces CrossX +Z and CrossY +Y.
    /// </summary>
    public sealed record Basis(Vector3 CrossX, Vector3 CrossY, Vector3 Forward, Vector3 Muzzle)
    {
        /// <summary>
        /// Geometric projection of the aim-to-Euler/matrix path in 0x0038748C..
        /// 0x003874DC. Aim selection (camera, player or held-Moby axis) stays an
        /// explicit caller fact; this helper does not choose a target.
        /// </summary>
        public static Basis FromNativeAim(Vector3 aim, Vector3 muzzle)
        {
            if (!float.IsFinite(aim.LengthSquared()) || aim.LengthSquared() < 1e-10f)
                throw new ArgumentOutOfRangeException(nameof(aim));
            Vector3 forward = Vector3.Normalize(aim);
            float horizontal = MathF.Sqrt(forward.X * forward.X + forward.Y * forward.Y);
            Vector3 crossY = horizontal > 0 ? new(-forward.Y / horizontal, forward.X / horizontal, 0) : Vector3.UnitY;
            return new(Vector3.Cross(forward, crossY), crossY, forward, muzzle);
        }
    }
    public readonly record struct Scroll(float First, float Second, float Third)
    {
        public Scroll Advance() => new(Wrap(First + .15f), Wrap(Second + .1f), Wrap(Third + .05f));
        private static float Wrap(float value) => value >= 7 ? value - 7 : value;
    }
    public sealed record Layer(int FxTextureId, int RingCount, uint NativeRgba, RuntimeObjectMesh Mesh);
    private static readonly float[] RingX = [1, .809017f, .309017f, -.309017f, -.809017f, -1, -.809017f, -.309017f, .309017f, .809017f, 1];
    private static readonly float[] RingY = [0, .58778524f, .95105654f, .95105654f, .58778524f, 0, -.58778524f, -.95105654f, -.95105654f, -.58778524f, 0];
    private static readonly RuntimeMaterialPresentation Material = new(
        RuntimeTextureWrap.Repeat, RuntimeTextureWrap.Repeat, RuntimeTextureMinFilter.Linear,
        AlphaBlendEnabled: true, BlendEquation: RuntimeBlendEquation.AdditiveSourceAlpha);

    /// <summary>
    /// Four native layers, including the six-ring outer layer and independently
    /// scrolled inner textures. The caller advances scroll once per draw, as the
    /// original callback does, separately from the 60-Hz gameplay counter.
    /// </summary>
    public static IReadOnlyList<Layer> Build(Rac3ShockCannonFrame frame, Basis basis, Scroll scroll)
    {
        if (frame.Weapon != Rac3ShockCannonProfile.Identity) throw new ArgumentException("Foreign beam source.", nameof(frame));
        if (!frame.Effects.Any(e => e.Kind == Rac3ShockCannonEffectKind.Beam)) return [];
        int remaining = frame.BeamUpdatesRemaining;
        if (remaining is < 1 or > 29) throw new ArgumentException("Beam frame has an invalid post-decrement counter.", nameof(frame));
        foreach (Vector3 vector in new[] { basis.CrossX, basis.CrossY, basis.Forward, basis.Muzzle })
            if (!float.IsFinite(vector.X) || !float.IsFinite(vector.Y) || !float.IsFinite(vector.Z))
                throw new ArgumentException("Beam basis must be finite.", nameof(basis));
        if (MathF.Abs(basis.CrossX.LengthSquared() - 1) > .001f || MathF.Abs(basis.CrossY.LengthSquared() - 1) > .001f ||
            MathF.Abs(basis.Forward.LengthSquared() - 1) > .001f || MathF.Abs(Vector3.Dot(basis.CrossX, basis.CrossY)) > .001f ||
            MathF.Abs(Vector3.Dot(basis.CrossX, basis.Forward)) > .001f || MathF.Abs(Vector3.Dot(basis.CrossY, basis.Forward)) > .001f)
            throw new ArgumentException("Beam basis must be orthonormal.", nameof(basis));
        if (Vector3.Dot(Vector3.Cross(basis.CrossX, basis.CrossY), -basis.Forward) < .999f)
            throw new ArgumentException("Beam basis has the wrong native handedness.", nameof(basis));
        ValidateScroll(scroll.First); ValidateScroll(scroll.Second); ValidateScroll(scroll.Third);
        float widthX = 1, widthY = 1;
        for (int counter = 29; counter >= remaining; counter--)
        {
            widthX += counter >= 16 ? .1f : -.075f;
            widthY += counter >= 16 ? .1f : -.125f;
        }
        Vector3 origin = basis.Muzzle + basis.Forward * 14.5f;
        Vector3 x = basis.CrossX * widthX, y = basis.CrossY * widthY;
        // Rotate both scaled cross-section vectors around the axial basis.
        x = Rotate(x, -basis.Forward, MathF.PI / 4); y = Rotate(y, -basis.Forward, MathF.PI / 4);
        var output = new List<Layer>();
        Add(23, 8, 0x00804020u | (uint)remaining << 25, scroll.First, x, y);
        x = Rotate(x, -basis.Forward, -MathF.PI / 2); y = Rotate(y, -basis.Forward, -MathF.PI / 2);
        Add(23, 8, 0x00801040u | (uint)remaining << 25, scroll.Second, x, y);
        Add(23, 8, 0x00804010u | (uint)remaining << 25, scroll.Third, x, y);
        Add(10, 6, 0x00d20a40u | (uint)remaining << 24, scroll.First, x * 1.1f, y * 1.1f);
        return output.AsReadOnly();

        void Add(int texture, int rings, uint color, float phase, Vector3 axisX, Vector3 axisY)
        {
            Vector3 axisZ = basis.Forward * -15;
            var matrix = new Matrix4x4(axisX.X, axisX.Y, axisX.Z, 0, axisY.X, axisY.Y, axisY.Z, 0,
                axisZ.X, axisZ.Y, axisZ.Z, 0, origin.X, origin.Y, origin.Z, 1);
            Vertex[][] strips = EllipsoidStrips(matrix, rings, color, phase);
            var positions = new List<double>(); var uvs = new List<float>(); var colors = new List<float>(); var indices = new List<int>();
            foreach (Vertex[] strip in strips)
            {
                int start = positions.Count / 3;
                foreach (Vertex vertex in strip)
                {
                    positions.AddRange([vertex.Position.X, vertex.Position.Z, vertex.Position.Y]);
                    uvs.AddRange([vertex.Uv.X, vertex.Uv.Y]);
                    for (int channel = 0; channel < 4; channel++) colors.Add((byte)(vertex.Rgba >> (channel * 8)) / 128f);
                }
                for (int i = 2; i < strip.Length; i++)
                    indices.AddRange(i % 2 == 0 ? [start + i - 2, start + i - 1, start + i] : [start + i - 1, start + i - 2, start + i]);
            }
            output.Add(new(texture, rings, color, new(Rac3FxTextures.AssetKind, texture,
                positions.ToArray(), uvs.ToArray(), indices.ToArray(), colors.ToArray(), Material)));
        }
    }

    /// <summary>
    /// Native primitive's alternating 22-vertex strips. Positive ring count
    /// fades to black (additive) or transparent; negative disables that fade.
    /// Each polar step is pi/10 regardless of the requested number of rings.
    /// </summary>
    public static Vertex[][] EllipsoidStrips(Matrix4x4 transform, int ringCount, uint rgba, float phase, bool additive = true)
    {
        if (ringCount is 0 or < -10 or > 10) throw new ArgumentOutOfRangeException(nameof(ringCount));
        ValidateScroll(phase);
        int count = Math.Abs(ringCount);
        var vertices = new Vertex[22];
        float v = 7 + (phase - MathF.Truncate(phase));
        Vector3 pole = Vector3.Transform(Vector3.UnitZ, transform);
        for (int i = 0; i < 22; i++) vertices[i] = new(pole, rgba, new((i / 2) & 1, v));
        var output = new Vertex[count][];
        float angle = 0, fraction = 0, fractionStep = ringCount < 0 ? 0 : 1f / ringCount;
        uint target = rgba & (additive ? 0xff000000u : 0x00ffffffu);
        for (int ring = 0; ring < count; ring++)
        {
            angle += .31415927f; fraction = RoundColor((double)fraction + fractionStep); v -= 1;
            uint color = Lerp(rgba, target, fraction);
            float radius = MathF.Sin(angle), z = MathF.Cos(angle);
            for (int point = 0; point < 11; point++)
            {
                int index = point * 2 + (ring % 2 == 0 ? 1 : 0);
                vertices[index] = new(Vector3.Transform(new(radius * RingX[point], radius * RingY[point], z), transform),
                    color, new(point & 1, v));
            }
            output[ring] = vertices.ToArray();
        }
        return output;
    }

    private static Vector3 Rotate(Vector3 vector, Vector3 axis, float angle) =>
        vector * MathF.Cos(angle) + Vector3.Cross(axis, vector) * MathF.Sin(angle) + axis * Vector3.Dot(axis, vector) * (1 - MathF.Cos(angle));

    private static uint Lerp(uint from, uint to, float amount)
    {
        uint color = 0;
        for (int c = 0; c < 4; c++)
        {
            int a = (byte)(from >> (c * 8)), b = (byte)(to >> (c * 8));
            // 0x0040D020 computes a*(1-t) + b*t in separate VU MAC
            // operations, then VFTOI0. The algebraically equivalent a+(b-a)*t
            // changes low bytes, including alpha, on the six-ring layer.
            float inverse = RoundColor(1d - amount);
            float first = RoundColor(a * (double)inverse), second = RoundColor(b * (double)amount);
            color |= (uint)Math.Clamp((int)RoundColor((double)first + second), 0, 255) << (c * 8);
        }
        return color;
    }

    private static float RoundColor(double value)
    {
        float result = (float)value;
        return Math.Abs(result) > Math.Abs(value)
            ? result > 0 ? MathF.BitDecrement(result) : MathF.BitIncrement(result)
            : result;
    }

    private static void ValidateScroll(float value)
    {
        if (!float.IsFinite(value) || value < 0 || value >= 7) throw new ArgumentOutOfRangeException(nameof(value));
    }
}
