
namespace ExoraFx.Api.Tests.Services;

public class PromptStateStoreTests
{
    [Fact]
    public void TryConsume_AfterRemember_ReturnsField()
    {
        var store = new PromptStateStore();
        store.Remember(messageId: 100, userId: 42L, field: "margin");

        Assert.True(store.TryConsume(100, 42L, out var field));
        Assert.Equal("margin", field);
    }

    [Fact]
    public void TryConsume_RemovesEntry_SecondCallReturnsFalse()
    {
        var store = new PromptStateStore();
        store.Remember(100, 42L, "amount");

        Assert.True(store.TryConsume(100, 42L, out _));
        Assert.False(store.TryConsume(100, 42L, out _));
    }

    [Fact]
    public void TryConsume_DifferentUser_ReturnsFalse()
    {
        var store = new PromptStateStore();
        store.Remember(100, 42L, "margin");

        Assert.False(store.TryConsume(100, 99L, out _));
    }

    [Fact]
    public void TryConsume_UnknownMessage_ReturnsFalse()
    {
        var store = new PromptStateStore();

        Assert.False(store.TryConsume(999, 1L, out _));
    }

    [Fact]
    public void Remember_OverwritesPrevious()
    {
        var store = new PromptStateStore();
        store.Remember(100, 42L, "margin");
        store.Remember(100, 42L, "amount");

        Assert.True(store.TryConsume(100, 42L, out var field));
        Assert.Equal("amount", field);
    }

    [Fact]
    public void Remember_SweepsExpiredEntries()
    {
        var store = new PromptStateStore(TimeSpan.FromMilliseconds(50));
        store.Remember(100, 42L, "margin");
        Thread.Sleep(120);

        store.Remember(200, 42L, "amount");

        Assert.False(store.TryConsume(100, 42L, out _));
        Assert.True(store.TryConsume(200, 42L, out var field));
        Assert.Equal("amount", field);
    }

    [Fact]
    public void TryConsume_ExpiredEntry_ReturnsFalse()
    {
        var store = new PromptStateStore(TimeSpan.FromMilliseconds(20));
        store.Remember(100, 42L, "margin");
        Thread.Sleep(60);

        Assert.False(store.TryConsume(100, 42L, out _));
    }

    [Fact]
    public void TryConsumeForUser_PicksNewestEntry_AndRemovesIt()
    {
        var store = new PromptStateStore();
        store.Remember(100, 42L, "margin");
        Thread.Sleep(5);
        store.Remember(101, 42L, "amount");

        Assert.True(store.TryConsumeForUser(42L, out var field));
        Assert.Equal("amount", field);

        Assert.True(store.TryConsume(100, 42L, out var older));
        Assert.Equal("margin", older);
    }

    [Fact]
    public void TryConsumeForUser_NoEntries_ReturnsFalse()
    {
        var store = new PromptStateStore();
        Assert.False(store.TryConsumeForUser(42L, out _));
    }

    [Fact]
    public void TryConsumeForUser_OtherUserOnly_ReturnsFalse()
    {
        var store = new PromptStateStore();
        store.Remember(100, 42L, "margin");
        Assert.False(store.TryConsumeForUser(99L, out _));
    }

    [Fact]
    public void TryConsumeForUser_ExpiredEntry_Skipped()
    {
        var store = new PromptStateStore(TimeSpan.FromMilliseconds(20));
        store.Remember(100, 42L, "margin");
        Thread.Sleep(60);
        Assert.False(store.TryConsumeForUser(42L, out _));
    }

    [Fact]
    public void Concurrent_RememberAndConsume_NoDuplicateConsumption()
    {
        var store = new PromptStateStore();
        for (var i = 1; i <= 100; i++)
            store.Remember(i, userId: 1L, field: "amount");

        var consumed = new System.Collections.Concurrent.ConcurrentBag<int>();
        Parallel.For(1, 101, i =>
        {
            if (store.TryConsume(i, 1L, out _))
                consumed.Add(i);
        });

        Assert.Equal(100, consumed.Count);
        Assert.Equal(100, consumed.Distinct().Count());
    }

    [Fact]
    public void Concurrent_TwoConsumersForSameMessage_OnlyOneWins()
    {
        var store = new PromptStateStore();
        store.Remember(messageId: 7, userId: 5L, field: "margin");

        var winners = 0;
        Parallel.For(0, 64, i =>
        {
            if (store.TryConsume(7, 5L, out var _field))
                Interlocked.Increment(ref winners);
        });

        Assert.Equal(1, winners);
    }

    [Fact]
    public void TryConsumeForUser_TwoEntries_ConsumesNewestFirstThenOlder()
    {
        var store = new PromptStateStore();
        store.Remember(1, 42L, "a");
        Thread.Sleep(5);
        store.Remember(2, 42L, "b");

        Assert.True(store.TryConsumeForUser(42L, out var newest));
        Assert.Equal("b", newest);
        Assert.True(store.TryConsumeForUser(42L, out var older));
        Assert.Equal("a", older);
        Assert.False(store.TryConsumeForUser(42L, out _));
    }

    [Fact]
    public void TryConsume_ZeroMessageId_BehavesLikeAnyOther()
    {
        var store = new PromptStateStore();
        store.Remember(0, 1L, "amount");
        Assert.True(store.TryConsume(0, 1L, out var f));
        Assert.Equal("amount", f);
    }

    [Fact]
    public void TryConsumeForUser_OnlyExpiredEntries_FallsThrough()
    {
        var store = new PromptStateStore(TimeSpan.FromMilliseconds(30));
        store.Remember(10, 99L, "margin");
        store.Remember(11, 99L, "amount");
        Thread.Sleep(80);
        Assert.False(store.TryConsumeForUser(99L, out _));
    }
}
