using OBP.Core;
using OBP.IO;
using OBP.PS2;
using OBP.PS2.Audio;
using OBP.PS2.Iso;
using OBP.RAC2.Gameplay;
using OBP.RAC2.Level;
using OBP.RAC3.Gameplay;
using OBP.RAC3.Level;
using OBP.Runtime;
using OBP.Runtime.Audio;
using OBP.Runtime.Diagnostics;
using OBP.Runtime.Gameplay;
using OBP.Runtime.Presentation;
using OBP.Runtime.Player;

namespace OneBigPackage;

/// <summary>Verified CPU-side weapon data. No Godot resources or scene access.</summary>
public sealed class FusionWeaponData
{
    public sealed record Voice(double Delay, bool Loop, RuntimeAudioClip Clip, double Gain);
    public Dictionary<SourceWeaponKey, string> Problems { get; } = [];
    public List<RuntimeTexture> Textures { get; } = [];
    public Dictionary<(SourceWeaponKey, int), IReadOnlyList<Voice>> Audio { get; } = [];
    public GcLancerAssets.HeldAssets? Lancer { get; private set; }
    public UyaGlobalGadget.Source? Cannon { get; private set; }
    public Dictionary<SourceWeaponKey, PlayerEquipmentAnimationSource> AvatarAnimations { get; } = [];
    public IReadOnlyList<RuntimeObjectMesh> Projectile { get; private set; } = [];
    private readonly Dictionary<ObpSourceGame, string> _identities = [];
    public int ReusedVerifications { get; private set; }
    public PlaytestWeaponConfiguration Configuration(bool isolated) => new(
        isolated ? FusionWeaponSession.LoadoutId : FusionWeaponSession.DefaultLoadoutId, FusionDamagePolicy.Id,
        new SortedDictionary<string, string>(_identities.ToDictionary(pair => SourceIdentityWire.FormatSource(pair.Key), pair => pair.Value), StringComparer.Ordinal));

    public FusionWeaponData(IReadOnlyList<ObpAttachedSource> sources,
        IProgress<string>? progress = null, CancellationToken cancellationToken = default,
        FileVerificationCache? verificationCache = null)
    {
        Load(ObpSourceGame.Rac1, FusionWeaponSession.BombGlove, _ => { });
        Load(ObpSourceGame.Rac2, FusionWeaponSession.Lancer, disc =>
        {
            var fs = Iso9660Filesystem.Open(disc);
            Lancer = GcLancerAssets.ReadHeld(fs.OpenFile(GcLancerAssets.GadgetIsoPath)!);
            Textures.Add(GcLancerPresentation.HeldTexture(Lancer));
            var wad = fs.OpenFile("/G/LEVEL1.WAD")!;
            var core = GcLevelCore.Open(GcLevelWad.RequireLump(wad, GcLevelWad.ReadHeader(wad), 0));
            AvatarAnimations.Add(FusionWeaponSession.Lancer, OBP.RAC2.Player.GcRatchetSkeletalSource.ReadLancer(core));
            var projectile = GcMobyClasses.Read(core)[GcLancerProfile.ProjectileMobyClass];
            Projectile = GcLancerPresentation.ProjectileModel(projectile);
            foreach (var texture in GcLevelTextures.Read(core, GcLevelTextures.Table.Moby).Where(t => projectile.TextureIds.Contains(t.Index)))
                Textures.Add(new(GcLancerPresentation.ProjectileAssetKind, texture.Index, texture.Width, texture.Height, texture.Rgba));
            for (int cue = 0; cue <= 1; cue++)
            {
                var voices = Ps2SoundRenderer989.TryRenderVoices(Lancer.SoundBank.Sounds[cue], Lancer.SoundBank.DecodeTone, seed: 0)
                    ?? throw new NotSupportedException($"Lancer sound {cue} needs an unsupported grain decoder.");
                Audio.Add((FusionWeaponSession.Lancer, cue), voices.Select(v => new Voice(
                    v.StartFrame / (double)Ps2SoundRenderer989.SampleRate, v.Loop,
                    new(Ps2SoundRenderer989.SampleRate, 1, v.Audio.Samples, v.Audio.LoopStartFrame, v.Audio.LoopEndFrame), .7)).ToArray());
            }
            _ = GcLancerPresentation.HeldModel(Lancer, GcLancerProfile.HeldIdleSequence, 0);
        });
        Load(ObpSourceGame.Rac3, FusionWeaponSession.ShockCannon, disc =>
        {
            Cannon = UyaGlobalGadget.Read(disc, UyaShockCannonProfile.HeldMobyClass);
            var core = UyaLevelCore.Open(disc, 1).Core;
            AvatarAnimations.Add(FusionWeaponSession.ShockCannon, OBP.RAC3.Player.UyaRatchetSkeletalSource.ReadShockCannon(core));
            foreach (var texture in UyaShockCannonAssets.Textures(Cannon).Concat(
                UyaFxTextures.Read(core, new HashSet<int> { 10, 23 }).Values)) Textures.Add(texture);
            for (int cue = 0; cue < 6; cue++)
            {
                var sound = UyaShockCannonAssets.Audio(Cannon, cue, seed: 0);
                Audio.Add((FusionWeaponSession.ShockCannon, cue), sound.Voices.Select(v => new Voice(
                    v.StartSeconds, v.Loop, v.Clip, sound.HostGain(0))).ToArray());
            }
            _ = UyaShockCannonAssets.Model(Cannon, 0, 0);
        });
        cancellationToken.ThrowIfCancellationRequested();

        void Load(ObpSourceGame game, SourceWeaponKey weapon, Action<FileRandomAccessReader> decode)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string title = game switch { ObpSourceGame.Rac1 => "Ratchet & Clank", ObpSourceGame.Rac2 => "Going Commando", _ => "Up Your Arsenal" };
            _identities[weapon.SourceGame] = "missing";
            var source = sources.FirstOrDefault(s => s.Game == game);
            if (source is null) { Problems[weapon] = $"Attach {title} NTSC-U in Game Sources."; return; }
            try
            {
                progress?.Report($"Checking {title} source…");
                using var disc = new FileRandomAccessReader(source.Path);
                string expectedHash = source.Identity.Sha256 ?? throw new InvalidDataException("Source has no pinned authority SHA-256.");
                var hashProgress = new HashProgress(title, progress);
                var verification = verificationCache?.Verify(source.Path, disc, expectedHash, hashProgress, cancellationToken);
                string hash = verification?.Sha256 ?? Hashing.Sha256Hex(disc, progress: hashProgress, cancellationToken: cancellationToken);
                if (verification?.Reused == true) ReusedVerifications++;
                if (!string.Equals(hash, expectedHash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Disc SHA-256 differs from the supported original NTSC-U build.");
                _identities[weapon.SourceGame] = source.Identity.BuildId + "/sha256:" + hash;
                progress?.Report($"Preparing {title} weapon…");
                cancellationToken.ThrowIfCancellationRequested();
                decode(disc);
            }
            catch (Exception error) when (error is IOException or InvalidDataException or NotSupportedException or ArgumentException)
            { Problems[weapon] = $"{title} weapon unavailable: {error.Message} Attach the supported NTSC-U source in Game Sources."; }
        }
    }

    private sealed class HashProgress(string title, IProgress<string>? target) : IProgress<double>
    {
        public void Report(double value) => target?.Report($"Verifying {title} source… {value * 100:0}%");
    }
}

/// <summary>Cancelable background preparation; only the main thread publishes its result.</summary>
public sealed class FusionWeaponPreparation : IDisposable, IProgress<string>
{
    private readonly CancellationTokenSource _cancellation = new();
    private string _status = "Preparing cross-game weapons…";
    private int _disposed;
    public string Status => Volatile.Read(ref _status);
    public Task<FusionWeaponData> Completion { get; }

    public FusionWeaponPreparation(IReadOnlyList<ObpAttachedSource> sources, FileVerificationCache? verificationCache = null)
        : this((progress, token) => new FusionWeaponData(sources, progress, token, verificationCache)) { }

    internal FusionWeaponPreparation(Func<IProgress<string>, CancellationToken, FusionWeaponData> prepare)
    {
        var token = _cancellation.Token;
        Completion = Task.Run(() => prepare(this, token), token);
    }

    public void Report(string value) => Volatile.Write(ref _status, value);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _cancellation.Cancel();
        _ = Completion.ContinueWith(task =>
        {
            _ = task.Exception;
            _cancellation.Dispose();
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }
}
