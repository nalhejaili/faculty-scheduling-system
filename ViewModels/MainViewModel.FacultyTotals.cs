using System.Collections.ObjectModel;
using System.Linq;

namespace MiniTrainerScheduler.ViewModels
{
    public sealed partial class MainViewModel
    {
        public sealed class FacultyTotalRow
        {
            public int FacultyId { get; init; }
            public string Faculty { get; init; } = "";
            public int TotalHours { get; init; }
        }

        public ObservableCollection<FacultyTotalRow> FacultyTotals { get; } = new();

        void RefreshFacultyTotalsByCourseLoad()
        {
            FacultyTotals.Clear();

            int? lockedDeptId = IsSupervisor ? GetSupervisorDepartment()?.Id : null;

            var courseHours = Store.Courses.ToDictionary(c => c.Id, c => c.HoursPerWeek);

            var distinctSections = Store.Assignments
                .Where(a => a.FacultyId > 0 && (lockedDeptId is null || a.DepartmentId == lockedDeptId.Value))
                .Select(a => new { a.FacultyId, a.DepartmentId, a.CourseId, a.SectionIndex })
                .Distinct();

            var totals = distinctSections
                .GroupBy(k => k.FacultyId)
                .Select(g => new FacultyTotalRow
                {
                    FacultyId = g.Key,
                    Faculty = Store.Faculties.FirstOrDefault(f => f.Id == g.Key)?.Name ?? g.Key.ToString(),
                    TotalHours = g.Sum(k => (int)System.Math.Round(System.Convert.ToDouble(
                        courseHours.TryGetValue(k.CourseId, out var h) ? h : 0)))
                })
                .OrderByDescending(r => r.TotalHours);

            foreach (var row in totals)
                FacultyTotals.Add(row);
        }
    }
}
