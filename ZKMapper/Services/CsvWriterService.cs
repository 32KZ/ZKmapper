using System.Globalization;
using CsvHelper;
using Serilog;
using ZKMapper.Infrastructure;
using ZKMapper.Models;

namespace ZKMapper.Services;

internal sealed class CsvWriterService : IDisposable
{
    private readonly StreamWriter _streamWriter;
    private readonly CsvWriter _csvWriter;

    public CsvWriterService(CompanyInput input, RunMetadata runMetadata)
    {
        Directory.CreateDirectory(AppPaths.OutputDirectory);

        var safeName = string.Concat(input.CompanyName.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_')).Trim('_');
        if (string.IsNullOrWhiteSpace(safeName))
        {
            safeName = "Company";
        }

        var timestampToken = runMetadata.StartedUtc.ToString("yyyy-MM-dd_HH-mm");
        var runNumber = ResolveNextRunNumber(safeName, timestampToken);
        var fileName = $"{safeName}_Run{runNumber:D2}_{timestampToken}.csv";
        var outputPath = Path.Combine(AppPaths.OutputDirectory, fileName);

        _streamWriter = new StreamWriter(new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read));
        _csvWriter = new CsvWriter(_streamWriter, CultureInfo.InvariantCulture);
        _csvWriter.WriteHeader<MappedContactRow>();
        _csvWriter.NextRecord();
        _streamWriter.Flush();

        OutputPath = outputPath;
    }

    public string OutputPath { get; }

    public async Task WriteRowAsync(MappedContactRow row, CancellationToken cancellationToken)
    {
        _csvWriter.WriteRecord(row);
        _csvWriter.NextRecord();
        await _streamWriter.FlushAsync(cancellationToken);
        Log.Information("CSV write success for {FullName} to {OutputPath}", row.FullName, OutputPath);
    }

    public void Dispose()
    {
        _csvWriter.Dispose();
        _streamWriter.Dispose();
    }

    private static int ResolveNextRunNumber(string safeName, string timestampToken)
    {
        var existingRuns = Directory
            .EnumerateFiles(AppPaths.OutputDirectory, $"{safeName}_Run*_*.csv", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => name is not null && name.Contains($"_{timestampToken}", StringComparison.OrdinalIgnoreCase))
            .Select(GetRunNumber)
            .DefaultIfEmpty(0)
            .Max();

        return existingRuns + 1;
    }

    private static int GetRunNumber(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return 0;
        }

        var markerIndex = fileName.IndexOf("_Run", StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
        {
            return 0;
        }

        var digits = new string(fileName
            .Skip(markerIndex + 4)
            .TakeWhile(char.IsDigit)
            .ToArray());

        return int.TryParse(digits, out var parsed) ? parsed : 0;
    }
}
