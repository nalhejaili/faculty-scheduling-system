using System;

namespace TrainerScheduler.Data.Entities
{
    /// <summary>
    /// to the internal CourseEntity.Id used inside the scheduler.
    ///
    /// Primary Key: CourseCode
    /// </summary>
    public sealed class CourseCodeMapEntity
    {
        /// <summary>
        /// Official course code from the report (e.g., "ENG101").
        /// </summary>
        public string CourseCode { get; set; } = string.Empty;

        /// <summary>
        /// Internal course ID (CourseEntity.Id). Nullable until the user maps it.
        /// </summary>
        public int? CourseId { get; set; }

        /// <summary>
        /// Last seen course name from the report (optional, for helping the user map).
        /// </summary>
        public string? CourseName { get; set; }

        /// <summary>
        /// Optional notes.
        /// </summary>
        public string? Notes { get; set; }

        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    }
}
