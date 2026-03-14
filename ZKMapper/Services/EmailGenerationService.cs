namespace ZKMapper.Services;

internal sealed class EmailGenerationService
{
    public (string Primary, string Alt1, string Alt2, string Alt3) Generate(string fullName, string domain)
    {
        var tokens = fullName
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeToken)
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .ToArray();

        if (tokens.Length == 0)
        {
            return (string.Empty, string.Empty, string.Empty, string.Empty);
        }

        var firstName = tokens.First();
        var lastName = tokens.Last();
        var normalizedDomain = domain.Trim().ToLowerInvariant();

        return (
            $"{firstName}.{lastName}@{normalizedDomain}",
            $"{firstName}{lastName}@{normalizedDomain}",
            $"{firstName[..1]}{lastName}@{normalizedDomain}",
            $"{firstName}.{lastName[..1]}@{normalizedDomain}");
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
