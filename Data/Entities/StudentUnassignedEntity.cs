using System;

namespace TrainerScheduler.Data.Entities
{
    public sealed class StudentUnassignedEntity
    {
        public long Id { get; set; }

        public int StudentId { get; set; }
        public int CourseId { get; set; }

        // "THEORY" / "LAB" (normalized)
        public string Kind { get; set; } = "";

        public string Reason { get; set; } = "";
        public string? Details { get; set; }

        public DateTime SavedUtc { get; set; }
    }
}
