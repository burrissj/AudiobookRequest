using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace AudiobookRequest.Core.Clients;

/// <summary>
/// Logs a failed call to an outside service. A plain network problem (this server can't resolve a name, the
/// connection is refused, it times out) is one short line saying what happened; a full stack trace for it only
/// buries the log and hides the cause. Anything unexpected still logs the whole exception.
/// </summary>
public static class ExternalCallLog
{
    public static void LogFailure(this ILogger logger, Exception ex, string messageTemplate, params object?[] args)
    {
        var reason = Describe(ex);
        if (reason is null)
        {
            logger.LogWarning(ex, messageTemplate, args);
            return;
        }

        logger.LogWarning(messageTemplate + ": {Reason}", [.. args, reason]);
    }

    /// <returns>A plain-English reason for a network-level failure, or null for anything that isn't one.</returns>
    public static string? Describe(Exception ex)
    {
        switch (ex)
        {
            case HttpRequestException { InnerException: SocketException socket }:
                return socket.SocketErrorCode switch
                {
                    SocketError.HostNotFound or SocketError.TryAgain or SocketError.NoData =>
                        $"DNS lookup failed ({socket.Message.TrimEnd('.')}); this server can't resolve the host, so check its internet connection and DNS",
                    SocketError.ConnectionRefused => "connection refused",
                    SocketError.TimedOut => "connection timed out",
                    SocketError.NetworkUnreachable or SocketError.HostUnreachable => "network unreachable; check this server's internet connection",
                    _ => socket.Message.TrimEnd('.'),
                };
            case HttpRequestException { StatusCode: { } status }:
                return $"the service answered HTTP {(int)status} ({status})";
            case HttpRequestException http:
                return http.Message.TrimEnd('.');
            case TaskCanceledException { InnerException: TimeoutException }:
            case TimeoutException:
                return "timed out";
            default:
                return null;
        }
    }
}
