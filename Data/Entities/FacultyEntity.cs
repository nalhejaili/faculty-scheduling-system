namespace TrainerScheduler.Data.Entities
{
    public sealed class FacultyEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";

        public int DepartmentId { get; set; }
        public bool IsGeneralStudies { get; set; }
    }
}
