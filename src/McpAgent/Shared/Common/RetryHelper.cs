using Microsoft.Extensions.Logging;

namespace McpAgent.Shared.Common;

public static class RetryHelper
{
    public static async Task<T> RetryAsync<T>(
        Func<Task<T>> operation,
        int maxAttempts = 3,
        TimeSpan? delay = null,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        delay ??= TimeSpan.FromSeconds(1);
        var attempts = 0;
        Exception? lastException = null;

        while (attempts < maxAttempts)
        {
            attempts++;
            try
            {
                return await operation();
            }
            catch (Exception ex) when (attempts < maxAttempts)
            {
                lastException = ex;
                logger?.LogWarning(ex, "Operation failed (attempt {Attempt}/{MaxAttempts}), retrying in {Delay}ms", 
                    attempts, maxAttempts, delay.Value.TotalMilliseconds);
                
                await Task.Delay(delay.Value, cancellationToken);
                delay = TimeSpan.FromMilliseconds(delay.Value.TotalMilliseconds * 1.5); // Exponential backoff
            }
        }

        throw lastException ?? new InvalidOperationException("Retry operation failed without exception");
    }

    public static async Task RetryAsync(
        Func<Task> operation,
        int maxAttempts = 3,
        TimeSpan? delay = null,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        await RetryAsync(async () =>
        {
            await operation();
            return true;
        }, maxAttempts, delay, logger, cancellationToken);
    }
}