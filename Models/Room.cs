namespace MiniTrainerScheduler.Models
{
    public sealed class Room
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";

        public int? DepartmentId { get; set; }
        public Room() { }

        public Room(int id, string name, int? departmentId = null)
        {
            Id = id;
            Name = name ?? "";
            DepartmentId = departmentId;
        }
    }
}
