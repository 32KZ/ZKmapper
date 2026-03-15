using Spectre.Console;
using System.Diagnostics;
using ZKMapper.Infrastructure;
using ZKMapper.Models;

namespace ZKMapper.Services;

internal sealed class MenuService
{
    private readonly ConsolePromptService _promptService;
    private readonly ConsoleUiService _consoleUiService;
    private readonly MapperApplication _mapperApplication;
    private readonly ConfigurationService _configurationService;
    private readonly InputFileLoader _inputFileLoader;

    public MenuService(
        ConsolePromptService promptService,
        ConsoleUiService consoleUiService,
        MapperApplication mapperApplication,
        ConfigurationService configurationService,
        InputFileLoader inputFileLoader)
    {
        _promptService = promptService;
        _consoleUiService = consoleUiService;
        _mapperApplication = mapperApplication;
        _configurationService = configurationService;
        _inputFileLoader = inputFileLoader;
    }

    public async Task<int> ShowMainMenuAsync()
    {
        while (true)
        {
            _consoleUiService.ResetScreen();
            var selection = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("Select an option")
                    .PageSize(10)
                    .AddChoices("Start Mapping", "Map From Input File", "Manage CSV Files", "Manage Logs", "Options", "Exit"));

            AppLog.Step("main menu selection received", "Menu", "show-main-menu", $"selection={selection}");

            switch (selection)
            {
                case "Start Mapping":
                    await StartMappingAsync();
                    break;
                case "Map From Input File":
                    await MapFromInputFileAsync();
                    break;
                case "Manage CSV Files":
                    ManageCsv();
                    break;
                case "Manage Logs":
                    ManageLogs();
                    break;
                case "Options":
                    OptionsMenu();
                    break;
                case "Exit":
                    AppLog.Result("main menu exit selected", "Menu", "show-main-menu", "selection=Exit");
                    return 0;
                default:
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
            _consoleUiService.ResetScreen();
            var input = _promptService.PromptCompanyInput();
            queue.Add(input);
            AppLog.Info("[QUEUE] company added", "Menu", "start-mapping", $"companyName={input.CompanyName}");
            AppLog.Data($"queueSize={queue.Count}", "Menu", "start-mapping", $"queueSize={queue.Count}");

            if (!_promptService.PromptYesNo("Add another company?"))
            {
                break;
            }
        }

        AppLog.Info("[QUEUE] starting company mapping", "Menu", "start-mapping", $"queueCount={queue.Count}");
        _consoleUiService.ResetScreen();
        AnsiConsole.MarkupLine("[grey]Processing queue...[/]");
        await _mapperApplication.RunCollectionAsync(queue);
    }

    public async Task MapFromInputFileAsync()
    {
        AppLog.Step("starting batch mapping from input file", "Menu", "map-from-input-file");
        _consoleUiService.ResetScreen();
        var inputPath = _promptService.PromptTextOrDefault("Enter path to input file", AppPaths.DefaultBatchInputFilePath);
        var queue = _inputFileLoader.LoadQueue(inputPath);
        _consoleUiService.ResetScreen();
        AnsiConsole.MarkupLine($"[grey]Loaded {queue.Count} companies[/]");
        AnsiConsole.MarkupLine("[grey]Processing queue...[/]");
        await _mapperApplication.RunCollectionAsync(queue);
    }

    public void ManageCsv()
    {
        ManageFiles(
            AppPaths.OutputDirectory,
            "*.csv",
            "Stored CSV Files",
            "CSV File",
            "excel.exe",
            "manage-csv",
            "[CSV]");
    }

    public void ManageLogs()
    {
        ManageFiles(
            AppPaths.LogDirectory,
            "*.log",
            "Stored Logs",
            "Log File",
            "notepad++.exe",
            "manage-logs",
            "[LOG]");
    }

    public void OptionsMenu()
    {
        while (true)
        {
            _consoleUiService.ResetScreen();
            var navigation = _configurationService.GetDelayRange(DelayProfile.Navigation);
            var scroll = _configurationService.GetDelayRange(DelayProfile.Scroll);
            var profile = _configurationService.GetDelayRange(DelayProfile.Profile);

            var selection = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("Options")
                    .PageSize(10)
                    .AddChoices(
                        $"Navigation Delay ({navigation.MinMs}-{navigation.MaxMs} ms)",
                        $"Scroll Delay ({scroll.MinMs}-{scroll.MaxMs} ms)",
                        $"Profile Delay ({profile.MinMs}-{profile.MaxMs} ms)",
                        "Back"));
            switch (selection)
            {
                case var _ when selection.StartsWith("Navigation Delay", StringComparison.Ordinal):
                    UpdateDelayRange(DelayProfile.Navigation, "Navigation Delay");
                    break;
                case var _ when selection.StartsWith("Scroll Delay", StringComparison.Ordinal):
                    UpdateDelayRange(DelayProfile.Scroll, "Scroll Delay");
                    break;
                case var _ when selection.StartsWith("Profile Delay", StringComparison.Ordinal):
                    UpdateDelayRange(DelayProfile.Profile, "Profile Delay");
                    break;
                case "Back":
                    return;
            }
        }
    }

    private void UpdateDelayRange(DelayProfile profile, string label)
    {
        var previous = _configurationService.GetDelayRange(profile);
        var currentMin = previous.MinMs;
        var currentMax = previous.MaxMs;
        var selectedIndex = 0;
        var actions = new[] { "Min", "Max", "Save", "Exit" };

        while (true)
        {
            _consoleUiService.ResetScreen();
            AnsiConsole.MarkupLine($"[bold]{Markup.Escape(label)}[/]");
            AnsiConsole.MarkupLine($"[grey]Previously set:[/] Min {previous.MinMs} ms | Max {previous.MaxMs} ms");
            AnsiConsole.MarkupLine($"[grey]Use Up/Down to select. Left/Right changes by 1000 ms. Enter activates Save/Exit.[/]");
            AnsiConsole.WriteLine();

            DrawDelayEditorRow(actions, selectedIndex, 0, $"Min: {currentMin} ms");
            DrawDelayEditorRow(actions, selectedIndex, 1, $"Max: {currentMax} ms");
            DrawDelayEditorRow(actions, selectedIndex, 2, "Save");
            DrawDelayEditorRow(actions, selectedIndex, 3, "Exit");

            var key = Console.ReadKey(intercept: true).Key;
            switch (key)
            {
                case ConsoleKey.UpArrow:
                    selectedIndex = selectedIndex == 0 ? actions.Length - 1 : selectedIndex - 1;
                    break;
                case ConsoleKey.DownArrow:
                    selectedIndex = (selectedIndex + 1) % actions.Length;
                    break;
                case ConsoleKey.LeftArrow:
                    if (selectedIndex == 0)
                    {
                        currentMin = Math.Max(0, currentMin - 1000);
                        if (currentMin > currentMax)
                        {
                            currentMax = currentMin;
                        }
                    }
                    else if (selectedIndex == 1)
                    {
                        currentMax = Math.Max(currentMin, currentMax - 1000);
                    }
                    break;
                case ConsoleKey.RightArrow:
                    if (selectedIndex == 0)
                    {
                        currentMin += 1000;
                        if (currentMin > currentMax)
                        {
                            currentMax = currentMin;
                        }
                    }
                    else if (selectedIndex == 1)
                    {
                        currentMax += 1000;
                    }
                    break;
                case ConsoleKey.Enter:
                    if (selectedIndex == 2)
                    {
                        _configurationService.UpdateDelayRange(profile, new DelayRange(currentMin, currentMax));
                        _consoleUiService.ResetScreen();
                        AnsiConsole.MarkupLine($"[green]Saved {Markup.Escape(label)}:[/] Min {currentMin} ms | Max {currentMax} ms");
                        return;
                    }

                    if (selectedIndex == 3)
                    {
                        return;
                    }
                    break;
                case ConsoleKey.Escape:
                    return;
            }
        }
    }

    private string PromptSelector(string title, params string[] choices)
    {
        _consoleUiService.ResetScreen();
        return AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title(title)
                .PageSize(10)
                .AddChoices(choices));
    }

    private void ManageFiles(
        string directory,
        string searchPattern,
        string title,
        string itemLabel,
        string defaultProgram,
        string actionName,
        string logPrefix)
    {
        while (true)
        {
            _consoleUiService.ResetScreen();
            Directory.CreateDirectory(directory);
            var files = Directory
                .EnumerateFiles(directory, searchPattern, SearchOption.TopDirectoryOnly)
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            AppLog.Info($"{logPrefix} listing files", "Menu", actionName, $"fileCount={files.Length}");

            if (files.Length == 0)
            {
                AnsiConsole.MarkupLine("[grey]No files found.[/]");
                AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title(title)
                        .PageSize(10)
                        .AddChoices("Back"));
                return;
            }

            var fileChoices = files
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Cast<string>()
                .Append("Back")
                .ToArray();

            var selectedFile = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title(title)
                    .PageSize(10)
                    .AddChoices(fileChoices));

            if (selectedFile == "Back")
            {
                return;
            }

            var targetFile = files.First(file => string.Equals(Path.GetFileName(file), selectedFile, StringComparison.OrdinalIgnoreCase));
            var action = PromptSelector(
                $"{itemLabel}: {selectedFile}",
                "Open",
                "Delete",
                "Back");

            if (action == "Back")
            {
                continue;
            }

            if (action == "Open")
            {
                OpenManagedFile(targetFile, defaultProgram, actionName, logPrefix);
                continue;
            }

            AppLog.Info($"{logPrefix} deleting file", "Menu", actionName, $"file={targetFile}");
            File.Delete(targetFile);
            _consoleUiService.ResetScreen();
            AnsiConsole.MarkupLine($"[red]Deleted {Markup.Escape(Path.GetFileName(targetFile))}[/]");
        }
    }

    private void OpenManagedFile(string targetFile, string defaultProgram, string actionName, string logPrefix)
    {
        _consoleUiService.ResetScreen();
        var program = _promptService.PromptTextOrDefault("Program to open file (leave blank for default app)", defaultProgram);

        if (string.IsNullOrWhiteSpace(program))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = targetFile,
                UseShellExecute = true
            });
            AppLog.Info($"{logPrefix} opened file with shell default", "Menu", actionName, $"file={targetFile}");
            _consoleUiService.ResetScreen();
            AnsiConsole.MarkupLine($"[green]Opened {Markup.Escape(Path.GetFileName(targetFile))} with the default program.[/]");
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = program,
            Arguments = $"\"{targetFile}\"",
            UseShellExecute = true
        });
        AppLog.Info($"{logPrefix} opened file with selected program", "Menu", actionName, $"file={targetFile};program={program}");
        _consoleUiService.ResetScreen();
        AnsiConsole.MarkupLine($"[green]Opened {Markup.Escape(Path.GetFileName(targetFile))} with {Markup.Escape(program)}.[/]");
    }

    private static void DrawDelayEditorRow(string[] actions, int selectedIndex, int rowIndex, string text)
    {
        if (selectedIndex == rowIndex)
        {
            AnsiConsole.MarkupLine($"[deepskyblue1]>[/] [bold]{Markup.Escape(text)}[/]");
            return;
        }

        AnsiConsole.MarkupLine($"  {Markup.Escape(text)}");
    }
}
