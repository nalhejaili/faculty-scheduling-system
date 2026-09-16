using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MiniTrainerScheduler.Models
{
    public readonly record struct FacultyDeptOverride(int FacultyId, int DepartmentId);
    public readonly record struct CourseFacultyOverride(int CourseId, int FacultyId);
        public readonly record struct CourseCodeFacultyOverride(int ScopeDepartmentId, string CourseCode, int FacultyId);
public readonly record struct CourseSlotOverride(int CourseId, int SlotId);
    public sealed record FacultyLoadOverride(int FacultyId, int MaxHours);
}
