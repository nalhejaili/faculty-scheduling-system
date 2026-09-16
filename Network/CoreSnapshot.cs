using System.Collections.Generic;
using TrainerScheduler.Data.Entities;
using MiniTrainerScheduler.Models;

namespace TrainerScheduler.Network;

/// <summary>
/// Core lookup snapshot used by LAN clients.
/// </summary>
public sealed record CoreSnapshot
{
    public List<DepartmentEntity> Departments { get; init; } = new();
    public List<CourseEntity> Courses { get; init; } = new();
    public List<FacultyEntity> Faculties { get; init; } = new();
    public List<RoomEntity> Rooms { get; init; } = new();
    public List<SlotEntity> Slots { get; init; } = new();

    public List<int> PracticalCourseIds { get; init; } = new();
    public List<CourseHourSplitOverride> CourseHourSplits { get; init; } = new();
    public List<CourseFacultyOverride> CourseFacultyOverrides { get; init; } = new();
    public List<CourseCodeFacultyOverride> CourseCodeFacultyOverrides { get; init; } = new();
}
