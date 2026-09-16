using System;

namespace TrainerScheduler.Data.Entities
{
    /// <summary>
    /// Persisted block that prevents a faculty member from teaching in a specific slot.
    /// </summary>
    public sealed class FacultySlotBlockEntity
    {
        public int Id { get; set; }

        public int FacultyId { get; set; }
        public int SlotId { get; set; }

        public string? Reason { get; set; }

        public DateTime SavedUtc { get; set; }
    }
}
