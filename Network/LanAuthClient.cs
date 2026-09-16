using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using TrainerScheduler.Security;
using TrainerScheduler.Services;

namespace TrainerScheduler.Network;

/// <summary>
/// Client-side auth against the Admin LAN server.
/// </summary>
public static class LanAuthClient
{
    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private sealed record LoginRequest(string Username, string Password);

    private sealed record LoginResponse(
        bool Ok,
        string ErrorMessage,
        string? Token,
        int? UserId,
        string? Username,
        string? Role,
        int? DepartmentId);

    public static async Task<(UserSession? session, string? token, string errorMessage)> LoginAsync(
        LanSettings settings,
        string username,
        string password)
    {
        if (settings is null) return (null, null, "LAN settings are not available.");

        var host = (settings.ServerHost ?? string.Empty).Trim();
        if (host.Length == 0) host = "127.0.0.1";

        // In Client mode, loopback means the user forgot to set the Admin IP.
        if (settings.Mode == LanMode.Client)
        {
            var h = host.ToLowerInvariant();
            if (h == "127.0.0.1" || h == "localhost" || h == "::1" || h == "0.0.0.0")
            {
                return (null, null,
                    "You are using Client mode. Open LAN Settings and enter the administrator device IP address instead of 127.0.0.1."
                    + "\n\nExample: 192.0.2.10");
            }
        }

        var port = settings.Port <= 0 ? 5178 : settings.Port;
        var baseUri = new Uri($"http://{host}:{port}");

        try
        {
            return await LanError.RetryAsync<(UserSession? session, string? token, string errorMessage)>("login", async attempt =>
            {
                LanLog.Info($"LAN Login attempt {attempt} -> {baseUri}");

                using var http = new HttpClient
                {
                    BaseAddress = baseUri,
                    Timeout = TimeSpan.FromSeconds(4)
                };

                var req = new LoginRequest(username, password);
                var json = JsonSerializer.Serialize(req, _json);
                using var content = new StringContent(json, Encoding.UTF8, "application/json");

                using var resp = await http.PostAsync("/auth/login", content);
                var body = await resp.Content.ReadAsStringAsync();

                if (!resp.IsSuccessStatusCode)
                {
                    // Retry only for transient gateway issues.
                    if (LanError.IsTransientStatus(resp.StatusCode))
                        throw new HttpRequestException($"HTTP {(int)resp.StatusCode} {resp.StatusCode}");

                    var msg = LanError.Friendly(resp.StatusCode);
                    if (!string.IsNullOrWhiteSpace(body) && body.Length < 300)
                        msg += "\n" + body.Trim();

                    return (null, null, msg);
                }

                LoginResponse? data = null;
                try { data = JsonSerializer.Deserialize<LoginResponse>(body, _json); } catch { /* ignore */ }

                if (data is null)
                    return (null, null, string.IsNullOrWhiteSpace(body) ? "Unable to read the server response." : body);

                if (!data.Ok)
                    return (null, null,
                        string.IsNullOrWhiteSpace(data.ErrorMessage) ? "Unable to sign in through the server." : data.ErrorMessage);

                if (data.UserId is null || string.IsNullOrWhiteSpace(data.Username) ||
                    string.IsNullOrWhiteSpace(data.Role) || string.IsNullOrWhiteSpace(data.Token))
                    return (null, null, "The server response is incomplete.");

                if (!Enum.TryParse<UserRole>(data.Role, ignoreCase: true, out var role))
                    role = UserRole.Supervisor;

                var session = new UserSession(data.UserId.Value, data.Username!, role, data.DepartmentId);
                return (session, data.Token, string.Empty);

            }, maxAttempts: LanError.DefaultMaxAttempts);
        }
        catch (Exception ex)
        {
            return (null, null, LanError.Friendly(ex));
        }
    }
}
