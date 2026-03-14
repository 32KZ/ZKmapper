using ZKMapper.Infrastructure;
using ZKMapper.Models;

namespace ZKMapper.Services;

internal sealed class ConsolePromptService
{
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
        while (true)
        {
            Console.Write($"{message} (y/n): ");
            var response = Console.ReadLine()?.Trim().ToLowerInvariant();
            AppLog.Input($"yesNoPrompt={message};response={response}", $"prompt={message};response={response}");

            if (response is "y" or "yes")
            {
                return true;
            }

            if (response is "n" or "no")
            {
                return false;
            }

            Console.WriteLine("Please enter y or n.");
        }
    }

    public void WaitForEnter(string message)
    {
        Console.WriteLine(message);
        AppLog.Step(message, "InputCapture", "wait-for-enter");
        Console.ReadLine();
        AppLog.Result("ENTER received", "InputCapture", "wait-for-enter");
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
