using Serilog.Context;
using ZKMapper.Infrastructure;
using ZKMapper.Models;

namespace ZKMapper.Services;

internal sealed class MapperApplication
{
    private readonly ConsolePromptService _promptService;
    private readonly SessionStateManager _sessionStateManager;
    private readonly BrowserManager _browserManager;
    private readonly LinkedInNavigationService _navigationService;
    private readonly LinkedInQueryService _queryService;
    private readonly ProfileExtractionService _profileExtractionService;
    private readonly EmailGenerationService _emailGenerationService;
    private readonly HumanDelayService _humanDelayService;
    private readonly RunStatistics _statistics;

    public MapperApplication(
        ConsolePromptService promptService,
        SessionStateManager sessionStateManager,
        BrowserManager browserManager,
        LinkedInNavigationService navigationService,
        LinkedInQueryService queryService,
        ProfileExtractionService profileExtractionService,
        EmailGenerationService emailGenerationService,
        HumanDelayService humanDelayService,
        RunStatistics statistics)
    {
        _promptService = promptService;
        _sessionStateManager = sessionStateManager;
        _browserManager = browserManager;
        _navigationService = navigationService;
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
        AppLog.Step("starting collection run", "RunCollection", "initialize-run", $"runNumber={runMetadata.RunNumber};startedUtc={runMetadata.StartedUtc:O};queueCount={queue.Count}");
        await using var session = await _browserManager.LaunchAsync(useSavedSession: true, CancellationToken.None);

        foreach (var input in queue.Companies)
        {
            using var csvWriter = new CsvWriterService(input, runMetadata);
            AppLog.Data($"csvOutputPath={csvWriter.OutputPath}", "RunCollection", "initialize-csv-writer", $"outputPath={csvWriter.OutputPath}");

            await ProcessCompanyAsync(session, input, csvWriter, CancellationToken.None);
            AppLog.Result("company mapping completed", "RunCollection", "process-company", $"companyName={input.CompanyName};outputPath={csvWriter.OutputPath}");
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
        CancellationToken cancellationToken)
    {
        using var timer = ExecutionTimer.Start("ProcessCompany");

        using (LogContext.PushProperty("Company", input.CompanyName))
        {
            AppLog.Next("open company page and switch to People", "ProcessCompany", "navigate-company", $"companyName={input.CompanyName}");
            await _navigationService.NavigateToCompanyPeoplePageAsync(session.Page, input, cancellationToken);

            for (var titleIndex = 0; titleIndex < input.TitleFilters.Count; titleIndex++)
            {
                var title = input.TitleFilters[titleIndex];
                var query = _queryService.BuildQuery(input.SearchCountry, title);

                using (LogContext.PushProperty("Query", query))
                {
                    AppLog.Data(
                        $"queryIndex={titleIndex + 1};keyword={query};queryUrl={session.Page.Url}",
                        "ProcessCompany",
                        "prepare-query",
                        $"queryIndex={titleIndex + 1};keyword={query};queryUrl={session.Page.Url}");

                    try
                    {
                        AppLog.Next("submit LinkedIn people query", "ProcessCompany", "submit-query", $"queryIndex={titleIndex + 1};keyword={query}");
                        await _queryService.SubmitQueryAsync(session.Page, query, cancellationToken);
                        _statistics.IncrementQueriesExecuted();
                    }
                    catch (Exception ex)
                    {
                        AppLog.Error(ex, $"Query submission failure for {query}. Continuing.", "ProcessCompany", "submit-query", $"query={query}");
                        continue;
                    }

                    IReadOnlyList<ContactDiscoveryTarget> targets;
                    try
                    {
                        AppLog.Next("discover matching profiles", "ProcessCompany", "discover-profiles", $"query={query}");
                        targets = await _queryService.DiscoverContactsAsync(session.Page, query, cancellationToken);
                        AppLog.Result("discovery complete", "ProcessCompany", "discover-profiles", $"query={query};targetCount={targets.Count}");
                    }
                    catch (Exception ex)
                    {
                        AppLog.Error(ex, $"Discovery failure for {query}. Continuing.", "ProcessCompany", "discover-profiles", $"query={query}");
                        continue;
                    }

                    foreach (var target in targets)
                    {
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
                                continue;
                            }

                            _statistics.IncrementProfilesScanned();

                            try
                            {
                                AppLog.Next("extract profile details", "ProcessCompany", "extract-profile", $"profileUrl={target.Href}");
                                var profile = await _profileExtractionService.ExtractAsync(profilePage, query, cancellationToken);
                                if (profile is null)
                                {
                                    continue;
                                }

                                var emails = _emailGenerationService.Generate(profile.FullName, input.CompanyDomain);
                                var row = new MappedContactRow
                                {
                                    CompanyName = input.CompanyName,
                                    SearchCountry = input.SearchCountry,
                                    SearchQuery = profile.SearchQuery,
                                    FullName = profile.FullName,
                                    CurrentJobTitles = profile.CurrentJobTitles,
                                    ProfileURL = profile.ProfileUrl,
                                    EmailPrimary = emails.Primary,
                                    EmailAlt1 = emails.Alt1,
                                    EmailAlt2 = emails.Alt2,
                                    EmailAlt3 = emails.Alt3,
                                    TimestampUTC = profile.TimestampUtc
                                };

                                AppLog.Next("write mapped CSV row", "ProcessCompany", "write-csv-row", $"profileUrl={profile.ProfileUrl}");
                                await csvWriter.WriteRowAsync(row, cancellationToken);
                                _statistics.IncrementRecordsWritten();
                                AppLog.Result("mapped row written", "ProcessCompany", "write-csv-row", $"profileUrl={profile.ProfileUrl};outputPath={csvWriter.OutputPath}");
                            }
                            catch (Exception ex)
                            {
                                AppLog.Error(ex, $"Extraction failure for target {target.Href}", "ProcessCompany", "extract-profile", $"profileUrl={target.Href}");
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
                    }

                    AppLog.Next("advance to next query if available", "ProcessCompany", "next-query", $"query={query}");
                    await _humanDelayService.DelayAsync(DelayProfile.Navigation, "pause between company title queries", cancellationToken);
                }
            }
        }
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
