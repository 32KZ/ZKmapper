using Serilog.Context;
using System.Threading;
using ZKMapper.Infrastructure;
using ZKMapper.Models;
using ZKMapper.Utils;

namespace ZKMapper.Services;

internal sealed class MapperApplication
{
    private readonly ConsolePromptService _promptService;
    private readonly ConsoleUiService _consoleUiService;
    private readonly SessionStateManager _sessionStateManager;
    private readonly BrowserManager _browserManager;
    private readonly LinkedInRegionMapper _regionMapper;
    private readonly LinkedInPeopleSearchUrlBuilder _peopleSearchUrlBuilder;
    private readonly LinkedInQueryService _queryService;
    private readonly ProfileExtractionService _profileExtractionService;
    private readonly EmailGenerationService _emailGenerationService;
    private readonly HumanDelayService _humanDelayService;
    private readonly RunStatistics _statistics;

    public MapperApplication(
        ConsolePromptService promptService,
        ConsoleUiService consoleUiService,
        SessionStateManager sessionStateManager,
        BrowserManager browserManager,
        LinkedInRegionMapper regionMapper,
        LinkedInPeopleSearchUrlBuilder peopleSearchUrlBuilder,
        LinkedInQueryService queryService,
        ProfileExtractionService profileExtractionService,
        EmailGenerationService emailGenerationService,
        HumanDelayService humanDelayService,
        RunStatistics statistics)
    {
        _promptService = promptService;
        _consoleUiService = consoleUiService;
        _sessionStateManager = sessionStateManager;
        _browserManager = browserManager;
        _regionMapper = regionMapper;
        _peopleSearchUrlBuilder = peopleSearchUrlBuilder;
        _queryService = queryService;
        _profileExtractionService = profileExtractionService;
        _emailGenerationService = emailGenerationService;
        _humanDelayService = humanDelayService;
        _statistics = statistics;
    }

    public async Task<int> RunAuthSetupAsync()
    {
        using var timer = ExecutionTimer.Start("RunAuthSetup");
        AppLog.Step("starting auth setup mode", "AuthSetup", "run-auth");
        await _sessionStateManager.CaptureSessionStateAsync(_browserManager, _promptService, CancellationToken.None);
        Console.WriteLine("Session saved.");
        Console.WriteLine("Press ENTER to exit.");
        Console.ReadLine();
        AppLog.Result("auth setup complete", "AuthSetup", "run-auth", $"sessionPath={AppPaths.SessionStatePath}");
        return 0;
    }

    public async Task<int> RunCollectionAsync(MappingQueue queue)
    {
        using var timer = ExecutionTimer.Start("RunCollection");
        _sessionStateManager.EnsureSessionStateExists();
        if (queue.Count == 0)
        {
            AppLog.Warn("mapping queue was empty", "RunCollection", "initialize-run");
            return 0;
        }

        var runMetadata = CreateRunMetadata();
        var abortRequested = 0;
        void RequestAbort(string source)
        {
            if (Interlocked.Exchange(ref abortRequested, 1) == 1)
            {
                return;
            }

            AppLog.Warn("mapping abort requested by user", "RunCollection", "abort-requested", $"source={source}");
        }

        using var abortMonitorCts = new CancellationTokenSource();
        var abortMonitor = MonitorAbortKeyAsync(
            () => RequestAbort("keyboard"),
            abortMonitorCts.Token);
        using var abortWindow = AbortWindowService.Start(() => RequestAbort("abort-window"));

        AppLog.Step("starting collection run", "RunCollection", "initialize-run", $"runNumber={runMetadata.RunNumber};startedUtc={runMetadata.StartedUtc:O};queueCount={queue.Count}");
        await using var session = await _browserManager.LaunchAsync(useSavedSession: true, CancellationToken.None);

        foreach (var input in queue.Companies)
        {
            if (Volatile.Read(ref abortRequested) == 1)
            {
                break;
            }

            using var csvWriter = new CsvWriterService(input, runMetadata);
            AppLog.Data($"csvOutputPath={csvWriter.OutputPath}", "RunCollection", "initialize-csv-writer", $"outputPath={csvWriter.OutputPath}");
            try
            {
                await ProcessCompanyAsync(session, input, csvWriter, CancellationToken.None, () => Volatile.Read(ref abortRequested) == 1);
                AppLog.Result("company mapping completed", "RunCollection", "process-company", $"companyName={input.CompanyName};outputPath={csvWriter.OutputPath}");
            }
            catch (Exception ex)
            {
                AppLog.Error(ex, "company mapping failed and will be skipped", "RunCollection", "process-company", $"companyName={input.CompanyName}");
            }
        }

        abortMonitorCts.Cancel();
        await abortMonitor;

        if (Volatile.Read(ref abortRequested) == 1)
        {
            _consoleUiService.ShowExtractionError("Mapping aborted by user. CSV output has been saved. Returning to main menu.");
        }

        AppLog.Summary(
            "run complete",
            $"profilesScanned={_statistics.ProfilesScanned};recordsWritten={_statistics.RecordsWritten};queriesExecuted={_statistics.QueriesExecuted}");

        return 0;
    }

    private async Task ProcessCompanyAsync(
        PlaywrightSession session,
        CompanyInput input,
        CsvWriterService csvWriter,
        CancellationToken cancellationToken,
        Func<bool> shouldAbort)
    {
        using var timer = ExecutionTimer.Start("ProcessCompany");

        using (LogContext.PushProperty("Company", input.CompanyName))
        {
            var slug = LinkedInUrlParser.ExtractCompanySlug(input.CompanyLinkedInUrl);
            var regionId = _regionMapper.ResolveRegionId(input.SearchCountry);

            for (var titleIndex = 0; titleIndex < input.TitleFilters.Count; titleIndex++)
            {
                if (shouldAbort())
                {
                    return;
                }

                var keyword = input.TitleFilters[titleIndex].Trim();
                var searchUrl = _peopleSearchUrlBuilder.BuildPeopleSearchUrl(slug, keyword, regionId);

                using (LogContext.PushProperty("Query", keyword))
                {
                    AppLog.Data(
                        $"queryIndex={titleIndex + 1};keyword={keyword};searchUrl={searchUrl}",
                        "ProcessCompany",
                        "prepare-query",
                        $"queryIndex={titleIndex + 1};keyword={keyword};searchUrl={searchUrl};slug={slug};regionId={regionId}");

                    try
                    {
                        AppLog.Next("navigate to filtered LinkedIn people page", "ProcessCompany", "submit-query", $"queryIndex={titleIndex + 1};keyword={keyword}");
                        await _queryService.NavigateToPeoplePageAsync(session.Page, searchUrl, keyword, cancellationToken);
                        _statistics.IncrementQueriesExecuted();
                    }
                    catch (Exception ex)
                    {
                        AppLog.Error(ex, "people search navigation failed", "ProcessCompany", "submit-query", $"company={input.CompanyName};keyword={keyword};searchUrl={searchUrl}");
                        AppLog.Info("skipping query", "ProcessCompany", "submit-query", $"company={input.CompanyName};keyword={keyword}");
                        continue;
                    }

                    IReadOnlyList<ContactDiscoveryTarget> targets;
                    try
                    {
                        AppLog.Next("discover matching profiles", "ProcessCompany", "discover-profiles", $"query={keyword}");
                        targets = await _queryService.DiscoverContactsAsync(session.Page, keyword, cancellationToken);
                        AppLog.Result("discovery complete", "ProcessCompany", "discover-profiles", $"query={keyword};targetCount={targets.Count}");
                        _consoleUiService.ShowDiscoveredProfiles(targets);
                    }
                    catch (Exception ex)
                    {
                        AppLog.Error(ex, $"Discovery failure for {keyword}. Continuing.", "ProcessCompany", "discover-profiles", $"query={keyword}");
                        AppLog.Info("skipping query", "ProcessCompany", "discover-profiles", $"company={input.CompanyName};keyword={keyword}");
                        continue;
                    }

                    _consoleUiService.ShowAbortHint();
                    await _consoleUiService.RunMappingProgressAsync(
                        input.CompanyName,
                        targets,
                        async (target, _, _) =>
                        {
                            if (shouldAbort())
                            {
                                return;
                            }

                            using (LogContext.PushProperty("ProfileUrl", target.Href))
                            {
                                AppLog.Next("open profile", "ProcessCompany", "open-profile", $"profileUrl={target.Href}");
                                var profilePage = await _profileExtractionService.TryOpenProfileInNewTabAsync(
                                    session.Context,
                                    session.Page,
                                    target,
                                    cancellationToken);

                                if (profilePage is null)
                                {
                                    _consoleUiService.ShowExtractionError("Failed to open profile experience");
                                    return;
                                }

                                _statistics.IncrementProfilesScanned();

                                try
                                {
                                    AppLog.Next("extract profile details", "ProcessCompany", "extract-profile", $"profileUrl={target.Href}");
                                    var profile = await _profileExtractionService.ExtractAsync(profilePage, target.DisplayName, keyword, cancellationToken);
                                    if (profile is null)
                                    {
                                        _consoleUiService.ShowExtractionError("Failed to extract profile experience");
                                        return;
                                    }

                                    var emails = _emailGenerationService.GenerateEmailPatterns(profile.FullName, input.CompanyDomain);

                                    var row = new MappedContactRow
                                    {
                                        CompanyName = input.CompanyName,
                                        SearchCountry = input.SearchCountry,
                                        SearchQuery = profile.SearchQuery,
                                        FullName = profile.FullName,
                                        Headline = profile.Headline,
                                        CurrentJobTitles = profile.CurrentJobTitles,
                                        ProfileURL = profile.ProfileUrl,
                                        EmailPrimary = emails.EmailPrimary,
                                        EmailAlt1 = emails.EmailAlt1,
                                        EmailAlt2 = emails.EmailAlt2,
                                        EmailAlt3 = emails.EmailAlt3,
                                        TimestampUTC = profile.TimestampUtc.ToString("yyyy-MM-ddTHH:mm:ssZ")
                                    };

                                    AppLog.Next("write mapped CSV row", "ProcessCompany", "write-csv-row", $"profileUrl={profile.ProfileUrl}");
                                    await csvWriter.WriteRowAsync(row, cancellationToken);
                                    _statistics.IncrementRecordsWritten();
                                    AppLog.Result("mapped row written", "ProcessCompany", "write-csv-row", $"profileUrl={profile.ProfileUrl};outputPath={csvWriter.OutputPath}");
                                    _consoleUiService.ShowExtractedProfile(profile);
                                }
                                catch (Exception ex)
                                {
                                    AppLog.Error(ex, $"Extraction failure for target {target.Href}", "ProcessCompany", "extract-profile", $"profileUrl={target.Href}");
                                    _consoleUiService.ShowExtractionError("Failed to extract profile experience");
                                }
                                finally
                                {
                                    AppLog.Action("close", "ProcessCompany", "close-profile-tab", $"profileUrl={target.Href}");
                                    await profilePage.CloseAsync();
                                    AppLog.Action("bring-to-front", "ProcessCompany", "focus-results-page", $"companyPageUrl={session.Page.Url}");
                                    await session.Page.BringToFrontAsync();
                                    await _humanDelayService.DelayAsync(DelayProfile.Navigation, "pause after closing profile tab and returning to results", cancellationToken);
                                }
                            }
                        },
                        shouldAbort);

                    if (shouldAbort())
                    {
                        return;
                    }

                    AppLog.Next("advance to next query if available", "ProcessCompany", "next-query", $"query={keyword}");
                    await _humanDelayService.DelayAsync(DelayProfile.Navigation, "pause between company title queries", cancellationToken);
                }
            }
        }
    }

    private static Task MonitorAbortKeyAsync(Action onAbortRequested, CancellationToken cancellationToken)
    {
        return Task.Run(async () =>
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(intercept: true).Key;
                    if (key == ConsoleKey.Escape)
                    {
                        onAbortRequested();
                        return;
                    }
                }

                await Task.Delay(100, cancellationToken);
            }
        }, CancellationToken.None).ContinueWith(
            _ => Task.CompletedTask,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default).Unwrap();
    }

    private static RunMetadata CreateRunMetadata()
    {
        var timestamp = DateTime.UtcNow;
        var runNumber = Directory
            .EnumerateFiles(AppPaths.OutputDirectory, "*.csv", SearchOption.TopDirectoryOnly)
            .Select(GetRunNumber)
            .DefaultIfEmpty(0)
            .Max() + 1;

        return new RunMetadata(runNumber, timestamp, timestamp.ToString("yyyyMMdd_HHmmss"));
    }

    private static int GetRunNumber(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        var markerIndex = name.IndexOf("_Run", StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
        {
            return 0;
        }

        var digits = new string(name
            .Skip(markerIndex + 4)
            .TakeWhile(char.IsDigit)
            .ToArray());

        return int.TryParse(digits, out var parsed) ? parsed : 0;
    }
}
