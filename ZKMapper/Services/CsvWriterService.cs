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
        var fileName = $"{safeName}_run{runMetadata.RunNumber:D3}_{runMetadata.TimestampToken}.csv";
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
}
