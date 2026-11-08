using System.Numerics;
using OBP.RAC1.Gameplay;
using OBP.Runtime.Player;

namespace OBP.RAC1.Player;

/// <summary>Thruster jet admission/ramp (2b4198), authored plume (2b3628), Hydro emissions (218bd4).</summary>
public sealed class Rac1ClankPackEffects(Rac1ClankPackAssets assets, Rac1PickupRandom random)
{
    public readonly record struct Input(int Action, int Age, float AuthoredTime, bool Blending,
        bool Landed, bool HasApexed, float Scalar, Vector3 ContactMotion, float Yaw, float Pitch,
        float FloorHeight, float ControlHeading);
    private int _jetState = 1, _jetAge, _hydroDelay;
    private float _jetStrength, _scroll;
    private bool _emitJets, _jetHistory;
    private readonly Vector3[] _previousJets = new Vector3[2];
    private readonly List<Jet> _jets = [];
    public float JetStrength => _jetStrength;
    public Rac1ParticleSession Particles { get; } = new(assets.Effects, random, 512);
    public sealed record Jet(double[] Positions, float[] Uvs, int[] Indices, int Texture,
        float BillboardSize = 0, uint Color = 0x80808080)
    {
        public int Texture { get; set; } = Texture;
    }

    private void EnsureJetBuffers()
    {
        if (_jets.Count != 0) return;
        // Native draw issues two overlapping triangle strips. Topology and UV
        // storage are fixed; only positions, UV scroll and flicker change.
        var indices = new List<int>();
        foreach (var (first, count) in new[] { (0, 148), (146, 50) })
            for (int i = 0; i < count - 2; i++)
            {
                indices.Add(first + i + (i % 2));
                indices.Add(first + i + (1 - i % 2));
                indices.Add(first + i + 2);
            }
        int[] plumeIndices = indices.ToArray();
        float[] quadUvs = [0, 0, 0, 1, 1, 0, 1, 1];
        int[] quadIndices = [0, 1, 2, 2, 1, 3];
        for (int side = 0; side < 2; side++)
        {
            _jets.Add(new(new double[assets.JetVertices.Count * 3], new float[assets.JetUvs.Count * 2],
                plumeIndices, assets.Effects.ParticleChoices[10][0]));
            for (int plane = 0; plane < 2; plane++)
                _jets.Add(new(new double[12], quadUvs, quadIndices, assets.Effects.ParticleChoices[12][0]));
            _jets.Add(new(new double[3], [], [], assets.Effects.ParticleChoices[11][0], .4f, 0x7f2020ff));
        }
    }

    public IReadOnlyList<Jet> StepJets(Rac1ClankPackSession pack, Input input, PlayerAvatarAttachmentPose worldBack)
    {
        bool on = pack is { Pack: 3, Ready: true } && (input.Action switch
        {
            >= 0xb and <= 0xe => !input.HasApexed,
            0x1c => !input.Blending && input.AuthoredTime is >= 6 and <= 16 && !input.HasApexed,
            0x10 => input.Age < 44,
            0x22 => input.Age < 15 || input.Age > 33 && input.Landed,
            8 or 0x81 => true,
            _ => false,
        });
        _emitJets = _jetState == 3 && on;
        switch (_jetState)
        {
            case 1 when on: _jetState = 2; _jetAge = 2; _jetStrength = 0; break;
            case 2:
                _jetStrength += .5f;
                if (--_jetAge == 0) { _jetState = 3; _jetStrength = 1; _jetHistory = false; }
                break;
            case 3 when !on: _jetState = 4; _jetAge = 8; break;
            case 4:
                if (--_jetAge == 0) { _jetState = 1; _jetStrength = 0; }
                else _jetStrength -= .125f;
                break;
            case 1 or 3: break; // Remain inactive/steady when the guarded transition is absent.
            default: throw new InvalidOperationException("Invalid RAC1 Thruster jet state.");
        }
        if (pack.Pack != 3 || pack.Upgrade is null || _jetStrength <= 0) return [];
        _scroll -= .025f;
        if (_scroll < -7) _scroll += 7;
        EnsureJetBuffers();
        for (int side = 0; side < 2; side++)
        {
            var origin = V(worldBack.Transform(pack.Upgrade.Attachment(side * 2).Origin));
            var end = V(worldBack.Transform(pack.Upgrade.Attachment(side * 2 + 1).Origin));
            var z = Vector3.Normalize(origin - end);
            var x = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, z));
            var y = Vector3.Cross(z, x);
            var positions = _jets[side * 4].Positions;
            var uvs = _jets[side * 4].Uvs;
            for (int i = 0; i < assets.JetVertices.Count; i++)
            {
                var v = assets.JetVertices[i] * (.0125f * _jetStrength);
                var p = origin + x * v.X + y * v.Y + z * v.Z;
                positions[i * 3] = p.X; positions[i * 3 + 1] = p.Y; positions[i * 3 + 2] = p.Z;
                uvs[i * 2] = assets.JetUvs[i].X; uvs[i * 2 + 1] = assets.JetUvs[i].Y + _scroll;
            }
            int texture = assets.Effects.ParticleChoices[12][random.Below(8)];
            for (int plane = 0; plane < 2; plane++)
            {
                var layer = _jets[side * 4 + plane + 1];
                layer.Texture = texture;
                var quad = layer.Positions;
                for (int i = 0; i < 4; i++)
                {
                    var v = assets.JetQuads[plane * 4 + i] * (.025f * _jetStrength);
                    if (i is 1 or 3) v.Z -= random.Range(0, .1f);
                    var p = origin + x * v.X + y * v.Y + z * (v.Z - .075f);
                    quad[i * 3] = p.X; quad[i * 3 + 1] = p.Y; quad[i * 3 + 2] = p.Z;
                }
            }
            var glow = origin - z * .13f;
            var glowPosition = _jets[side * 4 + 3].Positions;
            glowPosition[0] = glow.X; glowPosition[1] = glow.Y; glowPosition[2] = glow.Z;
        }
        return _jets;
    }

    /// <summary>The host supplies world-space nozzle points and the hero's native X basis.</summary>
    public void StepParticles(Rac1ClankPackSession pack, Input input, PlayerAvatarAttachmentPose worldBack, Vector3 heroX,
        Vector3 camera)
    {
        Particles.Step(camera, enforceLevelBounds: false, controlHeading: input.ControlHeading);
        if (_emitJets && pack is { Pack: 3, Upgrade: { } thruster })
        {
            for (int side = 0; side < 2; side++)
            {
                var origin = V(worldBack.Transform(thruster.Attachment(side * 2).Origin));
                var end = V(worldBack.Transform(thruster.Attachment(side * 2 + 1).Origin));
                var z = Vector3.Normalize(origin - end);
                var x = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, z)); var y = Vector3.Cross(z, x);
                var velocity = x * (MathF.Cos(random.Angle()) * .05f) + y * (MathF.Sin(random.Angle()) * .05f) - z * random.Range(.01f, .03f);
                Particles.HitFlash(origin, velocity, 0x4f007fff, 0x1fffffff, 10, 10000, true);
                if (_jetHistory) Particles.HitFlash((origin + _previousJets[side]) * .5f, velocity, 0x4f007fff, 0x1fffffff, 10, 10000, true);
                _previousJets[side] = origin;
            }
            _jetHistory = true;
        }
        if (pack is not { Pack: 4, Ready: true } || input.Action != 0x35) return;
        var first = V(worldBack.Transform(pack.Upgrade!.Attachment(0).Origin));
        var second = V(worldBack.Transform(pack.Upgrade.Attachment(1).Origin));
        if (_hydroDelay > 0 && --_hydroDelay > 0) return;
        for (int i = 0; i < 4; i++)
        {
            var point = (i % 2 == 0 ? first : second) - heroX * .15f +
                new Vector3(random.Range(0, .03f), random.Range(0, .03f), random.Range(0, .03f));
            var backward = new Vector3(MathF.Cos(input.Yaw) * MathF.Cos(input.Pitch),
                MathF.Sin(input.Yaw) * MathF.Cos(input.Pitch), MathF.Sin(input.Pitch)) * (-.75f / 60);
            _hydroDelay = (int)(((7f / 60 - input.Scalar) / (7f / 60)) * 5);
            Particles.Dust(point, input.ContactMotion + backward, random.Range(6300, 9450), input.FloorHeight);
        }
    }

    private static Vector3 V(PlayerAvatarPoint p) => new((float)p.X, (float)p.Y, (float)p.Z);
}
