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
    private readonly WebhookAuthenticationStorageService _webhookAuthenticationStorage;
    private readonly WebhookService _webhookService;

    public MenuService(
        ConsolePromptService promptService,
        ConsoleUiService consoleUiService,
        MapperApplication mapperApplication,
        ConfigurationService configurationService,
        InputFileLoader inputFileLoader,
        WebhookAuthenticationStorageService webhookAuthenticationStorage,
        WebhookService webhookService)
    {
        _promptService = promptService;
        _consoleUiService = consoleUiService;
        _mapperApplication = mapperApplication;
        _configurationService = configurationService;
        _inputFileLoader = inputFileLoader;
        _webhookAuthenticationStorage = webhookAuthenticationStorage;
        _webhookService = webhookService;
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
                    .AddChoices("Start Mapping", "Map From Input File", "Manage CSV Files", "Manage Logs", "Send Files to Webhook", "Options", "Exit"));

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
                case "Send Files to Webhook":
                    await SendFilesToWebhookAsync();
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
                        "Webhook Settings",
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
                case "Webhook Settings":
                    ShowWebhookSettingsMenu();
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

    private void ShowWebhookSettingsMenu()
    {
        while (true)
        {
            var webhook = _configurationService.GetWebhookSettings();
            var auth = _webhookAuthenticationStorage.Load();
            var headerState = webhook.HeaderAuthenticationEnabled ? "Enabled" : "Disabled";
            var configuredAuth = auth.IsConfigured ? auth.HeaderName : "Not configured";

            _consoleUiService.ResetScreen();
            var selection = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("Webhook Settings")
                    .PageSize(10)
                    .AddChoices(
                        $"Select Webhook Mode ({webhook.ActiveMode})",
                        $"Configure Production URL ({DisplayConfigured(webhook.ProductionWebhookUrl)})",
                        $"Configure Test URL ({DisplayConfigured(webhook.TestWebhookUrl)})",
                        $"Header Authentication ({headerState})",
                        $"Header Credentials ({configuredAuth})",
                        "Back"));

            switch (selection)
            {
                case var _ when selection.StartsWith("Select Webhook Mode", StringComparison.Ordinal):
                    ConfigureWebhookMode();
                    break;
                case var _ when selection.StartsWith("Configure Production URL", StringComparison.Ordinal):
                    ConfigureWebhookUrl(WebhookMode.Production);
                    break;
                case var _ when selection.StartsWith("Configure Test URL", StringComparison.Ordinal):
                    ConfigureWebhookUrl(WebhookMode.Test);
                    break;
                case var _ when selection.StartsWith("Header Authentication", StringComparison.Ordinal):
                    ConfigureHeaderAuthentication();
                    break;
                case var _ when selection.StartsWith("Header Credentials", StringComparison.Ordinal):
                    ConfigureHeaderCredentials();
                    break;
                case "Back":
                    return;
            }
        }
    }

    private async Task SendFilesToWebhookAsync()
    {
        var webhookSettings = _configurationService.GetWebhookSettings();
        var webhookUrl = _webhookService.GetActiveWebhookUrl();

        if (string.IsNullOrWhiteSpace(webhookUrl))
        {
            _consoleUiService.ResetScreen();
            AnsiConsole.MarkupLine("[red]Webhook URL is not configured for the active mode.[/]");
            _promptService.WaitForEnter("Press ENTER to return to the main menu.");
            return;
        }

        var csvPath = SelectFilePath("Select CSV File", AppPaths.OutputDirectory, "*.csv");
        if (string.IsNullOrWhiteSpace(csvPath))
        {
            return;
        }

        var logPath = SelectFilePath("Select Log File", AppPaths.LogDirectory, "*.log");
        if (string.IsNullOrWhiteSpace(logPath))
        {
            return;
        }

        _consoleUiService.ResetScreen();
        var confirmationTable = new Table().Border(TableBorder.Rounded);
        confirmationTable.AddColumn("Field");
        confirmationTable.AddColumn("Value");
        confirmationTable.AddRow("CSV File", Markup.Escape(csvPath));
        confirmationTable.AddRow("Log File", Markup.Escape(logPath));
        confirmationTable.AddRow("Webhook URL", Markup.Escape(webhookUrl));
        confirmationTable.AddRow("Mode", webhookSettings.ActiveMode.ToString());
        AnsiConsole.Write(confirmationTable);
        AnsiConsole.WriteLine();

        var confirmed = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Send selected files?")
                .PageSize(10)
                .AddChoices("Send", "Cancel"));

        if (confirmed != "Send")
        {
            return;
        }

        var result = await _webhookService.SendFilesAsync(csvPath, logPath);
        _consoleUiService.ResetScreen();

        if (result.Success)
        {
            AnsiConsole.MarkupLine("[green]Webhook sent successfully.[/]");
        }
        else
        {
            AnsiConsole.MarkupLine("[red]Webhook failed.[/]");
        }

        AnsiConsole.MarkupLine($"Status Code: {Markup.Escape(result.StatusCode?.ToString() ?? "n/a")}");
        if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
        {
            AnsiConsole.MarkupLine($"Error: {Markup.Escape(result.ErrorMessage)}");
        }

        AnsiConsole.MarkupLine($"Response: {Markup.Escape(result.ResponseBody)}");
        _promptService.WaitForEnter("Press ENTER to return to the main menu.");
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

    private void ConfigureWebhookMode()
    {
        var selection = PromptSelector("Select Webhook Mode", "Production", "Test");
        var mode = string.Equals(selection, "Production", StringComparison.Ordinal)
            ? WebhookMode.Production
            : WebhookMode.Test;

        _configurationService.UpdateWebhookMode(mode);
        _consoleUiService.ResetScreen();
        AnsiConsole.MarkupLine($"[green]Active webhook mode set to {Markup.Escape(mode.ToString())}.[/]");
    }

    private void ConfigureWebhookUrl(WebhookMode mode)
    {
        var settings = _configurationService.GetWebhookSettings();
        var currentUrl = mode == WebhookMode.Production
            ? settings.ProductionWebhookUrl
            : settings.TestWebhookUrl;
        var updatedUrl = _promptService.PromptTextOrDefault($"Enter {mode} webhook URL", currentUrl);
        _configurationService.UpdateWebhookUrl(mode, updatedUrl);
        _consoleUiService.ResetScreen();
        AnsiConsole.MarkupLine($"[green]{Markup.Escape(mode.ToString())} webhook URL saved.[/]");
    }

    private void ConfigureHeaderAuthentication()
    {
        var selection = PromptSelector("Header Authentication", "Disabled", "Enabled");
        var enabled = string.Equals(selection, "Enabled", StringComparison.Ordinal);
        _configurationService.UpdateHeaderAuthenticationEnabled(enabled);

        if (enabled)
        {
            ConfigureHeaderCredentials();
            return;
        }

        _consoleUiService.ResetScreen();
        AnsiConsole.MarkupLine("[green]Header authentication disabled.[/]");
    }

    private void ConfigureHeaderCredentials()
    {
        var currentValues = _webhookAuthenticationStorage.Load();
        var headerName = _promptService.PromptTextOrDefault("Header name", currentValues.HeaderName);
        var headerSecret = _promptService.PromptTextOrDefault("Header secret", currentValues.HeaderSecret);
        _webhookAuthenticationStorage.Save(headerName, headerSecret);
        _consoleUiService.ResetScreen();
        AnsiConsole.MarkupLine("[green]Webhook header credentials saved locally.[/]");
    }

    private string? SelectFilePath(string title, string defaultDirectory, string searchPattern)
    {
        while (true)
        {
            _consoleUiService.ResetScreen();
            Directory.CreateDirectory(defaultDirectory);
            var files = Directory
                .EnumerateFiles(defaultDirectory, searchPattern, SearchOption.TopDirectoryOnly)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .ThenBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var choices = files
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Cast<string>()
                .ToList();

            choices.Add("Enter path manually");
            choices.Add("Back");

            var selected = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title(title)
                    .PageSize(12)
                    .AddChoices(choices));

            if (selected == "Back")
            {
                return null;
            }

            if (selected == "Enter path manually")
            {
                var manualPath = _promptService.PromptRequiredText("Enter full file path");
                if (File.Exists(manualPath))
                {
                    return manualPath;
                }

                _consoleUiService.ResetScreen();
                AnsiConsole.MarkupLine($"[red]File not found:[/] {Markup.Escape(manualPath)}");
                _promptService.WaitForEnter("Press ENTER to try again.");
                continue;
            }

            return files.First(file => string.Equals(Path.GetFileName(file), selected, StringComparison.OrdinalIgnoreCase));
        }
    }

    private static string DisplayConfigured(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "Not set" : "Configured";
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
