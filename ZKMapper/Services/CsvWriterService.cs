using System.Globalization;
using CsvHelper;
using ZKMapper.Infrastructure;
using ZKMapper.Models;

namespace ZKMapper.Services;

internal sealed class CsvWriterService : IDisposable
{
    private readonly StreamWriter _streamWriter;
    private readonly CsvWriter _csvWriter;
    private int _rowIndex;

    public CsvWriterService(CompanyInput input, RunMetadata runMetadata)
    {
        using var timer = ExecutionTimer.Start("CsvWriterInitialization");
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
        AppLog.Result("CSV output file created", "CsvWriterInitialization", "create-output-file", $"path={OutputPath}");
    }

    public string OutputPath { get; }

    public async Task WriteRowAsync(MappedContactRow row, CancellationToken cancellationToken)
    {
        using var timer = ExecutionTimer.Start("CsvWrite");
        var nextRowIndex = _rowIndex + 1;
        AppLog.Step("writing record", "CsvWrite", "write-row", $"filePath={OutputPath};rowIndex={nextRowIndex}");
        AppLog.Data(
            $"rowData fullName={row.FullName};profileUrl={row.ProfileURL}",
            "CsvWrite",
            "write-row",
            $"fullName={row.FullName};profileUrl={row.ProfileURL}");
        _csvWriter.WriteRecord(row);
        _csvWriter.NextRecord();
        await _streamWriter.FlushAsync(cancellationToken);
        _rowIndex = nextRowIndex;
        AppLog.Result("row written successfully", "CsvWrite", "write-row", $"filePath={OutputPath};rowIndex={_rowIndex}");
    }

    public void Dispose()
    {
        AppLog.Info("disposing CSV writer", "CsvWriter", "dispose", $"outputPath={OutputPath};rows={_rowIndex}");
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
