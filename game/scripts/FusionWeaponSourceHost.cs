using OBP.Core;

namespace OBP.Game;

/// <summary>Verified weapon assets live for an attached source set, across world leases.</summary>
internal sealed class FusionWeaponSourceHost<TAssets>(Func<FusionWeaponData, TAssets> upload,
    Func<IReadOnlyList<ObpAttachedSource>, FusionWeaponPreparation> prepare) : IDisposable where TAssets : class, IDisposable
{
    private ObpAttachedSource[]? _snapshot;
    private FusionWeaponPreparation? _preparation;
    private TAssets? _assets;

    internal FusionWeaponPreparation? Preparation => _preparation;
    internal TAssets? Assets
    {
        get => _assets;
        set => _assets = value;
    }
    internal string? LoadingStatus { get; private set; }
    internal string? Failure { get; private set; }
    internal bool Preparing => _preparation is not null;
    internal bool Matches(IEnumerable<ObpAttachedSource> attached) => _snapshot is not null &&
        attached.OrderBy(source => source.Game).SequenceEqual(_snapshot);

    internal bool Prepare(IEnumerable<ObpAttachedSource> attached, bool force = false)
    {
        var snapshot = attached.OrderBy(source => source.Game).ToArray();
        if (!force && _snapshot is not null && snapshot.SequenceEqual(_snapshot)) return false;
        Reset();
        _snapshot = snapshot;
        _preparation = prepare(snapshot);
        return true;
    }

    /// <summary>Complete texture upload on the main thread after background verification finishes.</summary>
    internal bool Tick(Action<TAssets> attached, Action<string> log, Action<string> logError)
    {
        if (_preparation is not { } preparation) return false;
        if (!preparation.Completion.IsCompleted)
        {
            LoadingStatus = preparation.Status;
            return false;
        }
        _preparation = null;
        try
        {
            var data = preparation.Completion.GetAwaiter().GetResult();
            _assets = upload(data);
            attached(_assets);
            foreach (var problem in data.Problems.Values) log("[fusion] " + problem);
            log($"[fusion] shared weapon preparation complete; reused {data.ReusedVerifications}/3 saved verifications; retained for this source session");
        }
        catch (Exception error)
        {
            _assets?.Dispose();
            _assets = null;
            Failure = "Weapon preparation failed. Reattach the source in Game Sources.";
            logError("[fusion] weapon preparation failed: " + error);
        }
        finally { preparation.Dispose(); }
        LoadingStatus = null;
        return true;
    }

    internal void Reset()
    {
        var preparation = _preparation;
        _preparation = null;
        var assets = _assets;
        _assets = null;
        _snapshot = null;
        LoadingStatus = Failure = null;
        try { preparation?.Dispose(); }
        finally { assets?.Dispose(); }
    }

    public void Dispose() => Reset();
}
