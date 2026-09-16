using System;

namespace MiniTrainerScheduler.Models
{
    /// <summary>
    /// </summary>
    public sealed class FacultySlotBlock
    {
        public int FacultyId { get; set; }
        public int SlotId { get; set; }
        public string? Reason { get; set; }
    }
}
