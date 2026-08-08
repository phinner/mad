using Discord;
using Mad.Discord;

namespace Mad.Delete;

/// <summary>Deletes the messages in a channel that a set of sweep options selects.</summary>
public static class MessageDeletionService
{
    /// <summary>Discord refuses to bulk delete anything older than this.</summary>
    public static readonly TimeSpan BulkDeletionWindow = TimeSpan.FromDays(14);

    public static readonly TimeSpan MinimumOlderThan = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan MaximumOlderThan = TimeSpan.FromDays(12);

    private const string AuditLogReason = "Mad automated message deletion";

    /// <summary>
    /// Whole minutes between <see cref="MinimumOlderThan"/> and <see cref="MaximumOlderThan"/>;
    /// the upper bound leaves room under <see cref="BulkDeletionWindow"/> for a sweep to catch up.
    /// </summary>
    public static bool IsValidOlderThan(TimeSpan olderThan) =>
        olderThan >= MinimumOlderThan
        && olderThan <= MaximumOlderThan
        && olderThan.Ticks % TimeSpan.TicksPerMinute == 0;

    public static async Task<(int Scanned, int Deleted)> SweepAsync(
        ITextChannel channel,
        TimeSpan olderThan,
        DiscordUserType? target,
        bool includePins,
        CancellationToken cancellationToken = default
    )
    {
        var now = DateTimeOffset.UtcNow;
        var cutoff = now - olderThan;
        var bulkDeletionThreshold = now - BulkDeletionWindow;

        // Start at the newest message that is already old enough and walk backwards; anything
        // newer than the cutoff cannot match.
        var messageChunks = channel.GetMessagesAsync(
            SnowflakeUtils.ToSnowflake(cutoff),
            Direction.Before,
            int.MaxValue,
            CacheMode.AllowDownload,
            new RequestOptions { CancelToken = cancellationToken }
        );

        return await ProcessAsync(
            messageChunks,
            channel,
            message =>
            {
                if (message.CreatedAt <= bulkDeletionThreshold)
                {
                    return EvaluatorResult.Stop;
                }

                if (
                    (message.IsPinned && !includePins)
                    || (target is { } userType && GetUserType(message.Author) != userType)
                )
                {
                    return EvaluatorResult.Skip;
                }

                return message.CreatedAt <= cutoff ? EvaluatorResult.Take : EvaluatorResult.Skip;
            },
            cancellationToken
        );
    }

    private static async Task<(int Scanned, int Deleted)> ProcessAsync(
        IAsyncEnumerable<IReadOnlyCollection<IMessage>> chunks,
        ITextChannel channel,
        Func<IMessage, EvaluatorResult> evaluator,
        CancellationToken cancellationToken
    )
    {
        var scanned = 0;
        var deleted = 0;

        await foreach (var chunk in chunks.WithCancellation(cancellationToken))
        {
            var deleting = new List<IMessage>();
            var stop = false;

            foreach (var message in chunk)
            {
                var result = evaluator(message);
                if (result is EvaluatorResult.Stop)
                {
                    stop = true;
                    break;
                }

                scanned++;
                switch (result)
                {
                    case EvaluatorResult.Skip:
                        break;
                    case EvaluatorResult.Take:
                        deleting.Add(message);
                        break;
                    default:
                        throw new InvalidOperationException($"Unknown filter result {result}.");
                }
            }

            if (deleting.Count > 0)
            {
                await BulkDeleteAsync(channel, deleting, cancellationToken);
                deleted += deleting.Count;
            }

            if (stop)
            {
                break;
            }
        }

        return (scanned, deleted);
    }

    private static Task BulkDeleteAsync(
        ITextChannel channel,
        List<IMessage> messages,
        CancellationToken cancellationToken
    )
    {
        var requestOptions = CreateRequestOptions(cancellationToken);
        return messages.Count == 1
            ? channel.DeleteMessageAsync(messages[0], requestOptions)
            : channel.DeleteMessagesAsync(messages, requestOptions);
    }

    private enum EvaluatorResult
    {
        Take,
        Skip,
        Stop,
    }

    private static DiscordUserType GetUserType(IUser author) =>
        author.IsWebhook ? DiscordUserType.Webhook
        : author.IsBot ? DiscordUserType.Bot
        : DiscordUserType.User;

    private static RequestOptions CreateRequestOptions(CancellationToken cancellationToken) =>
        new() { AuditLogReason = AuditLogReason, CancelToken = cancellationToken };
}
