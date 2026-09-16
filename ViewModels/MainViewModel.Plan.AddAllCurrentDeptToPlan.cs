using System.Linq;
using System.Windows;

namespace MiniTrainerScheduler.ViewModels
{
    public sealed partial class MainViewModel
    {
        public void AddAllCurrentDeptToPlan()
        {
            if (SelectedDepartment is null || SelectedLevel is null)
            {
                MessageBox.Show("Select the department and level first.");
                return;
            }

            var baseRows = PlanRows
                .Where(r => r is not null && r.SectionIndex is null)
                .ToList();

            if (baseRows.Count == 0)
            {
                MessageBox.Show("There are no core courses in the current table to add.");
                return;
            }

            int totalCourses = 0;
            foreach (var baseRow in baseRows)
            {
                var overrides = PlanRows
                    .Where(r => r.CourseId == baseRow.CourseId && r.SectionIndex is not null)
                    .OrderBy(r => r.SectionIndex)
                    .ToList();

                AddCourseToQueuedPlanFromPlan(baseRow, overrides);
                totalCourses++;
            }

            RaiseAllCanExec();
            MessageBox.Show($"{totalCourses} course(s) have been added to the selected plan.", "Completed");
        }
    }
}
