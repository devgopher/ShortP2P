using Polly;
using Polly.Retry;
using ShortP2P.Client.Routing;
using ShortP2P.Discovery;

namespace ShortP2P.Client.Services;

/// <summary>
///     Политика гарантированной доставки: повторные попытки отправки с настраиваемой паузой
///     и пользовательским шагом failover между ретраями.
/// </summary>
public sealed class GuaranteedDeliveryPolicy
{
    public async Task ExecuteAsync(
        Func<CancellationToken, Task> sendAttemptAsync,
        Func<CancellationToken, Task>? onRetryAsync,
        bool enabled,
        P2pRoutingSettings? settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sendAttemptAsync);

        var retryDelay = enabled && settings != null ? settings.SendFailureRetryDelay : TimeSpan.Zero;

        RetryStrategyOptions retryOptions = new()
        {
            MaxRetryAttempts = 3,
            Delay = retryDelay,
            BackoffType = DelayBackoffType.Constant,
            ShouldHandle = new PredicateBuilder().Handle<Exception>(ex => ex is not OperationCanceledException),
            OnRetry = async args =>
            {
                if (!enabled || onRetryAsync == null)
                    return;

                await onRetryAsync(args.Context.CancellationToken).ConfigureAwait(false);
            }
        };

        var pipeline = new ResiliencePipelineBuilder().AddRetry(retryOptions).Build();
        await pipeline.ExecuteAsync(async ct => { await sendAttemptAsync(ct).ConfigureAwait(false); },
                cancellationToken)
            .ConfigureAwait(false);
    }
}