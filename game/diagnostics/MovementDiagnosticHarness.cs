using System.Collections.Immutable;
using System.Numerics;
using OBP.Core;

namespace OBP.Game.Diagnostics;

public sealed class MovementDiagnosticScenario(DiagnosticScenarioDefinition definition, MovementScenarioPorts ports,
    Func<MovementDiagnosticContext, Task> execute) : IDiagnosticScenario
{
    public DiagnosticScenarioDefinition Definition { get; } = definition;
    public async Task RunAsync(DiagnosticRunContext run)
    {
        run.Own(ports.Player.PreserveDevelopmentSettings());
        var input = run.Own(new MovementDiagnosticDriver(run, ports.Input, ports.Physical));
        await execute(new(ports, input));
    }
}

/// <summary>Registers existing commands and supervises composed traversal/controller checks without root access.</summary>
internal sealed class MovementDiagnosticHarness(MovementScenarioPorts ports, IDiagnosticWaterFixture water,
    IDiagnosticEnvironmentalDeath environment, IDiagnosticControllerEquipment equipment, ObpDestination? destination,
    ImmutableArray<DiagnosticSourceRequirement> authorities, DiagnosticScenarioRunner runner, HostOperationScope operations, string? movementCase)
{
    internal MovementScenarioPorts Ports => ports;
    internal IDiagnosticControllerEquipment Equipment => equipment;
    internal IDiagnosticScenario CreateMovement(DiagnosticScenarioDefinition definition) => new DiagnosticProcessScenario(
        new MovementDiagnosticScenario(definition, ports, RunSelectedAsync),
        new("movement-smoke-pass", 0), new("movement-smoke-fail", 1));
    private async Task RunSelectedAsync(MovementDiagnosticContext context)
    {
        if (movementCase is null or "full")
        {
            await new MovementScenario(context).RunAsync(destination ?? throw new DiagnosticUnavailableException("A movement destination is required."));
            return;
        }
        int level = movementCase == "swim" ? 1 : 0;
        if (ports.World.Read() is not { Source: ObpSourceGame.Rac1 } world || world.LevelId != level)
        { throw new DiagnosticUnavailableException($"Movement case {movementCase} requires R&C1 LEVEL{level}."); }
        using var rate = ports.World.LimitFrameRate(60);
        // Swimming stages its own authored pool and starts with three physics waits.
        // Do not prepend a ground trial to that original water fixture.
        if (movementCase != "swim")
        {
            await context.Driver.WaitGroundedAsync(() => ports.Player.Read().Grounded,
                () => ports.Player.Read().Rac1MovementPhase == OBP.RAC1.Player.Rac1RatchetMovementPhase.Grounded, 360);
        }
        await (movementCase switch
        {
            "airborne" => new Rac1AirborneRegression(context).RunAsync(),
            "environmental-death" => new Rac1EnvironmentalDeathRegression(context, environment).RunAsync(ports.Player.Read().GlobalPosition),
            "landing" => new Rac1LandingRegression(context).RunAsync(),
            "swim" => new Rac1NovalisSwimScenario(context, water).RunAsync(),
            "slope-jump" => new Rac1SlopeJumpRegression(context).RunAsync(),
            "small-step" => new Rac1SmallStepRegression(context).RunAsync(),
            "swingshot" => new Rac1SwingshotRegression(context).RunAsync(),
            "traversal-contact" => new Rac1TraversalContactRegression(context).RunAsync(),
            "controller-weapons" => new ControllerWeaponInputRegression(context, equipment).RunAsync(),
            _ => throw new DiagnosticUnavailableException("Unknown movement regression: " + movementCase),
        });
        ports.Log("[movement-case] PASS " + movementCase);
    }
    internal IDiagnosticScenario CreateSwim(DiagnosticScenarioDefinition definition) => new DiagnosticProcessScenario(
        new MovementDiagnosticScenario(definition, ports, context => new Rac1NovalisSwimScenario(context, water).RunAsync()),
        new("rac1-novalis-swim-smoke-pass", 0), new("rac1-novalis-swim-smoke-fail", 1));
    internal Task RunFallAsync(Vector3 respawn, CancellationToken lifetime) => RunHelperAsync("rac1-natural-veldin-fall", context =>
        new Rac1EnvironmentalDeathRegression(context, environment).RunAsync(respawn), lifetime);
    internal Task RunControllerAsync(CancellationToken lifetime) => RunHelperAsync("controller-weapon-input", context =>
        new ControllerWeaponInputRegression(context, equipment).RunAsync(), lifetime);
    internal async Task<float> MeasureJumpAsync(int holdFrames, bool partialAir, bool moving, CancellationToken lifetime)
    {
        float height = 0;
        await RunHelperAsync("movement-jump", async context => height = await new MovementScenario(context)
            .MeasureJumpAsync(ports.Player, holdFrames, partialAir, moving), lifetime);
        return height;
    }
    internal Task WaitSequenceAsync(int sequence, int maxFrames, string label, CancellationToken lifetime) =>
        RunHelperAsync("movement-avatar-wait", context => new MovementWaits(context).SequenceAsync(sequence, maxFrames, label), lifetime);
    private Task RunHelperAsync(string id, Func<MovementDiagnosticContext, Task> execute, CancellationToken lifetime)
    {
        lifetime.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource(); // Keep the existing inline engine signal continuation.
        operations.Start(id, async token =>
        {
            try
            {
                var definition = new DiagnosticScenarioDefinition(id + "/v1", DiagnosticSourceAccess.ExplicitSourceDiagnostic,
                    DiagnosticSetup.AssetFreeFixture, authorities, TimeSpan.FromMinutes(10), 108_000);
                var result = await runner.RunAsync(new MovementDiagnosticScenario(definition, ports, execute), true, token);
                if (result.Status == DiagnosticRunStatus.Passed) { completion.TrySetResult(); }
                else if (result.Status == DiagnosticRunStatus.Canceled && token.IsCancellationRequested)
                { completion.TrySetCanceled(lifetime.IsCancellationRequested ? lifetime : token); }
                else completion.TrySetException(result.Error ?? new InvalidOperationException(result.Detail));
            }
            catch (Exception error) { completion.TrySetException(error); }
        }, lifetime);
        return completion.Task;
    }
    private sealed class MovementWaits(MovementDiagnosticContext context) : MovementScenarioSupport(context)
    {
        public Task SequenceAsync(int sequence, int frames, string label) => WaitForPlayerAvatarSequenceAsync(sequence, frames, label);
    }
}
