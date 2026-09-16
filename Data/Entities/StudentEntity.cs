using System;

namespace TrainerScheduler.Data.Entities
{
    /// <summary>
    /// Persisted real student.
    /// </summary>
    public sealed class StudentEntity
    {
        public int Id { get; set; }

        public string? StudentNo { get; set; }
        public string FullName { get; set; } = string.Empty;

        public int? DepartmentId { get; set; }
        public int? Level { get; set; }

        public DateTime CreatedUtc { get; set; }
    }
}
