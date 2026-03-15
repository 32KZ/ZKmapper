using Spectre.Console;
using ZKMapper.Infrastructure;
using ZKMapper.Models;

namespace ZKMapper.Services;

internal sealed class ConsolePromptService
{
    public string PromptMenuChoice(string prompt)
    {
        Console.Write($"{prompt}: ");
        var response = Console.ReadLine()?.Trim() ?? string.Empty;
        AppLog.Input($"menuChoice={response}", $"prompt={prompt};response={response}");
        return response;
    }

    public CompanyInput PromptCompanyInput()
    {
        AppLog.Step("capturing CLI input", "InputCapture", "prompt-company-input");

        var companyName = PromptRequired("Company name");
        var companyDomain = PromptRequired("Company email domain");
        var companyLinkedInUrl = PromptRequired("Company LinkedIn URL");
        var searchCountry = PromptRequired("Country filter");
        var rawTitles = PromptRequired("Job title filters (comma separated)");

        var titleFilters = rawTitles
            .Split(",")
            .Select(title => title.Trim())
            .Where(title => !string.IsNullOrWhiteSpace(title))
            .ToArray();

        if (titleFilters.Length == 0)
        {
            throw new InvalidOperationException("At least one job title filter is required.");
        }

        AppLog.Input($"companyName={companyName.Trim()}", $"companyName={companyName.Trim()}");
        AppLog.Input($"companyUrl={companyLinkedInUrl.Trim()}", $"companyUrl={companyLinkedInUrl.Trim()}");
        AppLog.Input($"domain={companyDomain.Trim()}", $"domain={companyDomain.Trim()}");
        AppLog.Input($"country={searchCountry.Trim()}", $"country={searchCountry.Trim()}");
        AppLog.Input($"titles={string.Join(", ", titleFilters)}", $"titles={string.Join(", ", titleFilters)}");

        return new CompanyInput(
            companyName.Trim(),
            companyDomain.Trim(),
            companyLinkedInUrl.Trim(),
            searchCountry.Trim(),
            titleFilters);
    }

    public bool PromptYesNo(string message)
    {
        var selection = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title(message)
                .PageSize(10)
                .AddChoices("Yes", "No"));

        AppLog.Input($"yesNoPrompt={message};response={selection}", $"prompt={message};response={selection}");
        return string.Equals(selection, "Yes", StringComparison.Ordinal);
    }

    public void WaitForEnter(string message)
    {
        Console.WriteLine(message);
        AppLog.Step(message, "InputCapture", "wait-for-enter");
        Console.ReadLine();
        AppLog.Result("ENTER received", "InputCapture", "wait-for-enter");
    }

    public int PromptRequiredInt(string label)
    {
        while (true)
        {
            Console.Write($"{label}: ");
            var value = Console.ReadLine()?.Trim();
            AppLog.Input($"intPrompt={label};response={value}", $"label={label};response={value}");

            if (int.TryParse(value, out var parsed))
            {
                return parsed;
            }

            Console.WriteLine($"{label} must be a whole number.");
        }
    }

    public string PromptRequiredText(string label)
    {
        while (true)
        {
            Console.Write($"{label}: ");
            var value = Console.ReadLine()?.Trim();
            AppLog.Input($"textPrompt={label};response={value}", $"label={label};response={value}");

            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            Console.WriteLine($"{label} is required.");
        }
    }

    public string PromptTextOrDefault(string label, string defaultValue)
    {
        if (string.IsNullOrEmpty(defaultValue))
        {
            Console.Write($"{label}: ");
            var directValue = Console.ReadLine()?.Trim() ?? string.Empty;
            AppLog.Input($"textPrompt={label};response={directValue}", $"label={label};response={directValue};defaultApplied=false");
            return directValue;
        }

        Console.Write($"{label} [{defaultValue}]: ");
        var value = Console.ReadLine()?.Trim();
        var resolvedValue = string.IsNullOrWhiteSpace(value) ? defaultValue : value;
        AppLog.Input($"textPrompt={label};response={resolvedValue}", $"label={label};response={resolvedValue};defaultApplied={string.IsNullOrWhiteSpace(value)}");
        return resolvedValue;
    }

    private static string PromptRequired(string label)
    {
        while (true)
        {
            Console.Write($"{label}: ");
            var value = Console.ReadLine();
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            Console.WriteLine($"{label} is required.");
        }
    }
}
