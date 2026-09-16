using System;

namespace TrainerScheduler.Data.Entities
{
    public sealed class ServerMetaEntity
    {
        public int Id { get; set; } = 1;
        public long Revision { get; set; } = 1;
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    }
}
