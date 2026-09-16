using System;

namespace TrainerScheduler.Data.Entities
{
    /// <summary>
    /// Persisted student request/plan row.
    /// </summary>
    public sealed class StudentPlanEntity
    {
        public long Id { get; set; }

        public int StudentId { get; set; }
        public int CourseId { get; set; }

        public int Priority { get; set; }
        public bool IsRepeat { get; set; }
        public string? TermKey { get; set; }

        public DateTime CreatedUtc { get; set; }
    }
}
