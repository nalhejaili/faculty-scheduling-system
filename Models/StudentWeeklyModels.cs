namespace MiniTrainerScheduler.Models
{
    /// <summary>
    /// Lightweight student row for pickers/lists (avoids loading full student tables into memory).
    /// </summary>
    public readonly record struct StudentLiteRow(int Id, string? StudentNo, string FullName, int? Level);

    /// <summary>
    /// Lightweight weekly assignment row for a single student.
    /// Includes slot timing so the weekly grid can be built even if Slots lookup is missing.
    /// </summary>
    public readonly record struct StudentWeeklyAssignmentRow(
        long AssignmentId,
        int DepartmentId,
        int CourseId,
        int SectionIndex,
        int SlotId,
        int FacultyId,
        int? RoomId,
        string Status,
        string Kind,
        int SlotDay,
        int StartMinutes,
        int EndMinutes
    );
}
