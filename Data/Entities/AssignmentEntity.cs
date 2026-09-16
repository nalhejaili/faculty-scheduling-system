using System;

namespace TrainerScheduler.Data.Entities
{
    /// <summary>
    /// Persisted scheduled assignment row (generated result).
    /// We keep it flat and avoid FKs for robustness across partial datasets.
    /// </summary>
    public sealed class AssignmentEntity
    {
        public int Id { get; set; }

        public int DepartmentId { get; set; }
        public int CourseId { get; set; }
        public int SectionIndex { get; set; }
        public int SlotId { get; set; }
        public int FacultyId { get; set; }
        public int? RoomId { get; set; }

        public string Status { get; set; } = string.Empty;
        public string Kind { get; set; } = string.Empty;

        public DateTime SavedUtc { get; set; }
    }
}
