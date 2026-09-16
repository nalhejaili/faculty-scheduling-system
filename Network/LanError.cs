using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;
using TrainerScheduler.Services;

namespace TrainerScheduler.Network;

/// <summary>
/// Friendly LAN network errors + simple retry policy for transient failures.
/// </summary>
public static class LanError
{
    public const int DefaultMaxAttempts = 3;

    public static bool IsTransient(Exception ex)
    {
        if (ex is TaskCanceledException) return true;          // timeout
        if (ex is HttpRequestException) return true;           // network / DNS / refused
        if (ex is IOException) return true;                    // read/write errors
        return false;
    }

    public static TimeSpan DelayForAttempt(int attempt)
    {
        // 250ms, 500ms, 750ms (simple backoff)
        var ms = Math.Clamp(250 * attempt, 200, 1500);
        return TimeSpan.FromMilliseconds(ms);
    }

    public static bool IsTransientStatus(HttpStatusCode code)
    {
        return code == HttpStatusCode.BadGateway
            || code == HttpStatusCode.ServiceUnavailable
            || code == HttpStatusCode.GatewayTimeout;
    }

    public static string Friendly(Exception ex)
    {
        // Most common cases first.
        if (ex is TaskCanceledException)
            return "The connection to the server timed out. Make sure the administrator device is running, the port is correct, and the firewall allows the connection.";

        if (ex is HttpRequestException hre)
        {
            // SocketException gives more accurate reasons.
            if (hre.InnerException is SocketException se)
            {
                return se.SocketErrorCode switch
                {
                    SocketError.ConnectionRefused =>
                        "The connection to the server was refused. Make sure the server is running on the administrator device and the port number is correct.",
                    SocketError.TimedOut =>
                        "The connection attempt timed out. Please check the network and firewall.",
                    SocketError.NetworkUnreachable =>
                        "The network is unavailable. Make sure both devices are connected to the same network.",
                    SocketError.HostUnreachable =>
                        "Unable to reach the administrator device. Check the IP address and ensure both devices are on the same network.",
                    SocketError.AddressNotAvailable =>
                        "The server address is invalid or unavailable. Please verify the IP address.",
                    _ => "Unable to connect to the server. Please check the IP address, port, and firewall settings."
                };
            }

            // Fallback: common text hints (DNS etc.)
            var msg = (hre.Message ?? string.Empty).ToLowerInvariant();
            if (msg.Contains("name") && msg.Contains("host"))
                return "Unable to resolve the host name (DNS). Use the direct IP address of the administrator device.";
            if (msg.Contains("no such host") || msg.Contains("not known"))
                return "The server could not be found. Please check the IP address and port.";

            return "Unable to connect to the server. Please check the IP address, port, and firewall settings.";
        }

        return string.IsNullOrWhiteSpace(ex.Message)
            ? "An unexpected network error occurred."
            : ex.Message;
    }

    public static string Friendly(HttpStatusCode code)
    {
        return code switch
        {
            HttpStatusCode.Unauthorized => "The session has expired or the sign-in details are invalid. Please sign in again.",
            HttpStatusCode.Forbidden => "You do not have permission to perform this action on the server.",
            HttpStatusCode.NotFound => "The requested endpoint was not found on the server. Make sure the administrator copy is up to date.",
            HttpStatusCode.RequestTimeout => "The request timed out. Please check the network and firewall.",
            HttpStatusCode.InternalServerError => "An internal error occurred on the administrator server. Please review the network log on the administrator device.",
            _ => $"The request to the server failed. (HTTP {(int)code})"
        };
    }

    public static async Task<T> RetryAsync<T>(
        string opName,
        Func<int, Task<T>> action,
        int maxAttempts = DefaultMaxAttempts)
    {
        Exception? last = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                return await action(attempt);
            }
            catch (Exception ex) when (IsTransient(ex) && attempt < maxAttempts)
            {
                last = ex;
                LanLog.Warn($"LAN {opName} retry {attempt}/{maxAttempts}: {ex.GetType().Name} - {ex.Message}");
                await Task.Delay(DelayForAttempt(attempt));
            }
            catch (Exception ex)
            {
                last = ex;
                break;
            }
        }

        LanLog.Error($"LAN {opName} failed after retries.", last);
        throw last ?? new Exception("LAN operation failed.");
    }
}
