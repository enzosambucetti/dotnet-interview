namespace TodoApi.Realtime;

public class NoOpTodoRealtimeNotifier : ITodoRealtimeNotifier
{
    public Task PublishAsync(
        TodoRealtimeEvent realtimeEvent,
        CancellationToken cancellationToken = default
    )
    {
        return Task.CompletedTask;
    }

    public Task PublishManyAsync(
        IEnumerable<TodoRealtimeEvent> realtimeEvents,
        CancellationToken cancellationToken = default
    )
    {
        return Task.CompletedTask;
    }
}
