namespace Nodus.Shared.Models;

/// <summary>
/// Statistics for sync status across all votes and media
/// </summary>
public record SyncStatistics(
    int TotalVotes,
    int PendingVotes,
    int SyncedVotes,
    int PendingMedia,
    double SyncPercentage
)
{
    public static SyncStatistics Empty => new(0, 0, 0, 0, 0);
    
    public static SyncStatistics Calculate(int total, int pending, int pendingMedia)
    {
        var synced = total - pending;
        var percentage = total > 0 ? (double)synced / total * 100 : 0;
        return new(total, pending, synced, pendingMedia, Math.Round(percentage, 2));
    }
}
