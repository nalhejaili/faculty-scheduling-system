using MiniTrainerScheduler.Models;
using System.Linq;
using System.Windows;

namespace MiniTrainerScheduler.ViewModels
{
    public sealed partial class MainViewModel
    {
        void AddCfo()
        {
            if (SelectedOverrideFaculty is null || SelectedOverrideCourse is null)
            { MessageBox.Show("Select the faculty member and course."); return; }

            var fId = SelectedOverrideFaculty.Id;
            var cId = SelectedOverrideCourse.Id;
            if (Store.CourseFacultyOverrides.Any(o => o.CourseId == cId && o.FacultyId == fId))
            { MessageBox.Show("This assignment already exists."); return; }

            Store.CourseFacultyOverrides.Add(new CourseFacultyOverride(cId, fId));

            CfoRows.Add(new CourseFacultyOverrideRow
            {
                CourseId = cId,
                CourseName = SelectedOverrideCourse.Name,
                FacultyId = fId,
                FacultyName = SelectedOverrideFaculty.Name
            });

            SaveCourseFacultyOverridesToDatabase();
        }
    }
}
