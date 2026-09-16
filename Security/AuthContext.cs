namespace TrainerScheduler.Security
{
    /// <summary>
    /// Holds the current authenticated session in-memory.
    /// UI can bind to it later.
    /// </summary>
    public static class AuthContext
    {
        public static UserSession? Current { get; private set; }

        public static void Set(UserSession? session)
        {
            Current = session;
        }

        public static void Clear() => Set(null);

        public static bool IsAdmin => Current?.Role == UserRole.Admin;
        public static bool IsSupervisor => Current?.Role == UserRole.Supervisor;
    }
}
