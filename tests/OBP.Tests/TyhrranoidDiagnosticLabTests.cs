using OBP.Core;
using OBP.Fusion;

namespace OBP.Tests;

/// <summary>
/// OBP diagnostic-only layout. Independent eight-actor PVar lives and source
/// registration, not a claim of a retail-authored Maktar wave.
/// </summary>
public sealed class TyhrranoidDiagnosticLabTests
{
    [Fact]
    public void DiagnosticSamplerUsesVerifiedActionAndHoldsThenRepeatsWithoutAffectingNativeState()
    {
        double[] durations = Enumerable.Repeat(.1d, 20).ToArray();
        var clip = new OBP.Runtime.RuntimeObjectAnimationClip("uya-class5821-state10-action12",
            OBP.Runtime.RuntimeObjectAnimationRole.Reaction, [new(0, [.. Enumerable.Range(0, 20).Select(_ => new double[] { 0, 0, 0 })])],
            durations);

        var start = OBP.RAC3.Rac3WorldImport.SampleClass5821Diagnostic(clip, 0);
        Assert.Equal(0, start.Frame);
        Assert.Equal(1, start.NextFrame);
        Assert.Equal(12, OBP.RAC3.Rac3WorldImport.SampleClass5821Diagnostic(clip, 75).Frame);
        Assert.Equal(19, OBP.RAC3.Rac3WorldImport.SampleClass5821Diagnostic(clip, 130).Frame);
        Assert.Equal(0, OBP.RAC3.Rac3WorldImport.SampleClass5821Diagnostic(clip, 180).Frame);
        var unidentified = clip with { Id = "uya-class5821-unassigned-sequence00" };
        Assert.Equal(0, OBP.RAC3.Rac3WorldImport.SampleClass5821Diagnostic(unidentified, 0).Frame);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            OBP.RAC3.Rac3WorldImport.SampleClass5821Diagnostic(clip, -1));
        Assert.Throws<ArgumentException>(() =>
            OBP.RAC3.Rac3WorldImport.SampleClass5821Diagnostic(clip with { Id = "unverified" }, 0));
    }

    [Fact]
    public void EightSourceQualifiedOneEyedActorsAreIsolatedFromTheNormalLab()
    {
        string data = Path.Combine(RepoPaths.Root, "game", "data");
        var regular = FusionLabDefinition.Parse(File.ReadAllText(Path.Combine(data, "fusion_lab.json")));
        var trial = FusionLabDefinition.Parse(File.ReadAllText(Path.Combine(data, "fusion_lab_tyhrranoid_test.json")));

        Assert.Equal(3, regular.Actors.Count);
        Assert.Equal(8, trial.Actors.Count);
        Assert.Equal(8, trial.Actors.Select(actor => actor.Id).Distinct().Count());
        Assert.Equal("obp:fusion-lab", trial.Id);
        Assert.All(trial.Actors, actor =>
        {
            Assert.Equal(ObpSourceGame.Rac3, actor.Source.Game);
            Assert.Equal("rac3-ntscu-original", actor.Source.Build);
            Assert.Equal(5821, actor.NativeClassId);
            Assert.Equal(1, actor.LevelId);
            Assert.Equal("rac3/class5821-damage-only/v1", actor.AdapterId);
            Assert.Equal("uya/table1-class5821-damage-only/v1", actor.NativeContextId);
            Assert.Equal(430, actor.SourceInstanceIndex);
            Assert.Equal(0, actor.InitialNativeState);
        });
    }
}
