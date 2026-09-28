using AudiobookRequest.Api.Endpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace AudiobookRequest.Api.Hubs;

[Authorize]
public class NotificationHub : Hub;

public interface INotificationSender
{
    /// <summary>Pushed to every connected client whenever a request's status changes.</summary>
    Task RequestUpdatedAsync(BookRequestDto request, CancellationToken ct = default);
}

public class NotificationSender(IHubContext<NotificationHub> hub) : INotificationSender
{
    public Task RequestUpdatedAsync(BookRequestDto request, CancellationToken ct = default) =>
        hub.Clients.All.SendAsync("requestUpdated", request, ct);
}
