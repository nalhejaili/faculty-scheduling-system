namespace MiniTrainerScheduler.Models
{
    /// <summary>
    /// </summary>
    public sealed class Student
    {
        public int Id { get; set; }
        public string StudentNo { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;

        public int? DepartmentId { get; set; }
        public int? Level { get; set; }
    }
}
