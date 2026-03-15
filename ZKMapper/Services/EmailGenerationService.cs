using System.Text.RegularExpressions;
using ZKMapper.Infrastructure;
using ZKMapper.Models;

namespace ZKMapper.Services;

internal sealed class EmailGenerationService
{
    public EmailSet GenerateEmailPatterns(string fullName, string domain)
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
            return new EmailSet(string.Empty, string.Empty, string.Empty, string.Empty);
        }

        var normalizedDomain = NormalizeDomain(domain);
        if (string.IsNullOrWhiteSpace(normalizedDomain))
        {
            AppLog.Warn("No valid domain available for email generation", "EmailGeneration", "generate", $"fullName={fullName};domain={domain}");
            return new EmailSet(string.Empty, string.Empty, string.Empty, string.Empty);
        }

        if (tokens.Length == 1)
        {
            var fallback = $"{tokens[0]}@{normalizedDomain}";
            AppLog.Data(
                $"generated fallback email: {fallback}",
                "EmailGeneration",
                "generate",
                $"emailPrimary={fallback}");
            return new EmailSet(fallback, string.Empty, string.Empty, string.Empty);
        }

        var firstName = tokens.First();
        var lastName = tokens.Last();

        var result = new EmailSet(
            $"{firstName}.{lastName}@{normalizedDomain}",
            $"{firstName[..1]}.{lastName}@{normalizedDomain}",
            $"{firstName}{lastName}@{normalizedDomain}",
            $"{firstName[..1]}{lastName}@{normalizedDomain}");

        AppLog.Data(
            $"generated emails: {result.EmailPrimary}, {result.EmailAlt1}, {result.EmailAlt2}, {result.EmailAlt3}",
            "EmailGeneration",
            "generate",
            $"email1={result.EmailPrimary};email2={result.EmailAlt1};email3={result.EmailAlt2};email4={result.EmailAlt3}");

        return result;
    }

    private static string NormalizeToken(string token)
    {
        return Regex.Replace(token.ToLowerInvariant(), "[\\.,'\\-\\s]+", string.Empty);
    }

    private static string NormalizeDomain(string domain)
    {
        var normalized = domain.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        normalized = normalized.TrimStart('@');
        normalized = Regex.Replace(normalized, "\\s+", string.Empty);

        if (!normalized.Contains('.', StringComparison.Ordinal))
        {
            normalized = $"{normalized}.com";
        }

        return normalized.Trim('.');
    }
}
