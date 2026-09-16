using System;

namespace MiniTrainerScheduler.Models
{
    /// <summary>
    /// </summary>
    public sealed class StudentPlan
    {
        public long Id { get; set; }

        public int StudentId { get; set; }
        public int CourseId { get; set; }

        /// <summary>
        /// </summary>
        public int Priority { get; set; }

        /// <summary>
        /// </summary>
        public bool IsRepeat { get; set; }

        /// <summary>
        /// </summary>
        public string? TermKey { get; set; }

        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    }
}
