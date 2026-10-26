using System.Collections.Immutable;
using OBP.Core;

namespace OBP.Game.Diagnostics;

public sealed record DiagnosticRegistration(string Id, ImmutableArray<string> Aliases,
    Func<CommandLineArgs, bool> Requested, ObpSourceGame? WorldSource = null);

/// <summary>Explicit registration/selection facts. No root reference, scene nodes, native policy or mutable services.</summary>
public sealed class DiagnosticScenarioRegistry(IEnumerable<DiagnosticRegistration> registrations)
{
    private readonly ImmutableArray<DiagnosticRegistration> _registrations = Validate(registrations);
    public ImmutableArray<DiagnosticRegistration> Registrations => _registrations;
    public DiagnosticRegistration Find(string name) => _registrations.FirstOrDefault(registration => registration.Id == name || registration.Aliases.Contains(name, StringComparer.Ordinal))
        ?? throw new DiagnosticUnavailableException("Unknown diagnostic scenario: " + name);
    public ImmutableArray<DiagnosticRegistration> Select(CommandLineArgs args, ObpSourceGame game, int worldSwitches) => worldSwitches != 1 ? [] :
        [.. _registrations.Where(registration => registration.Requested(args) && (registration.WorldSource is null || registration.WorldSource == game))];
    private static ImmutableArray<DiagnosticRegistration> Validate(IEnumerable<DiagnosticRegistration> registrations)
    {
        var entries = registrations.ToImmutableArray();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Id) || entry.Aliases.IsDefault || !names.Add(entry.Id))
                throw new ArgumentException("Diagnostic registration requires a unique stable identity.");
            foreach (string alias in entry.Aliases)
                if (string.IsNullOrWhiteSpace(alias) || !names.Add(alias)) throw new ArgumentException("Diagnostic aliases must be unique.");
        }
        return entries;
    }
}
