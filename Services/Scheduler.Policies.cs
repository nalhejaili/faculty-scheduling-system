using System;
using System.Collections.Generic;
using System.Linq;
using MiniTrainerScheduler.Models;

namespace MiniTrainerScheduler.Services
{
    public static partial class Scheduler
    {
        private static bool IsGeneralStudiesDept(DataStore store, int departmentId)
        {
            var rawName = store.Departments.FirstOrDefault(d => d.Id == departmentId)?.Name ?? string.Empty;
            var compact = rawName.Replace(" ", string.Empty);

            return compact.Contains("\u0639\u0627\u0645")
                   || compact.Contains("\u0627\u0644\u0639\u0627\u0645\u0629")
                   || compact.Contains("\u0639\u0627\u0645\u0647")
                   || rawName.Contains("General", StringComparison.OrdinalIgnoreCase)
                   || rawName.Contains("General Studies", StringComparison.OrdinalIgnoreCase)
                   || rawName.Contains("Common Studies", StringComparison.OrdinalIgnoreCase)
                   || rawName.Contains("University Requirements", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// </summary>
        private static IEnumerable<int> CandidateFaculties(DataStore db, Course c, int? preferredFacultyId)
        {
            if (db is null) throw new ArgumentNullException(nameof(db));
            if (c is null) throw new ArgumentNullException(nameof(c));

            var yielded = new HashSet<int>();

            if (preferredFacultyId is int pf && db.Faculties.Any(f => f.Id == pf))
            {
                yielded.Add(pf);
                yield return pf;
            }

            var overrides = (db.CourseFacultyOverrides ?? new List<CourseFacultyOverride>())
                .Where(o => o.CourseId == c.Id)
                .Select(o => o.FacultyId)
                .Distinct()
                .ToList();

            if (overrides.Count > 0)
            {
                foreach (var fid in overrides)
                {
                    if (yielded.Add(fid))
                        yield return fid;
                }

                yield break;
            }

            foreach (var id in db.Faculties
                                 .Where(f => f.DepartmentId == c.DepartmentId)
                                 .Select(f => f.Id))
            {
                if (yielded.Add(id))
                    yield return id;
            }

            if (c.IsGeneralCourse || IsGeneralStudiesDept(db, c.DepartmentId))
            {
                foreach (var id in db.Faculties.Select(f => f.Id))
                {
                    if (yielded.Add(id))
                        yield return id;
                }
            }
        }
    }
}
