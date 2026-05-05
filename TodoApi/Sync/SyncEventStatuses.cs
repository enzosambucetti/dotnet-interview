namespace TodoApi.Sync;

public static class SyncEventStatuses
{
    public const string Pending = nameof(Pending);
    public const string Processing = nameof(Processing);
    public const string Completed = nameof(Completed);
    public const string Failed = nameof(Failed);
    public const string FailedRetryable = nameof(FailedRetryable);
    public const string FailedTerminal = nameof(FailedTerminal);
}
