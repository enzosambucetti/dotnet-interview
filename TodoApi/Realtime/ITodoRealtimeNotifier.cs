namespace TodoApi.Realtime;

public interface ITodoRealtimeNotifier
{
    Task PublishAsync(TodoRealtimeEvent realtimeEvent, CancellationToken cancellationToken = default);
    Task PublishManyAsync(
        IEnumerable<TodoRealtimeEvent> realtimeEvents,
        CancellationToken cancellationToken = default
    );
}
