using OBP.Game;
using OBP.RAC1.Gameplay;
using OBP.Runtime;

namespace OBP.Tests;

public sealed class FusionWeaponLifetimeTests
{
    private sealed class Assets : IDisposable
    {
        internal int Disposals { get; private set; }
        public void Dispose() => Disposals++;
    }

    [Fact]
    public async Task UnchangedSourceSetRetainsPreparationAndAssetsUntilExplicitInvalidation()
    {
        int workers = 0, uploads = 0;
        using var owner = new FusionWeaponSourceHost<Assets>(_ => { uploads++; return new(); }, _ =>
        {
            workers++;
            return new FusionWeaponPreparation((_, _) => new FusionWeaponData([]));
        });
        Assert.True(owner.Prepare([]));
        var preparation = owner.Preparation!;
        Assert.False(owner.Prepare([]));
        Assert.Same(preparation, owner.Preparation);
        await preparation.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(owner.Tick(_ => { }, _ => { }, Assert.Fail));
        var assets = owner.Assets!;
        Assert.False(owner.Prepare([]));
        Assert.Same(assets, owner.Assets);
        Assert.False(owner.Preparing);
        Assert.Equal(1, workers);
        Assert.Equal(1, uploads);
        Assert.Equal(0, assets.Disposals);
        owner.Reset();
        owner.Reset();
        Assert.Equal(1, assets.Disposals);
        Assert.Null(owner.Assets);
        Assert.False(owner.Matches([]));
    }

    [Fact]
    public async Task ReplacedWorkerCannotPublishIntoTheNewSourceLifetime()
    {
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int workers = 0, uploads = 0;
        using var owner = new FusionWeaponSourceHost<Assets>(_ => { uploads++; return new(); }, _ =>
        {
            bool stale = workers++ == 0;
            return new FusionWeaponPreparation((_, _) =>
            {
                if (stale) { entered.SetResult(); release.Wait(CancellationToken.None); }
                // Deliberately ignore cancellation to prove publication is owner-scoped.
                return new FusionWeaponData([]);
            });
        });
        owner.Prepare([]);
        var stalePreparation = owner.Preparation!;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            owner.Prepare([], force: true);
            var current = owner.Preparation!;
            await current.Completion.WaitAsync(TimeSpan.FromSeconds(10));
            owner.Tick(_ => { }, _ => { }, Assert.Fail);
            var currentAssets = owner.Assets;
            release.Set();
            await stalePreparation.Completion.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(owner.Tick(_ => Assert.Fail("A stale worker was published."), _ => { }, Assert.Fail));
            Assert.Same(currentAssets, owner.Assets);
            Assert.Equal(1, uploads);
        }
        finally { release.Set(); }
    }

    [Fact]
    public async Task FailedTextureUploadAndRetiredPreparationLeaveAnExplicitFailure()
    {
        using var owner = new FusionWeaponSourceHost<Assets>(_ => throw new InvalidDataException("missing texture"),
            _ => new FusionWeaponPreparation((_, _) => new FusionWeaponData([])));
        owner.Prepare([]);
        await owner.Preparation!.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        string? error = null;
        Assert.True(owner.Tick(_ => Assert.Fail("Failed upload was attached."), _ => { }, text => error = text));
        Assert.Null(owner.Assets);
        Assert.False(owner.Preparing);
        Assert.Contains("Game Sources", owner.Failure!);
        Assert.Contains("missing texture", error!);
        owner.Reset();
        Assert.Null(owner.Failure);
    }

    [Fact]
    public void ExplicitFixtureRestoresCampaignWalletAndGoldWithoutTouchingThePersistentPlayer()
    {
        var campaign = new Rac1CampaignHost();
        campaign.Wallet = new(123);
        var original = campaign.Session;
        var wallet = campaign.Wallet;
        var gold = new HashSet<int> { 4, 9 };
        var native = new PlayerSessionNativeState(original.Weapons, wallet.Balance, 1440);
        var persistentInventory = native.Inventory.CapturePersistentState();
        var fixture = new Rac1FusionCampaignIsolation(campaign, gold, new RuntimeSpawn(1, 2, 3, 0));
        Assert.Same(original.Weapons, fixture.OriginalInventory);
        Assert.NotSame(original, campaign.Session);
        Assert.Empty(gold);
        gold.Add(99);
        campaign.Session.Weapons.TryEquip(Rac1WeaponId.FirstRanged);
        campaign.Session.Weapons.TryConsumeAmmo(10, 1);
        fixture.Dispose();
        Assert.Same(original, campaign.Session);
        Assert.Same(wallet, campaign.Wallet);
        Assert.Equal(new[] { 4, 9 }, gold.Order().ToArray());
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(persistentInventory),
            System.Text.Json.JsonSerializer.Serialize(native.Inventory.CapturePersistentState()));
        gold.Add(12);
        fixture.Dispose();
        Assert.Contains(12, gold);
    }
}
