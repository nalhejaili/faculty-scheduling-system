using System;

namespace MiniTrainerScheduler.Models
{
    /// <summary>
    /// </summary>
    public sealed class IntakeDemand
    {
        public string Term { get; set; } = "";
        public string Program { get; set; } = "";

        public int DepartmentId { get; set; }
        public int Level { get; set; }
        public int CourseId { get; set; }

        public int DemandCount { get; set; }
        public int RepeatersCount { get; set; }

        public bool? IsPractical { get; set; }

        public IntakeDemand() { }

        public IntakeDemand(
            int departmentId,
            int level,
            int courseId,
            int demandCount,
            int repeatersCount = 0,
            string term = "",
            string program = "",
            bool? isPractical = null)
        {
            DepartmentId = departmentId;
            Level = level;
            CourseId = courseId;
            DemandCount = demandCount;
            RepeatersCount = repeatersCount;
            Term = term ?? "";
            Program = program ?? "";
            IsPractical = isPractical;
        }
    }
}
