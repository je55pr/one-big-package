using Godot;
using OBP.Godot;
using OBP.IO;
using OBP.RAC3;
using OBP.RAC3.Gameplay;
using OBP.RAC3.Level;
using OBP.Runtime;
using OBP.Runtime.Audio;
using OBP.Runtime.Presentation;
using System.Security.Cryptography;
using V3 = System.Numerics.Vector3;
using Environment = System.Environment;

// Standalone source-asset fixture. Its cameras, mount and sound gain are preview
// policy; it does not claim a recovered Ratchet attachment or normal gameplay.
public partial class Preview : Node
{
    private readonly Rac3ShockCannonSession _session = new();
    private readonly List<(double Due, int Owner, Rac3ShockCannonAssets.Voice Voice, double Gain)> _pending = [];
    private readonly List<(int Owner, AudioStreamPlayer Player)> _players = [];
    private FileRandomAccessReader _disc = null!;
    private Rac3GlobalGadget.Source _source = null!;
    private Dictionary<(string, int), ImageTexture> _textures = [];
    private Node3D _gun = null!, _beam = null!;
    private Label _status = null!;
    private Rac3ShockCannonFrame _frame = null!;
    private Rac3ShockCannonBeam.Scroll _scroll;
    private int _tick, _shotCount, _beamFrames, _audioStarted, _sequence, _seed;
    private bool _smoke, _captured, _capturePending;
    private double TimeSeconds => _tick / 60d;

    public override void _Ready()
    {
        try
        {
            _smoke = OS.GetCmdlineUserArgs().Contains("--smoke");
            GetWindow().Unfocusable = _smoke;
            _disc = new(Environment.GetEnvironmentVariable("OBP_UYA_ISO") ?? throw new InvalidOperationException("UYA ISO missing."));
            if (_disc.Length != Rac3Authority.PrimaryIsoSizeBytes ||
                !Convert.ToHexString(SHA256.HashData(_disc.Read(1463L * 2048, 771008))).Equals(
                    "200068cb4186715521cdc1c20bc6c5a88054d0da54aa336ffdb2022633351c7d", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Preview requires the supported original NTSC-U UYA source.");
            _source = Rac3GlobalGadget.Read(_disc, Rac3ShockCannonProfile.HeldMobyClass);
            var fx = Rac3FxTextures.Read(Rac3LevelCore.Open(_disc, 1).Core, new HashSet<int> { 10, 23 });
            foreach (var texture in Rac3ShockCannonAssets.Textures(_source).Concat(fx.Values))
            {
                using var image = Image.CreateFromData(texture.Width, texture.Height, false, Image.Format.Rgba8, texture.Rgba);
                _textures.Add((texture.AssetKind, texture.TextureId), ImageTexture.CreateFromImage(image));
            }
            var layout = new HBoxContainer(); layout.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect); AddChild(layout);
            _gun = View(layout, new(1.5f, 1, 1.5f), new(-.25f, 0, 0), 2.9f, 1);
            _beam = View(layout, new(-13, 8, 23), new(-13, 0, 0), 30, 2);
            Render(_gun, Rac3ShockCannonAssets.Model(_source, 0, 0));
            var overlay = new CanvasLayer(); AddChild(overlay);
            _status = new Label { Position = new(20, 20), Text = "UYA Shock Cannon — original gadget / beam assets\nSpace / Circle: hold to charge • 1/2/3: source model clip • Esc: quit\nPreview mount and cameras; Ratchet attachment remains unrecovered." };
            overlay.AddChild(_status);
            _session.Equip();
            _frame = _session.Advance(new(false));
            // Exercise all six decoders, including the normally rare empty cue.
            for (int cue = 0; cue < 6; cue++)
                foreach (var voice in Rac3ShockCannonAssets.Audio(_source, cue, 0).Voices)
                    if (!RuntimeAudioHost.TryPrepare(new(voice.Clip, RuntimeAudioCategory.SoundEffect, voice.Loop), out _, out var problem))
                        throw new InvalidOperationException(problem);
        }
        catch (Exception error) { Fail(error); }
    }

    private static Node3D View(HBoxContainer parent, Vector3 cameraAt, Vector3 lookAt, float size, float ratio)
    {
        var container = new SubViewportContainer { Stretch = true, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill, SizeFlagsStretchRatio = ratio };
        parent.AddChild(container);
        var viewport = new SubViewport { OwnWorld3D = true, Size = new(640, 720), RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        container.AddChild(viewport);
        var root = new Node3D(); viewport.AddChild(root);
        var camera = new Camera3D { Position = cameraAt, Projection = Camera3D.ProjectionType.Orthogonal, Size = size, Far = 100 };
        root.AddChild(camera); camera.LookAt(lookAt);
        var environment = new WorldEnvironment { Environment = new() { BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new(.025f, .03f, .045f), TonemapMode = Godot.Environment.ToneMapper.Linear } };
        root.AddChild(environment);
        var geometry = new Node3D(); root.AddChild(geometry); return geometry;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_source is null) return;
        try
        {
            _tick++;
            if (_smoke && _tick is 4 or 100)
                Input.ParseInputEvent(new InputEventKey { Keycode = Key.Space, Pressed = _tick == 4 });
            bool held = Input.IsKeyPressed(Key.Space) || Input.GetConnectedJoypads().Any(device => Input.IsJoyButtonPressed(device, JoyButton.B));
            _frame = _session.Advance(new(held));
            _shotCount += _frame.Shots.Count;
            if (_frame.Effects.Any(e => e.Kind == Rac3ShockCannonEffectKind.Beam)) _beamFrames++;
            foreach (var cue in _frame.Audio) Audio(cue);
            foreach (var pending in _pending.Where(p => p.Due <= TimeSeconds).ToArray())
            {
                StartVoice(pending.Owner, pending.Voice, pending.Gain); _pending.Remove(pending);
            }
            if (_smoke && _tick >= 145)
            {
                foreach (var cue in _session.Unequip().Audio) Audio(cue);
                if (_shotCount < 2 || _beamFrames != 29 || _audioStarted < 10 || _pending.Any(p => p.Owner == 3) ||
                    _players.Any(p => p.Owner == 3 && GodotObject.IsInstanceValid(p.Player)))
                    throw new InvalidOperationException("Incomplete input, beam or owned-audio lifecycle.");
                if (Environment.GetEnvironmentVariable("OBP_UYA_PREVIEW_CAPTURE") is not null && !_captured)
                    throw new InvalidOperationException("Rendered capture did not complete.");
                GD.Print($"UYA_SHOCK_CANNON_PREVIEW_PASS shots={_shotCount} beam_updates={_beamFrames} audio_voices={_audioStarted}");
                GetTree().Quit();
            }
        }
        catch (Exception error) { Fail(error); }
    }

    public override void _Process(double delta)
    {
        if (_source is null) return;
        try
        {
            int frame = (int)(TimeSeconds * 30) % _source.Sequences[_sequence].Frames.Count;
            Render(_gun, Rac3ShockCannonAssets.Model(_source, _sequence, frame));
            if (_frame.Effects.Any(e => e.Kind == Rac3ShockCannonEffectKind.Beam))
            {
                _scroll = _scroll.Advance();
                Render(_beam, Rac3ShockCannonBeam.Build(_frame, Rac3ShockCannonBeam.Basis.FromNativeAim(V3.UnitX, V3.Zero), _scroll).Select(l => l.Mesh));
                if (_tick >= 84 && !_captured && !_capturePending && Environment.GetEnvironmentVariable("OBP_UYA_PREVIEW_CAPTURE") is { } path)
                    Capture(path);
            }
            else Render(_beam, []);
        }
        catch (Exception error) { Fail(error); }
    }

    private async void Capture(string path)
    {
        _capturePending = true;
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        if (image.SavePng(path) != Error.Ok) { Fail(new InvalidOperationException("Preview capture failed.")); return; }
        _captured = true;
    }

    private void Render(Node3D root, IEnumerable<RuntimeObjectMesh> surfaces)
    {
        foreach (var child in root.GetChildren()) { root.RemoveChild(child); child.QueueFree(); }
        foreach (var surface in surfaces)
        {
            int count = surface.Positions.Length / 3;
            var vertices = new Vector3[count]; var uv = new Vector2[count]; var colors = new Color[count];
            for (int i = 0; i < count; i++)
            {
                vertices[i] = RuntimeWorldScene.ToScene(surface.Positions[i * 3], surface.Positions[i * 3 + 1], surface.Positions[i * 3 + 2]);
                uv[i] = new(surface.Uvs[i * 2], surface.Uvs[i * 2 + 1]);
                colors[i] = surface.Colors is { } c ? new(c[i * 4], c[i * 4 + 1], c[i * 4 + 2], c[i * 4 + 3]) : Colors.White;
            }
            using var arrays = new Godot.Collections.Array(); arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = vertices; arrays[(int)Mesh.ArrayType.TexUV] = uv;
            arrays[(int)Mesh.ArrayType.Color] = colors; arrays[(int)Mesh.ArrayType.Index] = surface.Indices;
            using var mesh = new ArrayMesh(); mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            var plan = WorldMaterialFactory.ResolveNativePresentation(surface.MaterialPresentation);
            using var material = new StandardMaterial3D { AlbedoTexture = _textures[(surface.AssetKind, surface.TextureId)],
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                VertexColorUseAsAlbedo = true, TextureRepeat = plan.TextureRepeat ?? true,
                TextureFilter = plan.TextureFilter ?? BaseMaterial3D.TextureFilterEnum.Linear,
                BlendMode = plan.BlendMode, Transparency = plan.Transparency ?? BaseMaterial3D.TransparencyEnum.Disabled };
            mesh.SurfaceSetMaterial(0, material); root.AddChild(new MeshInstance3D { Mesh = mesh });
        }
    }

    private void Audio(Rac3ShockCannonAudio cue)
    {
        if (cue.Operation == Rac3ShockCannonAudioOperation.StopLoop)
        {
            _pending.RemoveAll(p => p.Owner == cue.LocalSoundIndex);
            foreach (var owned in _players.Where(p => p.Owner == cue.LocalSoundIndex).ToArray())
            {
                if (GodotObject.IsInstanceValid(owned.Player)) { owned.Player.Stop(); owned.Player.QueueFree(); }
                _players.Remove(owned);
            }
            return;
        }
        var sound = Rac3ShockCannonAssets.Audio(_source, cue.LocalSoundIndex, _seed++);
        int owner = cue.Operation == Rac3ShockCannonAudioOperation.StartLoop ? cue.LocalSoundIndex : -1;
        foreach (var voice in sound.Voices) _pending.Add((TimeSeconds + voice.StartSeconds, owner, voice, sound.HostGain(0)));
    }

    private void StartVoice(int owner, Rac3ShockCannonAssets.Voice voice, double gain)
    {
        if (!RuntimeAudioHost.TryPrepare(new(voice.Clip, RuntimeAudioCategory.SoundEffect, voice.Loop, gain), out var clip, out var problem))
            throw new InvalidOperationException(problem);
        using var stream = new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, Data = clip!.Pcm16LittleEndian,
            MixRate = clip.SampleRateHz, LoopMode = voice.Loop ? AudioStreamWav.LoopModeEnum.Forward : AudioStreamWav.LoopModeEnum.Disabled,
            LoopBegin = clip.LoopStartFrame, LoopEnd = clip.LoopEndFrame!.Value };
        var player = new AudioStreamPlayer { Stream = stream, VolumeDb = clip.VolumeDb }; AddChild(player);
        var owned = (owner, player); _players.Add(owned);
        player.Finished += () => { _players.Remove(owned); player.QueueFree(); };
        player.Play(); _audioStarted++;
    }

    public override void _UnhandledInput(InputEvent input)
    {
        if (input is InputEventKey { Pressed: true, Echo: false } key)
        {
            if (key.Keycode == Key.Escape) GetTree().Quit();
            if (key.Keycode >= Key.Key1 && key.Keycode <= Key.Key3) _sequence = (int)(key.Keycode - Key.Key1);
        }
    }

    private void Fail(Exception error) { GD.PrintErr(error); SetProcess(false); SetPhysicsProcess(false); GetTree().Quit(1); }
    public override void _ExitTree()
    {
        foreach (var owned in _players) if (GodotObject.IsInstanceValid(owned.Player)) owned.Player.Stop();
        _disc?.Dispose();
        foreach (var texture in _textures.Values) texture.Dispose();
    }
}
