using System.Collections.Concurrent;

namespace ExoraFx.Api.Services.Bot;

public sealed class PromptStateStore
{
    private readonly TimeSpan _ttl;
    private readonly ConcurrentDictionary<int, Entry> _entries = new();

    public PromptStateStore() : this(TimeSpan.FromMinutes(10)) { }

    public PromptStateStore(TimeSpan ttl)
    {
        _ttl = ttl;
    }

    public void Remember(int messageId, long userId, string field)
    {
        Sweep();
        _entries[messageId] = new Entry(userId, field, DateTime.UtcNow);
    }

    public bool TryConsume(int messageId, long userId, out string field)
    {
        if (_entries.TryRemove(messageId, out var entry)
            && entry.UserId == userId
            && DateTime.UtcNow - entry.CreatedAtUtc <= _ttl)
        {
            field = entry.Field;
            return true;
        }

        field = string.Empty;
        return false;
    }

    public bool TryConsumeForUser(long userId, out string field)
    {
        var cutoff = DateTime.UtcNow - _ttl;
        var bestMessageId = 0;
        var bestCreatedAt = DateTime.MinValue;
        var bestField = string.Empty;

        foreach (var (messageId, entry) in _entries)
        {
            if (entry.UserId != userId || entry.CreatedAtUtc < cutoff)
                continue;
            if (entry.CreatedAtUtc > bestCreatedAt)
            {
                bestCreatedAt = entry.CreatedAtUtc;
                bestMessageId = messageId;
                bestField = entry.Field;
            }
        }

        if (bestMessageId == 0)
        {
            field = string.Empty;
            return false;
        }

        _entries.TryRemove(bestMessageId, out _);
        field = bestField;
        return true;
    }

    private void Sweep()
    {
        var cutoff = DateTime.UtcNow - _ttl;
        foreach (var (messageId, entry) in _entries)
        {
            if (entry.CreatedAtUtc < cutoff)
                _entries.TryRemove(messageId, out _);
        }
    }

    private sealed record Entry(long UserId, string Field, DateTime CreatedAtUtc);
}
