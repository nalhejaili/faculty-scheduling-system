namespace TrainerScheduler.Data.Entities
{
    public sealed class CourseEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";

        public int DepartmentId { get; set; }
        public int Level { get; set; }

        public bool IsGeneralCourse { get; set; }
        public int HoursPerWeek { get; set; }

        public string? CourseCode { get; set; }
    }
}
