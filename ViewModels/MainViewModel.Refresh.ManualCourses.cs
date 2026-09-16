using System.Linq;

namespace MiniTrainerScheduler.ViewModels
{
    public sealed partial class MainViewModel
    {
        void RefreshManualCourses()
        {
            ManualCourses.Clear();
            if (ManualSelectedDepartment is null || ManualSelectedLevel is null) { RaiseAllCanExec(); return; }

            foreach (var c in Store.Courses
                     .Where(c => c.DepartmentId == ManualSelectedDepartment.Id && c.Level == ManualSelectedLevel))
                ManualCourses.Add(c);

            ManualSelectedCourse = ManualCourses.FirstOrDefault();
            RaiseAllCanExec();
        }
    }
}
