using System.Linq;

namespace MiniTrainerScheduler.ViewModels
{
    public sealed partial class MainViewModel
    {
        void SyncOverrideRowsFromStore()
        {
            // Faculty -> Department overrides
            FdoRows.Clear();
            foreach (var o in Store.FacultyDeptOverrides)
            {
                var f = Store.Faculties.FirstOrDefault(x => x.Id == o.FacultyId);
                var d = Store.Departments.FirstOrDefault(x => x.Id == o.DepartmentId);
                FdoRows.Add(new FacultyDeptOverrideRow
                {
                    FacultyId = o.FacultyId,
                    FacultyName = f?.Name ?? o.FacultyId.ToString(),
                    DepartmentId = o.DepartmentId,
                    DepartmentName = d?.Name ?? o.DepartmentId.ToString()
                });
            }

            // Course -> Faculty overrides (CourseId + CourseCode)
            CfoRows.Clear();

            // 1) CourseId-level
            foreach (var o in Store.CourseFacultyOverrides)
            {
                var c = Store.Courses.FirstOrDefault(x => x.Id == o.CourseId);
                var f = Store.Faculties.FirstOrDefault(x => x.Id == o.FacultyId);
                var dept = c is null ? null : Store.Departments.FirstOrDefault(d => d.Id == c.DepartmentId);

                CfoRows.Add(new CourseFacultyOverrideRow
                {
                    Kind = "Course",
                    CourseId = o.CourseId,
                    CourseCode = c?.CourseCode ?? "",
                    ScopeDepartmentId = c?.DepartmentId ?? 0,
                    ScopeDepartmentName = dept?.Name ?? "",
                    CourseName = c?.Name ?? o.CourseId.ToString(),
                    FacultyId = o.FacultyId,
                    FacultyName = f?.Name ?? o.FacultyId.ToString()
                });
            }

            // 2) CourseCode-level (scoped)
            foreach (var o in Store.CourseCodeFacultyOverrides)
            {
                var f = Store.Faculties.FirstOrDefault(x => x.Id == o.FacultyId);
                var dept = o.ScopeDepartmentId <= 0 ? null : Store.Departments.FirstOrDefault(d => d.Id == o.ScopeDepartmentId);

                var scopeName = o.ScopeDepartmentId <= 0 ? "All Departments" : (dept?.Name ?? o.ScopeDepartmentId.ToString());
                var code = o.CourseCode ?? "";

                CfoRows.Add(new CourseFacultyOverrideRow
                {
                    Kind = "Code",
                    CourseId = 0,
                    CourseCode = code,
                    ScopeDepartmentId = o.ScopeDepartmentId,
                    ScopeDepartmentName = scopeName,
                    CourseName = $"By Code: {code} ({scopeName})",
                    FacultyId = o.FacultyId,
                    FacultyName = f?.Name ?? o.FacultyId.ToString()
                });
            }
        }
    }
}
