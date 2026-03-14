using Microsoft.Playwright;

namespace ZKMapper.Services;

internal sealed class ScrollExhaustionService
{
    private readonly HumanDelayService _humanDelayService;

    public ScrollExhaustionService(HumanDelayService humanDelayService)
    {
        _humanDelayService = humanDelayService;
    }

    public async Task ScrollToEndAsync(IPage page, CancellationToken cancellationToken = default)
    {
        double previousHeight = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var height = await page.EvaluateAsync<double>("() => document.body.scrollHeight");
            if (Math.Abs(height - previousHeight) < 1)
            {
                break;
            }

            await page.EvaluateAsync("() => window.scrollTo(0, document.body.scrollHeight)");
            await _humanDelayService.DelayAsync(2, 4, cancellationToken);
            previousHeight = height;
        }
    }
}
