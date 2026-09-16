using System;
using System.Windows;
using MiniTrainerScheduler.Services;

namespace MiniTrainerScheduler.ViewModels
{
    public sealed partial class MainViewModel
    {
        void AddManualToQueuedPlan()
        {
            if (!CanAddManual())
            {
                MessageBox.Show("Select the department, level, and course, then specify the number of sections.", "Notice");
                return;
            }

            var course = ManualSelectedCourse!;
            var (th, lab, _) = SectionPlanner.GetHoursSplit(Store, course);

            var sections = Math.Max(1, ManualSections);

            for (int i = 0; i < sections; i++)
            {
                QueuedPlan.Add(new QueuedPlanRow
                {
                    DepartmentId = ManualSelectedDepartment!.Id,
                    DepartmentName = ManualSelectedDepartment!.Name,

                    Level = ManualSelectedLevel!.Value,

                    CourseId = course.Id,
                    CourseName = course.Name,

                    TheoryHoursRequired = th,
                    PracticalHoursRequired = lab,

                    SectionIndex = i + 1,

                    SectionsRequested = 1,

                    PreferredFacultyId = ManualSelectedFaculty?.Id,
                    PreferredSlotId = ManualSelectedSlotId
                });
            }

            RaiseAllCanExec();
        }
    }
}
