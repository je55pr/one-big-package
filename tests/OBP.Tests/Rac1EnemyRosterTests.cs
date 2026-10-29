using OBP.Core;
using OBP.Game;

namespace OBP.Tests;

public sealed class Rac1EnemyRosterTests
{
    private sealed record Contact(ObpSourceGame Source, int Instance);
    private static Rac1EnemyRoster<Contact> Create(Action<Contact> retire) => new(contact => (contact.Source, contact.Instance), retire);

    [Fact]
    public void EqualNativeNumbersAcrossSourcesCannotEnterTheRac1ContactSet()
    {
        using var roster = Create(_ => { });
        var rac1 = new Contact(ObpSourceGame.Rac1, 7);
        roster.Add(7, rac1);
        Assert.Throws<InvalidDataException>(() => roster.Add(8, new(ObpSourceGame.Rac2, 8)));
        Assert.Throws<InvalidDataException>(() => roster.Add(9, new(ObpSourceGame.Rac1, 10)));
        Assert.Single(roster.Values);
        Assert.True(roster.Contains(rac1));
        Assert.False(roster.Contains(new(ObpSourceGame.Rac1, 7)));
    }

    [Fact]
    public void RetirementReleasesEveryFamilyEvenWhenOneContactFails()
    {
        var retired = new List<int>();
        var roster = Create(contact =>
        {
            retired.Add(contact.Instance);
            if (contact.Instance == 459) throw new IOException("birdbot contact release");
        });
        foreach (int id in new[] { 749, 459, 577, 193 }) roster.Add(id, new(ObpSourceGame.Rac1, id));
        var error = Assert.Throws<AggregateException>(roster.Dispose);
        Assert.Single(error.InnerExceptions);
        Assert.Equal(new[] { 749, 459, 577, 193 }, retired);
        Assert.Empty(roster.Values);
        roster.Dispose();
        Assert.Equal(4, retired.Count);
        Assert.Throws<ObjectDisposedException>(() => roster.Add(1, new(ObpSourceGame.Rac1, 1)));
    }

    [Fact]
    public void ReloadAndDelayedRetirementCannotRemoveTheSuccessorsEqualNativeId()
    {
        int retired = 0;
        var first = Create(_ => retired++);
        var old = new Contact(ObpSourceGame.Rac1, 7);
        first.Add(7, old);
        first.Dispose();
        using var next = Create(_ => retired++);
        var replacement = new Contact(ObpSourceGame.Rac1, 7);
        next.Add(7, replacement);
        Assert.False(first.Contains(old));
        Assert.False(first.Remove(7, out _));
        first.Dispose();
        Assert.True(next.Contains(replacement));
        Assert.False(next.Contains(old));
        Assert.Equal(1, retired);
    }

    [Fact]
    public void RetainedActorRetirementCannotRemoveAReplacementWithinTheSameScene()
    {
        using var roster = Create(_ => { });
        var old = new Contact(ObpSourceGame.Rac1, 7);
        roster.Add(7, old);
        Assert.True(roster.Remove(old, out _));
        var replacement = new Contact(ObpSourceGame.Rac1, 7);
        roster.Add(7, replacement);
        Assert.False(roster.Remove(old, out _));
        Assert.True(roster.Contains(replacement));
    }

    [Fact]
    public void RemovedInstanceRemainsKnownUntilItsSceneLeaseRetires()
    {
        using var roster = Create(_ => { });
        var first = new Contact(ObpSourceGame.Rac1, 7);
        Assert.False(roster.PreviouslyOwned(7));
        roster.Add(7, first);
        Assert.True(roster.Remove(first, out _));
        Assert.True(roster.PreviouslyOwned(7));
        Assert.False(roster.ContainsKey(7));
        var replacement = new Contact(ObpSourceGame.Rac1, 7);
        roster.Add(7, replacement);
        Assert.False(roster.Contains(first));
        Assert.True(roster.Contains(replacement));
    }

    [Fact]
    public void RemovingAnAuthoredActorDoesNotRetireOtherContacts()
    {
        int retired = 0;
        using var roster = Create(_ => retired++);
        var first = new Contact(ObpSourceGame.Rac1, 7);
        var next = new Contact(ObpSourceGame.Rac1, 8);
        roster.Add(7, first); roster.Add(8, next);
        Assert.True(roster.Remove(7, out var removed));
        Assert.Same(first, removed);
        Assert.False(roster.Contains(first));
        Assert.True(roster.Contains(next));
        Assert.Equal(0, retired); // The actor-scene lease owns the removed actor's bodies.
    }
}
