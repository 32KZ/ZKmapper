using Serilog.Context;
using ZKMapper.Infrastructure;
using ZKMapper.Models;
using ZKMapper.Utils;

namespace ZKMapper.Services;

internal sealed class MapperApplication
{
    private readonly ConsolePromptService _promptService;
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
        AppLog.Step("starting collection run", "RunCollection", "initialize-run", $"runNumber={runMetadata.RunNumber};startedUtc={runMetadata.StartedUtc:O};queueCount={queue.Count}");
        await using var session = await _browserManager.LaunchAsync(useSavedSession: true, CancellationToken.None);

        foreach (var input in queue.Companies)
        {
            using var csvWriter = new CsvWriterService(input, runMetadata);
            AppLog.Data($"csvOutputPath={csvWriter.OutputPath}", "RunCollection", "initialize-csv-writer", $"outputPath={csvWriter.OutputPath}");
            try
            {
                await ProcessCompanyAsync(session, input, csvWriter, CancellationToken.None);
                AppLog.Result("company mapping completed", "RunCollection", "process-company", $"companyName={input.CompanyName};outputPath={csvWriter.OutputPath}");
            }
            catch (Exception ex)
            {
                AppLog.Error(ex, "company mapping failed and will be skipped", "RunCollection", "process-company", $"companyName={input.CompanyName}");
            }
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
            var slug = LinkedInUrlParser.ExtractCompanySlug(input.CompanyLinkedInUrl);
            var regionId = _regionMapper.ResolveRegionId(input.SearchCountry);

            for (var titleIndex = 0; titleIndex < input.TitleFilters.Count; titleIndex++)
            {
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
                    }
                    catch (Exception ex)
                    {
                        AppLog.Error(ex, $"Discovery failure for {keyword}. Continuing.", "ProcessCompany", "discover-profiles", $"query={keyword}");
                        AppLog.Info("skipping query", "ProcessCompany", "discover-profiles", $"company={input.CompanyName};keyword={keyword}");
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
                                var profile = await _profileExtractionService.ExtractAsync(profilePage, keyword, cancellationToken);
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
                                    Headline = profile.Headline,
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

                    AppLog.Next("advance to next query if available", "ProcessCompany", "next-query", $"query={keyword}");
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
