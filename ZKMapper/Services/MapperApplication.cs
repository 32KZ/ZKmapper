using Serilog;
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

    public MapperApplication(
        ConsolePromptService promptService,
        SessionStateManager sessionStateManager,
        BrowserManager browserManager,
        LinkedInNavigationService navigationService,
        LinkedInQueryService queryService,
        ProfileExtractionService profileExtractionService,
        EmailGenerationService emailGenerationService,
        HumanDelayService humanDelayService)
    {
        _promptService = promptService;
        _sessionStateManager = sessionStateManager;
        _browserManager = browserManager;
        _navigationService = navigationService;
        _queryService = queryService;
        _profileExtractionService = profileExtractionService;
        _emailGenerationService = emailGenerationService;
        _humanDelayService = humanDelayService;
    }

    public async Task<int> RunAuthSetupAsync()
    {
        Log.Information("Starting auth setup mode");
        await _sessionStateManager.CaptureSessionStateAsync(_browserManager, _promptService, CancellationToken.None);
        Console.WriteLine("Session saved.");
        Console.WriteLine("Press ENTER to exit.");
        Console.ReadLine();
        return 0;
    }

    public async Task<int> RunCollectionAsync()
    {
        _sessionStateManager.EnsureSessionStateExists();
        var runMetadata = CreateRunMetadata();

        await using var session = await _browserManager.LaunchAsync(useSavedSession: true, CancellationToken.None);

        while (true)
        {
            var input = _promptService.PromptCompanyInput();
            Log.Information("Company input accepted for {CompanyName}", input.CompanyName);

            using var csvWriter = new CsvWriterService(input, runMetadata);
            await ProcessCompanyAsync(session, input, csvWriter, CancellationToken.None);

            Log.Information("Company mapping completed for {CompanyName}", input.CompanyName);

            if (!_promptService.PromptYesNo("Would you like to add another company to map?"))
            {
                break;
            }
        }

        return 0;
    }

    private async Task ProcessCompanyAsync(
        PlaywrightSession session,
        CompanyInput input,
        CsvWriterService csvWriter,
        CancellationToken cancellationToken)
    {
        using (LogContext.PushProperty("Company", input.CompanyName))
        {
            Log.Information("Next step: open company page and switch to People for {CompanyName}", input.CompanyName);
            await _navigationService.NavigateToCompanyPeoplePageAsync(session.Page, input, cancellationToken);

            foreach (var title in input.TitleFilters)
            {
                var query = _queryService.BuildQuery(input.SearchCountry, title);
                using (LogContext.PushProperty("Query", query))
                {
                    Log.Information("Next step: submit LinkedIn people query {Query}", query);
                    try
                    {
                        await _queryService.SubmitQueryAsync(session.Page, query, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Query submission failure for {Query}. Continuing.", query);
                        continue;
                    }

                    IReadOnlyList<ContactDiscoveryTarget> targets;
                    try
                    {
                        Log.Information("Next step: discover matching profiles for query {Query}", query);
                        targets = await _queryService.DiscoverContactsAsync(session.Page, query, cancellationToken);
                        Log.Information("Discovery complete for query {Query}. Found {TargetCount} targets.", query, targets.Count);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Discovery failure for {Query}. Continuing.", query);
                        continue;
                    }

                    foreach (var target in targets)
                    {
                        using (LogContext.PushProperty("ProfileUrl", target.Href))
                        {
                            Log.Information("Next step: open profile {ProfileUrl}", target.Href);
                            var profilePage = await _profileExtractionService.TryOpenProfileInNewTabAsync(
                                session.Context,
                                session.Page,
                                target,
                                cancellationToken);

                            if (profilePage is null)
                            {
                                continue;
                            }

                            try
                            {
                                Log.Information("Next step: extract profile details from {ProfileUrl}", target.Href);
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

                                Log.Information("Next step: write mapped row for {ProfileUrl}", profile.ProfileUrl);
                                await csvWriter.WriteRowAsync(row, cancellationToken);
                                Log.Information("Mapped row written for {ProfileUrl}", profile.ProfileUrl);
                            }
                            catch (Exception ex)
                            {
                                Log.Error(ex, "Extraction failure for target {Target}", target.Href);
                            }
                            finally
                            {
                                await profilePage.CloseAsync();
                                await session.Page.BringToFrontAsync();
                                await _humanDelayService.DelayAsync(1, 2, cancellationToken);
                            }
                        }
                    }

                    await _humanDelayService.DelayAsync(2, 4, cancellationToken);
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
