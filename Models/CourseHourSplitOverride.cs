namespace MiniTrainerScheduler.Models
{
    public sealed class CourseHourSplitOverride
    {
        public int CourseId { get; set; }
        public double TheoryHours { get; set; }
        public double PracticalHours { get; set; }
        public bool PreferSameInstructor { get; set; } = true;
    }
}
