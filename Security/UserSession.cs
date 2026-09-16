namespace TrainerScheduler.Security
{
    public sealed record UserSession(
        int UserId,
        string Username,
        UserRole Role,
        int? DepartmentId
    );
}
