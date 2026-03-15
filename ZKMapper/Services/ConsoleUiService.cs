using Spectre.Console;
using Spectre.Console.Rendering;
using ZKMapper.Models;

namespace ZKMapper.Services;

internal sealed class ConsoleUiService
{
    public void ResetScreen()
    {
        AnsiConsole.Clear();
        ShowStartupBanner();
    }

    public void ShowStartupBanner()
    {
        AnsiConsole.MarkupLine("[deepskyblue1]+-------------------------------+[/]");
        AnsiConsole.MarkupLine("[deepskyblue1]|[/]           [deepskyblue1]ZKMapper[/]           [deepskyblue1]|[/]");
        AnsiConsole.MarkupLine("[deepskyblue1]|[/] [grey]  LinkedIn Company Mapper   [/][deepskyblue1]|[/]");
        AnsiConsole.MarkupLine("[deepskyblue1]+-------------------------------+[/]");
        AnsiConsole.WriteLine();
    }

    public void ShowDiscoveredProfiles(IReadOnlyList<ContactDiscoveryTarget> targets)
    {
        if (targets.Count == 0)
        {
            return;
        }

        var table = new Table()
            .Border(TableBorder.Simple)
            .Title("[bold]DISCOVERED PROFILES[/]");

        table.AddColumn("Name");
        table.AddColumn("URL");

        foreach (var target in targets)
        {
            table.AddRow(
                Escape(target.DisplayName),
                Escape(ToDisplayUrl(target.Href)));
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
    }

    public async Task RunMappingProgressAsync(
        string companyName,
        IReadOnlyList<ContactDiscoveryTarget> targets,
        Func<ContactDiscoveryTarget, int, int, Task> processTargetAsync)
    {
        await AnsiConsole.Progress()
            .AutoClear(false)
            .HideCompleted(false)
            .Columns(new ProgressColumn[]
            {
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new CountProgressColumn(),
                new SpinnerColumn()
            })
            .StartAsync(async context =>
            {
                var task = context.AddTask($"[bold]Mapping {Escape(companyName)}[/]", maxValue: Math.Max(1, targets.Count));

                for (var index = 0; index < targets.Count; index++)
                {
                    await processTargetAsync(targets[index], index + 1, targets.Count);
                    task.Increment(1);
                }
            });

        AnsiConsole.WriteLine();
    }

    public void ShowExtractedProfile(ExtractedProfile profile)
    {
        AnsiConsole.MarkupLine($"[green]OK[/] {Escape(profile.FullName)}");
        AnsiConsole.MarkupLine($"  Headline: {Escape(NullSafe(profile.Headline))}");
        AnsiConsole.MarkupLine($"  Role: {Escape(NullSafe(profile.CurrentJobTitles))}");
        AnsiConsole.WriteLine();
    }

    public void ShowExtractionError(string message)
    {
        AnsiConsole.MarkupLine($"[red]{Escape(message)}[/]");
        AnsiConsole.WriteLine();
    }

    private static string ToDisplayUrl(string? href)
    {
        if (string.IsNullOrWhiteSpace(href))
        {
            return string.Empty;
        }

        return Uri.TryCreate(href, UriKind.Absolute, out var uri)
            ? uri.AbsolutePath.TrimEnd('/')
            : href;
    }

    private static string NullSafe(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value;
    }

    private static string Escape(string? value)
    {
        return Markup.Escape(value ?? string.Empty);
    }

    private sealed class CountProgressColumn : ProgressColumn
    {
        public override IRenderable Render(RenderOptions options, ProgressTask task, TimeSpan deltaTime)
        {
            var total = task.MaxValue <= 0 ? 0 : (int)task.MaxValue;
            var current = total == 0 ? 0 : Math.Min(total, (int)task.Value);
            return new Markup($"{current} / {total} profiles");
        }
    }
}
