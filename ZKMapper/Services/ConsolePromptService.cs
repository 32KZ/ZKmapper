using ZKMapper.Models;

namespace ZKMapper.Services;

internal sealed class ConsolePromptService
{
    public CompanyInput PromptCompanyInput()
    {
        var companyName = PromptRequired("Company name");
        var companyDomain = PromptRequired("Company email domain");
        var companyLinkedInUrl = PromptRequired("Company LinkedIn URL");
        var searchCountry = PromptRequired("Country filter");
        var rawTitles = PromptRequired("Job title filters (comma-separated)");

        var titleFilters = rawTitles
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(title => !string.IsNullOrWhiteSpace(title))
            .ToArray();

        if (titleFilters.Length == 0)
        {
            throw new InvalidOperationException("At least one job title filter is required.");
        }

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
        Console.ReadLine();
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
