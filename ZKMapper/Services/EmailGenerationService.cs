using ZKMapper.Infrastructure;

namespace ZKMapper.Services;

internal sealed class EmailGenerationService
{
    public (string Primary, string Alt1, string Alt2, string Alt3) Generate(string fullName, string domain)
    {
        using var timer = ExecutionTimer.Start("EmailGeneration");
        AppLog.Step("generating email guesses", "EmailGeneration", "generate", $"fullName={fullName};domain={domain}");

        var tokens = fullName
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeToken)
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .ToArray();

        if (tokens.Length == 0)
        {
            AppLog.Warn("No valid name tokens available for email generation", "EmailGeneration", "generate", $"fullName={fullName}");
            return (string.Empty, string.Empty, string.Empty, string.Empty);
        }

        var firstName = tokens.First();
        var lastName = tokens.Last();
        var normalizedDomain = domain.Trim().ToLowerInvariant();

        var result = (
            $"{firstName}.{lastName}@{normalizedDomain}",
            $"{firstName}{lastName}@{normalizedDomain}",
            $"{firstName[..1]}{lastName}@{normalizedDomain}",
            $"{firstName}.{lastName[..1]}@{normalizedDomain}");

        AppLog.Data(
            $"generated emails: {result.Item1}, {result.Item2}, {result.Item3}, {result.Item4}",
            "EmailGeneration",
            "generate",
            $"email1={result.Item1};email2={result.Item2};email3={result.Item3};email4={result.Item4}");

        return result;
    }

    private static string NormalizeToken(string token)
    {
        return token
            .ToLowerInvariant()
            .Replace("'", string.Empty, StringComparison.Ordinal)
            .Replace(".", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal);
    }
}
