using Godot;
using OBP.Godot;
using OBP.RAC1.Gameplay;
using OBP.Runtime.Gameplay;
using NVector3 = System.Numerics.Vector3;

namespace OBP.Game;

public partial class OBPGame
{
    private sealed record ArsenalChicken(Rac1HostedEnemy Enemy, Rac1EquipmentView View, Shape3D OriginalShape, Vector3 OriginalShapePosition)
    {
        public int Age;
        public Vector3 Direction;
    }

    private readonly Dictionary<int, ArsenalChicken> _rac1Chickens = [];
    private readonly Dictionary<int, int> _rac1MorphExposure = [];
    private readonly List<MeshInstance3D> _rac1ArsenalBeams = [];
    private readonly Rac1TeslaPresentation _rac1TeslaPresentation = new();
    private readonly Dictionary<int, ShaderMaterial> _rac1TeslaMaterials = [];
    private Rac1PickupRandom? _rac1TeslaEffectRandom;
    private StandardMaterial3D? _rac1PlainBeamMaterial;
    private bool _rac1ArsenalHeld;
    private readonly HashSet<int> _rac1TeslaSoundTargets = [];

    private void ResetRac1Arsenal()
    {
        Rac1World.Weapons.Missiles.Reset();
        Rac1World.Weapons.Deployables.Reset();
        foreach (var node in _rac1Chickens.Values.Select(item => (Node)item.View).Concat(_rac1ArsenalBeams))
            if (IsInstanceValid(node)) node.QueueFree();
        foreach (var chicken in _rac1Chickens.Values.Where(chicken => IsInstanceValid(chicken.Enemy.Node.Root) && IsInstanceValid(chicken.Enemy.Body)))
        {
            // Restore only surfaces that had supported materials before morphing.
            // Materialless Moby children must remain hidden after the chicken resets.
            foreach (var mesh in chicken.Enemy.Node.Root.GetChildren().OfType<MeshInstance3D>())
                mesh.Visible = mesh.MaterialOverride is not null;
            var collider = chicken.Enemy.Body.GetChild<CollisionShape3D>(0);
            collider.Shape = chicken.OriginalShape; collider.Position = chicken.OriginalShapePosition;
        }
        _rac1Chickens.Clear();
        _rac1MorphExposure.Clear(); _rac1ArsenalBeams.Clear();
        Rac1World.Weapons.Taunter.Reset();
        _rac1TeslaPresentation.Reset(); _rac1TeslaMaterials.Clear(); _rac1TeslaEffectRandom = null;
        Rac1World.Weapons.Walloper.Reset();
        _rac1ArsenalHeld = false;
        _rac1TeslaSoundTargets.Clear();
        if (_player is not null) { _player.Rac1EquipmentActionActive = false; _player.Rac1EquipmentLungeVelocity = Vector3.Zero; }
    }

    private bool AdmitRac1ArsenalUse() => _rac1Weapons.Equipped switch
    {
        Rac1WeaponId.VisibombGun => !Rac1World.Weapons.Missiles.GuidedActive,
        Rac1WeaponId.DroneDevice => Rac1World.Weapons.Deployables.CanFireDrone,
        Rac1WeaponId.Ryno => Rac1World.Weapons.Missiles.CanBeginRyno,
        Rac1WeaponId.DecoyGlove => Rac1World.Weapons.Deployables.CanFireDecoy,
        _ => true,
    };

    private RuntimeWorldScene.DynamicObjectNode? FindArsenalTarget(Vector3 origin, float range, float cone = -.1f,
        IEnumerable<int>? excluded = null)
    {
        var skip = excluded?.ToHashSet();
        return Rac1World.Enemies.Targets.Where(node => Rac1World.Weapons.Contacts.IsAimTarget(node) && !_rac1Chickens.ContainsKey(node.Source.InstanceIndex) &&
                skip?.Contains(node.Source.InstanceIndex) != true)
            .Where(node =>
            {
                var delta = node.Root.GlobalPosition + Vector3.Up * .5f - origin;
                return delta.LengthSquared() < range * range && delta.LengthSquared() > .00001f &&
                    delta.Normalized().Dot(EquipmentForward) > cone && Rac1World.Weapons.Contacts.HasLineOfSight(origin, node.Root.GlobalPosition + Vector3.Up * .5f);
            }).OrderBy(node => node.Root.GlobalPosition.DistanceSquaredTo(origin)).FirstOrDefault();
    }

    private void FireRac1Arsenal(Rac1AdditionalWeaponSession.Shot shot)
    {
        switch (shot.Weapon)
        {
            case Rac1WeaponId.Devastator: Rac1World.Weapons.Missiles.Fire(shot.Weapon); break;
            case Rac1WeaponId.VisibombGun: Rac1World.Weapons.Missiles.Fire(shot.Weapon); break;
            case Rac1WeaponId.Ryno: Rac1World.Weapons.Missiles.Fire(shot.Weapon); break;
            case Rac1WeaponId.DroneDevice: Rac1World.Weapons.Deployables.FireDrones(); break;
            case Rac1WeaponId.DecoyGlove: Rac1World.Weapons.Deployables.FireDecoy(); break;
            case Rac1WeaponId.Walloper: Rac1World.Weapons.Walloper.Begin(); break;
        }
    }

    private void StepRac1ArsenalWorld()
    {
        if (_player is null) return;
        Rac1World.Weapons.Missiles.Tick();
        StepRac1ArsenalDeployables(); StepRac1ArsenalBeams(); Rac1World.Weapons.Walloper.Tick();
    }

    private void ApplyArsenalBlast(Rac1WeaponId weapon, Vector3 position, float radius, double damage, uint flags)
    {
        foreach (var (node, bounds) in Rac1World.Weapons.Contacts.Geometry.ToArray())
        {
            var closest = position.Clamp(bounds.Position, bounds.End);
            if (!Rac1World.Enemies.IsRac1GameplayActive(node) || position.DistanceTo(closest) > radius ||
                !Rac1World.Weapons.Contacts.HasLineOfSight(position + Vector3.Up * .1f, bounds.GetCenter())) continue;
            ApplyRac1AdditionalDamage(node, Rac1WeaponProfiles.DirectDamage(weapon,
                new(node.Source.NativeClassId, node.Source.InstanceIndex), damage, flags));
        }
    }

    private void StepRac1ArsenalDeployables()
    {
        Rac1World.Weapons.Deployables.Tick();
        foreach (var pair in _rac1Chickens.ToArray())
        {
            var chicken = pair.Value;
            if (!Rac1World.Enemies.IsRac1GameplayActive(chicken.Enemy.Node) || chicken.Enemy.RewardEmitted)
            { chicken.View.QueueFree(); _rac1Chickens.Remove(pair.Key); continue; }
            if (chicken.Enemy.NeedsGroundPlacement)
            {
                if (!Rac1World.Enemies.TryGroundRac1Enemy(chicken.Enemy, upwardReach: 1, downwardReach: 1)) continue;
                chicken.Enemy.NeedsGroundPlacement = false;
            }
            chicken.Age++; var body = chicken.Enemy.Body;
            if (chicken.Age % 90 == 1) chicken.Direction = EquipmentForward.Rotated(Vector3.Up, _persistentPlayer.Rac1EffectRandom(_args.FixedSeed).Signed(0, MathF.PI));
            bool supported = EquipmentGround(body.GlobalPosition + chicken.Direction * .5f, .4f, .8f) is not null;
            body.Velocity = (supported ? chicken.Direction : Vector3.Zero) * .8f + Vector3.Down * 2;
            body.MoveAndSlide(); chicken.Enemy.Node.Root.GlobalPosition = body.GlobalPosition;
            chicken.View.Play(1); Rac1ArsenalViewPose.Orient(chicken.View, chicken.Direction);
            chicken.View.Rotation -= new Vector3(0, chicken.Enemy.Node.Root.Rotation.Y, 0); chicken.View.Step();
        }
    }

    private void StepRac1ArsenalBeams()
    {
        foreach (var beam in _rac1ArsenalBeams) beam.Visible = false;
        foreach (int key in _rac1MorphExposure.Keys.ToArray())
            if (--_rac1MorphExposure[key] <= 0) _rac1MorphExposure.Remove(key);
        if (!_rac1ArsenalHeld) { _rac1TeslaSoundTargets.Clear(); return; }
        var weapon = _rac1Weapons.Equipped;
        if (weapon != Rac1WeaponId.TeslaClaw) _rac1TeslaSoundTargets.Clear();
        if (weapon is not (Rac1WeaponId.TeslaClaw or Rac1WeaponId.MorphORay or Rac1WeaponId.Taunter)) return;
        if (weapon == Rac1WeaponId.TeslaClaw && _rac1Weapons.GetAmmo(19) <= 0) return;
        if (weapon == Rac1WeaponId.TeslaClaw) _rac1TeslaPresentation.Advance();
        var from = EquipmentMuzzle(weapon);
        if (weapon is Rac1WeaponId.TeslaClaw or Rac1WeaponId.MorphORay && _rac1WeaponWorldTick % 3 == 0)
            SpawnEquipmentFlash(from, 27, weapon == Rac1WeaponId.TeslaClaw ? new(.3f, .6f, 1, .85f) : new(.4f, 1, .25f, .75f), .28f, 5);
        if (weapon == Rac1WeaponId.Taunter)
        {
            Rac1World.Weapons.Taunter.Tick(from, EquipmentForward, _player!.GlobalPosition);
            return;
        }
        var first = FindArsenalTarget(from, weapon == Rac1WeaponId.TeslaClaw ? Rac1ArsenalRules.TeslaRange : Rac1ArsenalHostPolicy.MorphRange, .5f);
        if (first is null)
        {
            _rac1TeslaSoundTargets.Clear();
            var end = from + EquipmentForward * (weapon == Rac1WeaponId.MorphORay ? 3 : .55f);
            var hit = Rac1World.Weapons.Contacts.Contact(from, end, .01f);
            DrawArsenalBeam(0, from, hit?.Point ?? end, weapon == Rac1WeaponId.TeslaClaw ? new(.3f, .6f, 1) : new(.4f, 1, .25f));
            return;
        }
        var targets = new List<RuntimeWorldScene.DynamicObjectNode> { first };
        if (weapon == Rac1WeaponId.TeslaClaw)
        {
            var second = FindArsenalTarget(from, Rac1ArsenalRules.TeslaRange, .5f, [first.Source.InstanceIndex]);
            if (second is not null) targets.Add(second);
            _rac1TeslaSoundTargets.RemoveWhere(id => targets.All(target => target.Source.InstanceIndex != id));
            foreach (var target in targets)
                if (_rac1TeslaSoundTargets.Add(target.Source.InstanceIndex)) FusionWeapons.PlayNativeSound(177, 1, from); // 0x2b9870 lock acquisition.
        }
        for (int i = 0; i < targets.Count; i++)
        {
            var target = targets[i]; var to = target.Root.GlobalPosition + Vector3.Up * .5f;
            DrawArsenalBeam(i, from, to, weapon == Rac1WeaponId.TeslaClaw ? new(.3f, .6f, 1) : new(.4f, 1, .25f));
            if (weapon == Rac1WeaponId.TeslaClaw)
            {
                if (_rac1WeaponWorldTick % 10 == 0) ApplyRac1AdditionalDamage(target,
                    Rac1WeaponProfiles.DirectDamage(weapon, new(target.Source.NativeClassId, target.Source.InstanceIndex), Rac1ArsenalRules.TeslaDamage, Rac1ArsenalRules.TeslaDamageFlags));
            }
            else
            {
                int progress = _rac1MorphExposure.GetValueOrDefault(target.Source.InstanceIndex) + 2;
                _rac1MorphExposure[target.Source.InstanceIndex] = progress;
                if (progress >= Rac1ArsenalHostPolicy.MorphExposureTicks) MorphArsenalEnemy(target);
            }
        }
    }

    private void DrawArsenalBeam(int index, Vector3 from, Vector3 to, Color color)
    {
        if (_rac1Weapons.Equipped == Rac1WeaponId.TeslaClaw && _rac1EquipmentAssets?.Effects is { } effects)
        {
            DrawRac1TeslaRibbons(index, from, to, effects);
            return;
        }
        while (_rac1ArsenalBeams.Count <= index)
        {
            var beam = new MeshInstance3D
            {
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                MaterialOverride = new StandardMaterial3D
                {
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    VertexColorUseAsAlbedo = true,
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    CullMode = BaseMaterial3D.CullModeEnum.Disabled
                }
            };
            _sceneResult!.Root.AddChild(beam); _rac1ArsenalBeams.Add(beam);
        }
        _rac1ArsenalBeams[index].MaterialOverride = _rac1PlainBeamMaterial ??= new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            VertexColorUseAsAlbedo = true,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled
        };
        var mesh = _rac1ArsenalBeams[index].Mesh as ImmediateMesh ?? new ImmediateMesh();
        mesh.ClearSurfaces(); var side = (to - from).Cross(Vector3.Up).Normalized() * .045f;
        mesh.SurfaceBegin(Mesh.PrimitiveType.Triangles);
        var points = new List<Vector3> { from };
        for (int part = 1; part < 10; part++) points.Add(from.Lerp(to, part / 10f) + new Vector3(
            _persistentPlayer.Rac1EffectRandom(_args.FixedSeed).Signed(0, .13f), _persistentPlayer.Rac1EffectRandom(_args.FixedSeed).Signed(0, .13f), _persistentPlayer.Rac1EffectRandom(_args.FixedSeed).Signed(0, .13f)));
        points.Add(to);
        for (int part = 0; part + 1 < points.Count; part++)
            foreach (var p in new[] { points[part] - side, points[part] + side, points[part + 1] + side,
                points[part] - side, points[part + 1] + side, points[part + 1] - side })
            { mesh.SurfaceSetColor(color); mesh.SurfaceAddVertex(p); }
        mesh.SurfaceEnd(); _rac1ArsenalBeams[index].Mesh = mesh; _rac1ArsenalBeams[index].Visible = true;
    }

    private void DrawRac1TeslaRibbons(int index, Vector3 from, Vector3 to, Rac1EffectAssets effects)
    {
        _rac1TeslaEffectRandom ??= Rac1PickupRandom.FromSeed(unchecked((uint)(_args.FixedSeed ?? System.Environment.TickCount)));
        var ribbons = _rac1TeslaPresentation.ProjectTargetLine(Rac1LootPresentation.ToEffectNative(from), Rac1LootPresentation.ToEffectNative(to), _rac1TeslaEffectRandom);
        Vector3 eye = GetViewport().GetCamera3D()?.GlobalPosition ?? from + Vector3.Up * 5;
        for (int layer = 0; layer < ribbons.Count; layer++)
        {
            int slot = index * 4 + layer;
            while (_rac1ArsenalBeams.Count <= slot)
            {
                var node = new MeshInstance3D { CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
                _sceneResult!.Root.AddChild(node); _rac1ArsenalBeams.Add(node);
            }
            var ribbon = ribbons[layer];
            if (!_rac1TeslaMaterials.TryGetValue(ribbon.Texture, out var material))
            {
                var texture = effects.FxTextures[ribbon.Texture];
                material = new ShaderMaterial { Shader = new Shader { Code = """
                    shader_type spatial;
                    render_mode unshaded, blend_add, cull_disabled, depth_draw_never, fog_disabled;
                    uniform sampler2D art : source_color, filter_linear, repeat_enable;
                    void fragment() {
                        vec4 texel = texture(art, UV);
                        ALBEDO = texel.rgb * COLOR.rgb;
                        ALPHA = texel.a * COLOR.a;
                    }
                    """ } };
                material.SetShaderParameter("art", ImageTexture.CreateFromImage(Image.CreateFromData(texture.Width,
                    texture.Height, false, Image.Format.Rgba8, texture.Rgba)));
                _rac1TeslaMaterials.Add(ribbon.Texture, material);
            }
            var beam = _rac1ArsenalBeams[slot]; beam.MaterialOverride = material;
            var mesh = beam.Mesh as ArrayMesh;
            if (mesh is null) { var previous = beam.Mesh; mesh = new ArrayMesh(); beam.Mesh = mesh; previous?.Dispose(); }
            var positions = new List<double>(); var uvs = new List<float>(); var colors = new List<float>();
            uint rgba = ribbon.Color;
            var tint = new Color((rgba & 255) / 128f, (rgba >> 8 & 255) / 128f, (rgba >> 16 & 255) / 128f, (rgba >> 24) / 128f);
            var points = ribbon.Points.Select(Rac1LootPresentation.FromEffectNative).ToArray();
            var left = new Vector3[points.Length]; var right = new Vector3[points.Length];
            for (int i = 0; i < points.Length; i++)
            {
                Vector3 tangent = points[Math.Min(i + 1, points.Length - 1)] - points[Math.Max(i - 1, 0)];
                Vector3 side = tangent.Cross(points[i] - eye);
                if (side.LengthSquared() < .000001f) side = tangent.Cross(Vector3.Right);
                if (side.LengthSquared() < .000001f) side = Vector3.Forward;
                side = side.Normalized() * ribbon.HalfWidth;
                left[i] = points[i] - side; right[i] = points[i] + side;
            }
            for (int i = 0; i + 1 < points.Length; i++)
            {
                Vector3 la = left[i], ra = right[i], lb = left[i + 1], rb = right[i + 1];
                if (ribbon.EndExtension > 0)
                {
                    // 0x2bae40..2bafdc brings each blue segment's ends onto
                    // the nearer camera sphere, then extends it along its axis.
                    Vector3 pa = points[i], pb = points[i + 1];
                    float da = pa.DistanceTo(eye), db = pb.DistanceTo(eye);
                    if (da < db && db > .000001f) pb = eye + (pb - eye) * (da / db);
                    else if (da > .000001f) pa = eye + (pa - eye) * (db / da);
                    Vector3 extension = (pb - pa) * ribbon.EndExtension;
                    pa -= extension; pb += extension;
                    Vector3 side = (pb - pa).Cross(pa - eye).Normalized() * ribbon.HalfWidth;
                    la = pa - side; ra = pa + side; lb = pb - side; rb = pb + side;
                }
                float a = _rac1TeslaPresentation.TexturePhase, b = a + 1;
                Add(la, a, 0, i); Add(ra, a, 1, i); Add(rb, b, 1, i + 1);
                Add(la, a, 0, i); Add(rb, b, 1, i + 1); Add(lb, b, 0, i + 1);
            }
            EffectMeshSceneView.Upload(mesh, new(effects.FxTextures[ribbon.Texture].AssetKind, ribbon.Texture,
                positions.ToArray(), uvs.ToArray(), Enumerable.Range(0, positions.Count / 3).ToArray(), colors.ToArray()));
            beam.Visible = true;

            void Add(Vector3 p, float u, float v, int point)
            {
                var vertexColor = tint;
                if (point == 0 || point == points.Length - 1) vertexColor.A = 0;
                positions.AddRange([-p.X, p.Y, p.Z]); uvs.AddRange([u, v]);
                colors.AddRange([vertexColor.R, vertexColor.G, vertexColor.B, vertexColor.A]);
            }
        }
    }

    private void MorphArsenalEnemy(RuntimeWorldScene.DynamicObjectNode target)
    {
        if (_rac1Chickens.ContainsKey(target.Source.InstanceIndex) || !Rac1World.Enemies.Roster.TryGetValue(target.Source.InstanceIndex, out var enemy)) return;
        var view = CreateEquipmentWorldView(270); view.Reparent(target.Root, false); view.Position = Vector3.Up * view.FootOffset;
        view.Scale = Vector3.One / target.Root.Scale;
        foreach (var mesh in target.Root.GetChildren().OfType<MeshInstance3D>()) mesh.Visible = false;
        Rac1World.Enemies.SetRac1EnemyFlame(enemy, false); enemy.Reaction = null;
        var collider = enemy.Body.GetChild<CollisionShape3D>(0);
        var originalShape = collider.Shape; var originalPosition = collider.Position;
        collider.Shape = new CapsuleShape3D { Radius = .24f, Height = .65f }; collider.Position = Vector3.Up * .325f;
        _rac1Chickens.Add(target.Source.InstanceIndex, new(enemy, view, originalShape, originalPosition));
        _rac1MorphExposure.Remove(target.Source.InstanceIndex);
        SpawnEquipmentFlash(target.Root.GlobalPosition + Vector3.Up * .5f, 27, new(.4f, 1, .3f), 1, 12);
    }
}
