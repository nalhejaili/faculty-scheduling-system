using System;

namespace TrainerScheduler.Data.Entities
{
    /// <summary>
    /// Persisted student -> assignment enrollment.
    /// </summary>
    public sealed class StudentEnrollmentEntity
    {
        public long Id { get; set; }

        public int StudentId { get; set; }
        public long AssignmentId { get; set; }

        public DateTime CreatedUtc { get; set; }
    }
}
