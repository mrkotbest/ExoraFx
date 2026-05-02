using ExoraFx.Api.Models;

namespace ExoraFx.Api.Services.Storage;

public interface IConversionHistoryStore
{
    long Append(long userId, string? userName, string userRole, ConvertResult result);

    IReadOnlyList<HistoryEntry> GetPage(long userId, int offset, int take);

    int Count(long userId);

    string? ToggleState(long userId, long entryId);

    HistoryEntry? GetById(long userId, long entryId);

    HistoryStats GetStats(long userId, DateTime? sinceUtc = null, DateTime? untilUtc = null);

    void Clear(long userId);
}
