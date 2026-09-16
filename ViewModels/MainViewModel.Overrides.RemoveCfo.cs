namespace MiniTrainerScheduler.ViewModels
{
    public sealed partial class MainViewModel
    {
        void RemoveCfo()
        {
            if (SelectedCfoRow is null) return;

            var row = SelectedCfoRow;

            // Code-level override
            if (row.IsCodeMode || (row.CourseId <= 0 && !string.IsNullOrWhiteSpace(row.CourseCode)))
            {
                static string Norm(string s) => (s ?? "").Trim().ToUpperInvariant();

                var code = Norm(row.CourseCode);
                Store.CourseCodeFacultyOverrides.RemoveAll(o =>
                    o.FacultyId == row.FacultyId &&
                    o.ScopeDepartmentId == row.ScopeDepartmentId &&
                    Norm(o.CourseCode) == code);

                CfoRows.Remove(row);
                SelectedCfoRow = null;

                SaveCourseCodeFacultyOverridesToDatabase();
                return;
            }

            // CourseId-level override
            Store.CourseFacultyOverrides.RemoveAll(o => o.CourseId == row.CourseId &&
                                                       o.FacultyId == row.FacultyId);
            CfoRows.Remove(row);
            SelectedCfoRow = null;

            SaveCourseFacultyOverridesToDatabase();
        }
    }
}
