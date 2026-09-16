using System;

namespace MiniTrainerScheduler.Models
{
    /// <summary>
    /// </summary>
    public sealed class StudentEnrollment
    {
        public long Id { get; set; }

        public int StudentId { get; set; }
        public long AssignmentId { get; set; }

        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    }
}
