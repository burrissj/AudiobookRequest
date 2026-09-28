using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace AudiobookRequest.Api.Hubs;

[Authorize]
public class NotificationHub : Hub
{
    // Clients call no server methods yet; the server pushes "bookReady" events
    // (see INotificationSender) once the processing pipeline lands a request (Phase 3).
}

public interface INotificationSender
{
    Task BookReadyAsync(int bookRequestId, string title, CancellationToken ct = default);
}

public class NotificationSender(IHubContext<NotificationHub> hub) : INotificationSender
{
    public Task BookReadyAsync(int bookRequestId, string title, CancellationToken ct = default) =>
        hub.Clients.All.SendAsync("bookReady", new { bookRequestId, title }, ct);
}
