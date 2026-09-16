using System;

namespace MiniTrainerScheduler.Models
{
    public enum IssueSeverity { Info, Warning, Error }

    public enum IssueCode
    {
        UnscheduledSection,
        FacultyConflict,
        RoomUnavailable,
        SlotUnavailable,
        CapacityExceeded,
        Other
    }

    public sealed class PlanningIssue
    {
        public IssueSeverity Severity { get; set; }
        public string Message { get; set; } = "";
        public string Context { get; set; } = "";

        public IssueCode Code { get; set; } = IssueCode.Other;
        public int? DepartmentId { get; set; }
        public int? Level { get; set; }
        public int? CourseId { get; set; }
        public int? SectionIndex { get; set; }
        public int? SlotId { get; set; }
        public int? MissingCount { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public override string ToString()
            => $"{Severity} [{Code}]: {Message} ({Context})";


        public static PlanningIssue Unscheduled(string courseName, int courseId,
            int deptId, int level, int missingCount)
        {
            return new PlanningIssue
            {
                Severity = IssueSeverity.Warning,
                Code = IssueCode.UnscheduledSection,
                Message = $"Unable to generate {missingCount} section(s) for course \"{courseName}\" (level {level}).",
                Context = $"Dept={deptId};Level={level};Course={courseId}",
                DepartmentId = deptId,
                Level = level,
                CourseId = courseId,
                MissingCount = missingCount
            };
        }

        public static PlanningIssue FacultyBusy(string courseName, int courseId,
            int facultyId, int slotId, int deptId, int level, int sectionIndex)
        {
            return new PlanningIssue
            {
                Severity = IssueSeverity.Warning,
                Code = IssueCode.FacultyConflict,
                Message = $"Faculty conflict while scheduling \"{courseName}\" (section {sectionIndex}).",
                Context = $"Dept={deptId};Level={level};Course={courseId};Faculty={facultyId};Slot={slotId}",
                DepartmentId = deptId,
                Level = level,
                CourseId = courseId,
                SectionIndex = sectionIndex,
                SlotId = slotId
            };
        }

        public static PlanningIssue RoomBusy(string courseName, int courseId,
            int roomId, int slotId, int deptId, int level, int sectionIndex)
        {
            return new PlanningIssue
            {
                Severity = IssueSeverity.Warning,
                Code = IssueCode.RoomUnavailable,
                Message = $"The room is unavailable while scheduling \"{courseName}\" (section {sectionIndex}).",
                Context = $"Dept={deptId};Level={level};Course={courseId};Room={roomId};Slot={slotId}",
                DepartmentId = deptId,
                Level = level,
                CourseId = courseId,
                SectionIndex = sectionIndex,
                SlotId = slotId
            };
        }
    }
}
