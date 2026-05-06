namespace TodoApi.Realtime;

public static class TodoRealtimeEventTypes
{
    public const string TodoListCreated = nameof(TodoListCreated);
    public const string TodoListUpdated = nameof(TodoListUpdated);
    public const string TodoListDeleted = nameof(TodoListDeleted);
    public const string ItemCreated = nameof(ItemCreated);
    public const string ItemUpdated = nameof(ItemUpdated);
    public const string ItemDeleted = nameof(ItemDeleted);
    public const string InboundSyncCompleted = nameof(InboundSyncCompleted);
}
