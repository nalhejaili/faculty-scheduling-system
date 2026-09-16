using System;
using System.Collections.Generic;
using System.Linq;
using MiniTrainerScheduler.Models;

namespace MiniTrainerScheduler.ViewModels
{
    public sealed partial class MainViewModel
    {
        void RestoreCourseCodeFacultyOverridesAfterImport(List<CourseCodeFacultyOverride> preserved, bool preferCurrent)
        {
            try
            {
                if (preserved == null) return;
                var store = Store;
                if (store == null) return;

                var facultyIds = store.Faculties.Select(f => f.Id).ToHashSet();

                var deptIds = store.Departments.Select(d => d.Id).ToHashSet();
                deptIds.Add(0);

                var courseCodes = store.Courses
                    .Select(c => NormalizeCourseCode(c.CourseCode ?? string.Empty))
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var current = store.CourseCodeFacultyOverrides;

                IEnumerable<CourseCodeFacultyOverride> mergedSeq =
                    preferCurrent
                        ? current.Concat(preserved)
                        : preserved.Concat(current);

                var merged = mergedSeq
                    .Select(x =>
                    {
                        var code = NormalizeCourseCode(x.CourseCode ?? string.Empty);
                        return new CourseCodeFacultyOverride(
                            ScopeDepartmentId: x.ScopeDepartmentId,
                            CourseCode: code,
                            FacultyId: x.FacultyId
                        );
                    })
                    .Where(x => x.FacultyId > 0 && facultyIds.Contains(x.FacultyId))
                    .Where(x => deptIds.Contains(x.ScopeDepartmentId))
                    .Where(x => !string.IsNullOrWhiteSpace(x.CourseCode) && courseCodes.Contains(x.CourseCode))
                    .Distinct()
                    .ToList();

                store.CourseCodeFacultyOverrides!.Clear();
                store.CourseCodeFacultyOverrides!.AddRange(merged);
            }
            catch
            {
            }
        }
    }
}
