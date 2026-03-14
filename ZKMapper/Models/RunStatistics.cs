namespace ZKMapper.Models;

internal sealed class RunStatistics
{
    public int ProfilesScanned { get; private set; }
    public int RecordsWritten { get; private set; }
    public int QueriesExecuted { get; private set; }

    public void IncrementProfilesScanned()
    {
        ProfilesScanned++;
    }

    public void IncrementRecordsWritten()
    {
        RecordsWritten++;
    }

    public void IncrementQueriesExecuted()
    {
        QueriesExecuted++;
    }
}
