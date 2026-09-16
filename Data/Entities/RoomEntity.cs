namespace TrainerScheduler.Data.Entities
{
    public sealed class RoomEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";

        public int? DepartmentId { get; set; }
    }
}
