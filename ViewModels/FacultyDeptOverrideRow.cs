namespace MiniTrainerScheduler.ViewModels
{
    public sealed class FacultyDeptOverrideRow
    {
        public int FacultyId { get; set; }
        public string FacultyName { get; set; } = "";
        public int DepartmentId { get; set; }
        public string DepartmentName { get; set; } = "";
    }
}
