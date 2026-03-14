using Microsoft.Playwright;
using ZKMapper.Infrastructure;
using ZKMapper.Models;

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
        using var timer = ExecutionTimer.Start("ScrollExhaustion");
        double previousHeight = 0;
        var iteration = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            iteration++;

            var height = await page.EvaluateAsync<double>("() => document.body.scrollHeight");
            AppLog.Data(
                $"scroll iteration={iteration};previousHeight={previousHeight};newHeight={height}",
                "ScrollExhaustion",
                "measure-scroll-height",
                $"iteration={iteration};previousHeight={previousHeight};newHeight={height}");
            if (Math.Abs(height - previousHeight) < 1)
            {
                AppLog.Result("results exhausted", "ScrollExhaustion", "scroll-to-end", $"iteration={iteration};finalHeight={height}");
                break;
            }

            AppLog.Action("scrolling to page bottom", "ScrollExhaustion", "scroll-to-end", $"iteration={iteration};scrollDistance={height - previousHeight}");
            await page.EvaluateAsync("() => window.scrollTo(0, document.body.scrollHeight)");
            await _humanDelayService.DelayAsync(DelayProfile.Navigation, "waiting for LinkedIn lazy-loaded results after scroll", cancellationToken);
            previousHeight = height;
        }
    }
}
