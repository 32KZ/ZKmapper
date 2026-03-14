using ZKMapper.Infrastructure;
using ZKMapper.Models;

namespace ZKMapper.Services;

internal sealed class MenuService
{
    private readonly ConsolePromptService _promptService;
    private readonly MapperApplication _mapperApplication;
    private readonly ConfigurationService _configurationService;

    public MenuService(
        ConsolePromptService promptService,
        MapperApplication mapperApplication,
        ConfigurationService configurationService)
    {
        _promptService = promptService;
        _mapperApplication = mapperApplication;
        _configurationService = configurationService;
    }

    public async Task<int> ShowMainMenuAsync()
    {
        while (true)
        {
            Console.WriteLine("==== ZKMapper ====");
            Console.WriteLine("1 Start Mapping");
            Console.WriteLine("2 Manage CSVs");
            Console.WriteLine("3 Options");
            Console.WriteLine("4 Exit");

            var selection = _promptService.PromptMenuChoice("Select option");
            AppLog.Step("main menu selection received", "Menu", "show-main-menu", $"selection={selection}");

            switch (selection)
            {
                case "1":
                    await StartMappingAsync();
                    break;
                case "2":
                    ManageCsv();
                    break;
                case "3":
                    OptionsMenu();
                    break;
                case "4":
                    AppLog.Result("main menu exit selected", "Menu", "show-main-menu", "selection=4");
                    return 0;
                default:
                    Console.WriteLine("Invalid option.");
                    AppLog.Warn("invalid main menu selection", "Menu", "show-main-menu", $"selection={selection}");
                    break;
            }

            Console.WriteLine();
        }
    }

    public async Task StartMappingAsync()
    {
        var queue = new MappingQueue();
        AppLog.Step("starting mapping queue capture", "Menu", "start-mapping");

        while (true)
        {
            var input = _promptService.PromptCompanyInput();
            queue.Add(input);
            AppLog.Info("[QUEUE] company added", "Menu", "start-mapping", $"companyName={input.CompanyName}");
            AppLog.Info($"[QUEUE] total companies queued={queue.Count}", "Menu", "start-mapping", $"queueCount={queue.Count}");

            if (!_promptService.PromptYesNo("Add another company?"))
            {
                break;
            }
        }

        AppLog.Result("mapping queue ready", "Menu", "start-mapping", $"queueCount={queue.Count}");
        Console.WriteLine("Processing queue...");
        await _mapperApplication.RunCollectionAsync(queue);
    }

    public void ManageCsv()
    {
        while (true)
        {
            Directory.CreateDirectory(AppPaths.OutputDirectory);
            var files = Directory
                .EnumerateFiles(AppPaths.OutputDirectory, "*.csv", SearchOption.TopDirectoryOnly)
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            AppLog.Info("[CSV] listing stored files", "Menu", "manage-csv", $"fileCount={files.Length}");
            Console.WriteLine("Stored CSV files:");

            if (files.Length == 0)
            {
                Console.WriteLine("No CSV files found.");
            }
            else
            {
                for (var index = 0; index < files.Length; index++)
                {
                    Console.WriteLine($"{index + 1} {Path.GetFileName(files[index])}");
                }
            }

            Console.WriteLine("Enter number to delete");
            Console.WriteLine("Enter A to delete all");
            Console.WriteLine("Enter B to go back");

            var selection = _promptService.PromptMenuChoice("Select CSV action");
            if (string.Equals(selection, "B", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (string.Equals(selection, "A", StringComparison.OrdinalIgnoreCase))
            {
                AppLog.Info("[CSV] deleting file", "Menu", "manage-csv", "selection=all");
                foreach (var file in files)
                {
                    File.Delete(file);
                }

                Console.WriteLine("All CSV files deleted.");
                continue;
            }

            if (!int.TryParse(selection, out var fileNumber) || fileNumber < 1 || fileNumber > files.Length)
            {
                Console.WriteLine("Invalid selection.");
                continue;
            }

            var targetFile = files[fileNumber - 1];
            AppLog.Info("[CSV] deleting file", "Menu", "manage-csv", $"file={targetFile}");
            File.Delete(targetFile);
            Console.WriteLine($"Deleted {Path.GetFileName(targetFile)}");
        }
    }

    public void OptionsMenu()
    {
        while (true)
        {
            var navigation = _configurationService.GetDelayRange(DelayProfile.Navigation);
            var search = _configurationService.GetDelayRange(DelayProfile.Search);
            var profile = _configurationService.GetDelayRange(DelayProfile.ProfileOpen);

            Console.WriteLine("Options");
            Console.WriteLine($"1 Navigation Delay ({navigation.MinMs}-{navigation.MaxMs} ms)");
            Console.WriteLine($"2 Search Delay ({search.MinMs}-{search.MaxMs} ms)");
            Console.WriteLine($"3 Profile Delay ({profile.MinMs}-{profile.MaxMs} ms)");
            Console.WriteLine("4 Back");

            var selection = _promptService.PromptMenuChoice("Select option");
            switch (selection)
            {
                case "1":
                    UpdateDelayRange(DelayProfile.Navigation, "Navigation Delay");
                    break;
                case "2":
                    UpdateDelayRange(DelayProfile.Search, "Search Delay");
                    break;
                case "3":
                    UpdateDelayRange(DelayProfile.ProfileOpen, "Profile Delay");
                    break;
                case "4":
                    return;
                default:
                    Console.WriteLine("Invalid option.");
                    break;
            }
        }
    }

    private void UpdateDelayRange(DelayProfile profile, string label)
    {
        Console.WriteLine($"{label}:");
        var min = _promptService.PromptRequiredInt("Min ms");
        var max = _promptService.PromptRequiredInt("Max ms");
        _configurationService.UpdateDelayRange(profile, new DelayRange(min, max));
    }
}
