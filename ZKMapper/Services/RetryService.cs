using Polly;
using Polly.Retry;
using Serilog;

namespace ZKMapper.Services;

internal sealed class RetryService
{
    private readonly ResiliencePipeline _pipeline;

    public RetryService()
    {
        _pipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromSeconds(2),
                BackoffType = DelayBackoffType.Linear,
                UseJitter = true,
                OnRetry = args =>
                {
                    Log.Warning(
                        "Retry attempt {Attempt} after transient failure: {Message}",
                        args.AttemptNumber + 1,
                        args.Outcome.Exception?.Message ?? "Unknown failure");
                    return default;
                }
            })
            .Build();
    }

    public Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken)
    {
        return _pipeline.ExecuteAsync(operation, cancellationToken);
    }

    public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        return _pipeline.ExecuteAsync(operation, cancellationToken);
    }
}
