using Serilog;
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

    public MapperApplication(
        ConsolePromptService promptService,
        SessionStateManager sessionStateManager,
        BrowserManager browserManager,
        LinkedInNavigationService navigationService,
        LinkedInQueryService queryService,
        ProfileExtractionService profileExtractionService,
        EmailGenerationService emailGenerationService)
    {
        _promptService = promptService;
        _sessionStateManager = sessionStateManager;
        _browserManager = browserManager;
        _navigationService = navigationService;
        _queryService = queryService;
        _profileExtractionService = profileExtractionService;
        _emailGenerationService = emailGenerationService;
    }

    public async Task<int> RunAuthSetupAsync()
    {
        Log.Information("Starting auth setup mode");

        await using var session = await _browserManager.LaunchAsync(useSavedSession: false, CancellationToken.None);
        await session.Page.GotoAsync("https://www.linkedin.com/login");

        _promptService.WaitForEnter(
            "Sign in to LinkedIn in the opened browser window, then press Enter here to save the session.");

        await _sessionStateManager.SaveStorageStateAsync(session.Context, CancellationToken.None);
        Console.WriteLine("LinkedIn session saved.");
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

            Log.Information("End of company processing for {CompanyName}", input.CompanyName);
            Log.Information("Prompt for next company");

            if (!_promptService.PromptYesNo("Add another company to map in this run?"))
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
        await _navigationService.NavigateToCompanyPeoplePageAsync(session.Page, input, cancellationToken);

        foreach (var title in input.TitleFilters)
        {
            var query = $"{input.SearchCountry} {title}";

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
                targets = await _queryService.DiscoverContactsAsync(session.Page, query, cancellationToken);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Discovery failure for {Query}. Continuing.", query);
                continue;
            }

            foreach (var target in targets)
            {
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
                    var profile = await _profileExtractionService.ExtractAsync(profilePage, cancellationToken);
                    var emails = _emailGenerationService.Generate(profile.FullName, input.CompanyDomain);

                    var row = new MappedContactRow
                    {
                        CompanyName = input.CompanyName,
                        SearchCountry = input.SearchCountry,
                        SearchQuery = query,
                        FullName = profile.FullName,
                        CurrentJobTitles = profile.CurrentJobTitles,
                        ProfileURL = profile.ProfileUrl,
                        EmailPrimary = emails.Primary,
                        EmailAlt1 = emails.Alt1,
                        EmailAlt2 = emails.Alt2,
                        EmailAlt3 = emails.Alt3,
                        TimestampUTC = DateTime.UtcNow
                    };

                    await csvWriter.WriteRowAsync(row, cancellationToken);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Profile extraction failure for target {Target}", target.Href);
                }
                finally
                {
                    await profilePage.CloseAsync();
                    await session.Page.BringToFrontAsync();
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
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
        var markerIndex = name.IndexOf("_run", StringComparison.OrdinalIgnoreCase);
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
