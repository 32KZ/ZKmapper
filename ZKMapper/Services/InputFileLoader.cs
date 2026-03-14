using ZKMapper.Infrastructure;
using ZKMapper.Models;

namespace ZKMapper.Services;

internal sealed class InputFileLoader
{
    public MappingQueue LoadQueue(string inputPath)
    {
        using var timer = ExecutionTimer.Start("InputFileLoad");
        AppLog.Step("loading mapping queue from input file", "InputFileLoad", "load-input-file", $"path={inputPath}");

        var resolvedPath = ResolvePath(inputPath);
        if (!File.Exists(resolvedPath))
        {
            AppLog.Error(
                new FileNotFoundException("Input file not found.", resolvedPath),
                "input file not found",
                "InputFileLoad",
                "load-input-file",
                $"path={resolvedPath}");
            throw new FileNotFoundException($"Input file not found: {resolvedPath}", resolvedPath);
        }

        var queue = new MappingQueue();
        var lines = File.ReadAllLines(resolvedPath);

        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            var line = lines[lineIndex];
            var trimmedLine = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmedLine))
            {
                continue;
            }

            if (trimmedLine.StartsWith('#'))
            {
                AppLog.Data(
                    $"skipping commented batch input line;lineNumber={lineIndex + 1}",
                    "InputFileLoad",
                    "parse-input-line",
                    $"path={resolvedPath};lineNumber={lineIndex + 1};reason=comment");
                continue;
            }

            var parts = trimmedLine.Split('|');
            if (parts.Length != 5)
            {
                AppLog.Error(
                    new InvalidOperationException("Invalid batch input line format."),
                    "invalid batch input line",
                    "InputFileLoad",
                    "parse-input-line",
                    $"path={resolvedPath};lineNumber={lineIndex + 1};partCount={parts.Length}");
                throw new InvalidOperationException($"Invalid input line {lineIndex + 1} in {resolvedPath}. Expected 5 pipe-delimited fields.");
            }

            var titleFilters = parts[4]
                .Split(',')
                .Select(title => title.Trim())
                .Where(title => !string.IsNullOrWhiteSpace(title))
                .ToArray();

            if (titleFilters.Length == 0)
            {
                AppLog.Error(
                    new InvalidOperationException("At least one job title is required."),
                    "batch input line missing job titles",
                    "InputFileLoad",
                    "parse-input-line",
                    $"path={resolvedPath};lineNumber={lineIndex + 1}");
                throw new InvalidOperationException($"Input line {lineIndex + 1} in {resolvedPath} has no job titles.");
            }

            var company = new CompanyInput(
                parts[0].Trim(),
                parts[1].Trim(),
                parts[2].Trim(),
                parts[3].Trim(),
                titleFilters);

            queue.Add(company);
            AppLog.Info("[QUEUE] company added", "InputFileLoad", "parse-input-line", $"companyName={company.CompanyName};lineNumber={lineIndex + 1}");
        }

        AppLog.Input($"batchFile={resolvedPath}", $"path={resolvedPath}");
        AppLog.Info($"[QUEUE] companiesLoaded={queue.Count}", "InputFileLoad", "load-input-file", $"path={resolvedPath};companiesLoaded={queue.Count}");
        return queue;
    }

    private static string ResolvePath(string inputPath)
    {
        if (Path.IsPathRooted(inputPath))
        {
            return inputPath;
        }

        return Path.GetFullPath(Path.Combine(AppPaths.RootDirectory, inputPath));
    }
}
