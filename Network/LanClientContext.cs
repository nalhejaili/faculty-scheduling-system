using TrainerScheduler.Security;

namespace TrainerScheduler.Network;

/// <summary>
/// Holds LAN client connection info (server + auth token) in-memory.
/// </summary>
public static class LanClientContext
{
    public static LanSettings? Settings { get; private set; }
    public static string? Token { get; private set; }
    public static UserSession? Session { get; private set; }

    // Last revision observed from the server (via /rev). Used to prevent stale pushes.
    public static long? LastKnownRevision { get; set; }

    public static bool IsConnected => Settings is not null && !string.IsNullOrWhiteSpace(Token) && Session is not null;

    // Compatibility alias (some callers used "IsAuthenticated").
    public static bool IsAuthenticated => IsConnected;

    public static void Set(LanSettings settings, string token, UserSession session)
    {
        Settings = settings;
        Token = token;
        Session = session;
        LastKnownRevision = null;
    }

    public static void Clear()
    {
        Settings = null;
        Token = null;
        Session = null;
        LastKnownRevision = null;
    }
}
