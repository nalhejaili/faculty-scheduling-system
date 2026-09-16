namespace MiniTrainerScheduler.ViewModels
{
    public sealed class CourseFacultyOverrideRow
    {
        /// <summary>
        /// "Course" = specific course (CourseId).
        /// "Code"   = course code (CourseCode) scoped by department.
        /// Legacy Arabic values are still supported for previously saved rows.
        /// </summary>
        public string Kind { get; set; } = "Course";

        public bool IsCodeMode =>
            string.Equals(Kind, "Code", System.StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Kind, "\u0631\u0645\u0632", System.StringComparison.Ordinal);

        public string KindDisplay => IsCodeMode ? "Code" : "Course";

        // CourseId-level overrides
        public int CourseId { get; set; }

        // CourseCode-level overrides
        public string CourseCode { get; set; } = "";
        public int ScopeDepartmentId { get; set; } = 0;
        public string ScopeDepartmentName { get; set; } = "";

        // Display
        public string CourseName { get; set; } = "";
        public int FacultyId { get; set; }
        public string FacultyName { get; set; } = "";
    }
}
