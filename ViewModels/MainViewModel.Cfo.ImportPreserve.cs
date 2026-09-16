using System;
using System.Collections.Generic;
using System.Linq;
using MiniTrainerScheduler.Models;

namespace MiniTrainerScheduler.ViewModels
{
    public sealed partial class MainViewModel
    {
        void RestoreCourseFacultyOverridesAfterImport(List<CourseFacultyOverride> preserved, bool preferCurrent)
        {
            try
            {
                if (preserved == null) return;
                var store = Store;
                if (store == null) return;

                var courseIds = store.Courses.Select(c => c.Id).ToHashSet();
                var facultyIds = store.Faculties.Select(f => f.Id).ToHashSet();

                var current = store.CourseFacultyOverrides;

                IEnumerable<CourseFacultyOverride> mergedSeq =
                    preferCurrent
                        ? current.Concat(preserved)
                        : preserved.Concat(current);

                var merged = mergedSeq
                    .Where(x => courseIds.Contains(x.CourseId) && facultyIds.Contains(x.FacultyId))
                    .Distinct()
                    .ToList();

                store.CourseFacultyOverrides!.Clear();
                store.CourseFacultyOverrides!.AddRange(merged);
            }
            catch
            {
            }
        }
    }
}
