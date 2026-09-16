namespace MiniTrainerScheduler.Models
{
    public sealed record Course(
        int Id,
        string Name,
        int DepartmentId,
        int Level,
        bool IsGeneralCourse = false,
        int HoursPerWeek = 4
    )
    {
        public string? CourseCode { get; set; }

        public string DisplayName => string.IsNullOrWhiteSpace(CourseCode)
            ? Name
            : $"{CourseCode} — {Name}";
    }
}
