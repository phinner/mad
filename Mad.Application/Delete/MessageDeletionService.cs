using Mad.Discord;
using NetCord;
using NetCord.Rest;

namespace Mad.Delete;

public static class MessageDeletionService
{
    /// <summary>Discord refuses to bulk delete anything older than this.</summary>
    private static readonly TimeSpan BulkDeletionWindow = TimeSpan.FromDays(14);

    private static readonly TimeSpan MinimumOlderThan = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan MaximumOlderThan = TimeSpan.FromDays(12);

    private const string AuditLogReason = "Mad automated message deletion";
    private const int DeleteBatchSize = 100;

    public static bool IsValidOlderThan(TimeSpan olderThan) =>
        olderThan >= MinimumOlderThan
        && olderThan <= MaximumOlderThan
        && olderThan.Ticks % TimeSpan.TicksPerMinute == 0;

    public static async Task<(int Scanned, int Deleted)> SweepAsync(
        RestClient rest,
        ulong channelId,
        TimeSpan olderThan,
        DiscordUserType? target,
        bool includePins,
        CancellationToken cancellationToken = default
    )
    {
        var now = DateTimeOffset.UtcNow;
        var cutoff = now - olderThan;
        var bulkDeletionThreshold = now - BulkDeletionWindow;

        var messages = rest.GetMessagesAsync(
            channelId,
            new PaginationProperties<ulong> { From = Snowflake.Create(cutoff), Direction = PaginationDirection.Before }
        );

        var scanned = 0;
        var deleted = 0;
        var deleting = new List<ulong>(DeleteBatchSize);

        await foreach (var message in messages.WithCancellation(cancellationToken))
        {
            var result = Evaluate(message);
            if (result is EvaluatorResult.Stop)
            {
                break;
            }

            scanned++;
            if (result is not EvaluatorResult.Take)
            {
                continue;
            }

            deleting.Add(message.Id);
            if (deleting.Count < DeleteBatchSize)
            {
                continue;
            }

            await DeleteAsync(deleting);
        }

        await DeleteAsync(deleting);
        return (scanned, deleted);

        EvaluatorResult Evaluate(RestMessage message)
        {
            if (message.CreatedAt <= bulkDeletionThreshold)
            {
                return EvaluatorResult.Stop;
            }

            if ((message.IsPinned && !includePins) || (target is { } userType && GetUserType(message) != userType))
            {
                return EvaluatorResult.Skip;
            }

            return message.CreatedAt <= cutoff ? EvaluatorResult.Take : EvaluatorResult.Skip;
        }

        async Task DeleteAsync(List<ulong> ids)
        {
            if (ids.Count == 0)
            {
                return;
            }

            await rest.DeleteMessagesAsync(
                channelId,
                ids,
                new RestRequestProperties { AuditLogReason = AuditLogReason },
                cancellationToken
            );
            deleted += ids.Count;
            ids.Clear();
        }
    }

    private enum EvaluatorResult
    {
        Take,
        Skip,
        Stop,
    }

    private static DiscordUserType GetUserType(RestMessage message) =>
        message.WebhookId.HasValue ? DiscordUserType.Webhook
        : message.Author.IsBot ? DiscordUserType.Bot
        : DiscordUserType.User;
}
