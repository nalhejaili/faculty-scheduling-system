namespace TrainerScheduler.Data.Entities
{
    public sealed class SlotEntity
    {
        public int Id { get; set; }

        // DayOfWeek stored as int (Sunday=0 ... Saturday=6)
        public int Day { get; set; }

        // Store times as minutes-from-midnight to keep SQLite simple
        public int StartMinutes { get; set; }
        public int EndMinutes { get; set; }
    }
}
