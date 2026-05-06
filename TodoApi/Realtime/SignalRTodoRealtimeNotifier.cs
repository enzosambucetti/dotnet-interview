using Microsoft.AspNetCore.SignalR;

namespace TodoApi.Realtime;

public class SignalRTodoRealtimeNotifier : ITodoRealtimeNotifier
{
    public const string ClientEventName = "todoUpdated";
    private readonly IHubContext<TodoUpdatesHub> _hubContext;

    public SignalRTodoRealtimeNotifier(IHubContext<TodoUpdatesHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public Task PublishAsync(
        TodoRealtimeEvent realtimeEvent,
        CancellationToken cancellationToken = default
    )
    {
        return _hubContext.Clients.All.SendAsync(ClientEventName, realtimeEvent, cancellationToken);
    }

    public async Task PublishManyAsync(
        IEnumerable<TodoRealtimeEvent> realtimeEvents,
        CancellationToken cancellationToken = default
    )
    {
        foreach (var realtimeEvent in realtimeEvents)
        {
            await PublishAsync(realtimeEvent, cancellationToken);
        }
    }
}
