using Microsoft.Playwright;
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
                ShouldHandle = new PredicateBuilder().Handle<PlaywrightException>().Handle<TimeoutException>(),
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
        return ExecuteInternalAsync(operation, cancellationToken);
    }

    public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        return ExecuteInternalAsync(operation, cancellationToken);
    }

    private async Task ExecuteInternalAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken)
    {
        var context = ResilienceContextPool.Shared.Get(cancellationToken);
        try
        {
            await _pipeline.ExecuteAsync(
                static async (resilienceContext, state) =>
                {
                    await state.operation(resilienceContext.CancellationToken);
                },
                context,
                (operation: operation));
        }
        finally
        {
            ResilienceContextPool.Shared.Return(context);
        }
    }

    private async Task<T> ExecuteInternalAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        var context = ResilienceContextPool.Shared.Get(cancellationToken);
        try
        {
            return await _pipeline.ExecuteAsync(
                static async (resilienceContext, state) =>
                {
                    return await state.operation(resilienceContext.CancellationToken);
                },
                context,
                (operation: operation));
        }
        finally
        {
            ResilienceContextPool.Shared.Return(context);
        }
    }
}
