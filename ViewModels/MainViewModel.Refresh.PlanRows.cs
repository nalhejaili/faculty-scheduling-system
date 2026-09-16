using System.Linq;
using MiniTrainerScheduler.Services;

namespace MiniTrainerScheduler.ViewModels
{
    public sealed partial class MainViewModel
    {
        void RefreshPlanRows()
        {
            PlanRows.CollectionChanged -= PlanRows_CollectionChanged;
            PlanRows.Clear();

            if (SelectedDepartment is null || SelectedLevel is null)
            {
                PlanRows.CollectionChanged += PlanRows_CollectionChanged;
                RaiseAllCanExec();
                return;
            }

            var courses = Store.Courses
                .Where(c => c.DepartmentId == SelectedDepartment.Id && c.Level == SelectedLevel);

            foreach (var c in courses.OrderBy(c => c.Name))
            {
                var (th, lab, _) = SectionPlanner.GetHoursSplit(Store, c);

                PlanRows.Add(new CoursePlanRow
                {
                    CourseId = c.Id,
                    CourseName = c.Name,
                    HoursPerWeek = c.HoursPerWeek,

                    IsPractical = Store.PracticalCourseIds.Contains(c.Id),

                    TheoryHoursRequired = th,
                    PracticalHoursRequired = lab,

                    SectionsRequested = 1
                });
            }

            PlanRows.CollectionChanged += PlanRows_CollectionChanged;
            RaiseAllCanExec();
        }
    }
}
