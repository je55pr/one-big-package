using OBP.RAC3.Gameplay;

namespace OBP.Tests;

public sealed class UyaIndexedResourceRuntimeTests
{
    [Fact]
    public void Class500PrefersUnderCapacityResourcesAndCanSpawnTwo3291Objects()
    {
        var slots = Slots(
            Slot(2, available: true, current: 4, amount: 3, capacity: 10),
            Slot(3, available: true, current: 10, amount: 7, capacity: 10));

        var plan = UyaClass500ResourceDropPlanner.Plan(
            slots,
            resourceSelectionRoll: 0,
            pickupCountRoll: 0);

        Assert.True(plan.HasDrop);
        Assert.True(plan.UsedUnderCapacityPool);
        Assert.Equal(2, plan.SelectedResourceIndex);
        Assert.Equal(2, plan.Pickups.Count);
        Assert.All(plan.Pickups, pickup =>
        {
            Assert.Equal(2, pickup.ResourceIndex);
            Assert.Equal(3, pickup.Amount);
            Assert.Equal(3291, UyaClass3291PickupPlan.NativeClassId);
        });
    }
    [Fact]
    public void Class500FallsBackToAllEligibleResourcesWhenEveryCandidateIsFull()
    {
        var slots = Slots(
            Slot(2, available: true, current: 10, amount: 3, capacity: 10),
            Slot(3, available: true, current: 20, amount: 5, capacity: 20));

        var plan = UyaClass500ResourceDropPlanner.Plan(
            slots,
            resourceSelectionRoll: 1,
            pickupCountRoll: 4);

        Assert.True(plan.HasDrop);
        Assert.False(plan.UsedUnderCapacityPool);
        Assert.Equal(3, plan.SelectedResourceIndex);
        var pickup = Assert.Single(plan.Pickups);
        Assert.Equal(5, pickup.Amount);
    }

    [Fact]
    public void ExcludedUnavailableAndZeroCapacitySlotsYieldNoClass500Drop()
    {
        var slots = Slots(
            Slot(0x16, available: true, current: 0, amount: 9, capacity: 10),
            Slot(7, available: false, current: 0, amount: 9, capacity: 10),
            Slot(8, available: true, current: 0, amount: 9, capacity: 0));

        var plan = UyaClass500ResourceDropPlanner.Plan(
            slots,
            resourceSelectionRoll: 0,
            pickupCountRoll: 0);

        Assert.False(plan.HasDrop);
        Assert.Null(plan.SelectedResourceIndex);
        Assert.Empty(plan.Pickups);
    }
    [Fact]
    public void NativeRandomDomainsFailClosed()
    {
        var slots = Slots(
            Slot(2, available: true, current: 0, amount: 3, capacity: 10));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            UyaClass500ResourceDropPlanner.Plan(slots, 1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            UyaClass500ResourceDropPlanner.Plan(slots, 0, 5));
    }

    [Fact]
    public void Class3291CollectionUsesIndexedAddAndCapacityClampThenConsumesPickup()
    {
        var slots = Slots(
            Slot(2, available: true, current: 8, amount: 4, capacity: 10));
        var plan = UyaClass500ResourceDropPlanner.Plan(
            slots,
            resourceSelectionRoll: 0,
            pickupCountRoll: 1);
        var session = new UyaIndexedResourceSession(slots);
        var pickup = Assert.Single(session.Spawn(plan));

        UyaIndexedResourceCollection result = session.Collect(pickup.PickupId);

        Assert.Equal(8, result.CurrentBefore);
        Assert.Equal(10, result.CurrentAfter);
        Assert.Equal(2, result.Added);
        Assert.True(result.Pickup.Collected);
        Assert.Equal(10, session.Current(2));
        Assert.Throws<InvalidOperationException>(() =>
            session.Collect(pickup.PickupId));
    }
    [Fact]
    public void GenericClass3291AddServiceDoesNotClampWhenCapacityIsZero()
    {
        var slots = Slots(
            Slot(4, available: false, current: 3, amount: 5, capacity: 0));
        var session = new UyaIndexedResourceSession(slots);
        var plan = new UyaClass500ResourceDropPlan(
            4,
            false,
            [new UyaClass3291PickupPlan(4, 5)]);
        var pickup = Assert.Single(session.Spawn(plan));

        var result = session.Collect(pickup.PickupId);

        Assert.Equal(8, result.CurrentAfter);
        Assert.Equal(5, result.Added);
    }

    private static UyaIndexedResourceSlot[] Slots(
        params UyaIndexedResourceSlot[] overrides)
    {
        var slots = Enumerable.Range(
                0,
                UyaClass500ResourceDropPlanner.ResourceSlotCount)
            .Select(index => new UyaIndexedResourceSlot(
                index,
                Available: false,
                ItemDefinitionIndex: index,
                Current: 0,
                PickupAmount: 0,
                Capacity: 0))
            .ToArray();

        foreach (var item in overrides)
            slots[item.ResourceIndex] = item;
        return slots;
    }
    private static UyaIndexedResourceSlot Slot(
        int index,
        bool available,
        int current,
        int amount,
        int capacity) =>
        new(
            index,
            available,
            ItemDefinitionIndex: index,
            current,
            amount,
            capacity);
}
