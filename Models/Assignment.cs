using System;

namespace MiniTrainerScheduler.Models
{
    /// <summary>
    /// </summary>
    public sealed class Assignment
    {
        /// <summary>
        /// </summary>
        public int Id { get; set; }

        public int DepartmentId { get; }
        public int CourseId { get; }
        public int SectionIndex { get; }
        public int SlotId { get; }
        public int FacultyId { get; }
        public int? RoomId { get; }

        public Assignment? Source { get; set; }


        /// <summary>
        /// </summary>
        public string Status { get; set; }


        /// <summary>
        /// </summary>
        public string Kind { get; set; }

        public Assignment(
            int departmentId,
            int courseId,
            int sectionIndex,
            int slotId,
            int facultyId,
            int? roomId,
            string status,
            string kind = "")
        {
            DepartmentId = departmentId;
            CourseId = courseId;
            SectionIndex = sectionIndex;
            SlotId = slotId;
            FacultyId = facultyId;
            RoomId = roomId;
            Status = status ?? string.Empty;
            Kind = kind ?? string.Empty;
        }
    }
}
