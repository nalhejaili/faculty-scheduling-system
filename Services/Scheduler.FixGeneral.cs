using System;
using System.Collections.Generic;
using System.Linq;
using MiniTrainerScheduler.Models;

namespace MiniTrainerScheduler.Services
{
    public static partial class Scheduler
    {
        /// <summary>
        /// </summary>
        private static void FixGeneralStudiesAssignments(DataStore store)
        {
            if (store is null) return;

            const int GeneralDeptId = 6; // Legacy fallback for the General Studies department ID

            if (store.Courses is null ||
                store.Faculties is null ||
                store.Slots is null ||
                store.Assignments is null)
                return;

            var overridesByCourse = (store.CourseFacultyOverrides ?? new List<CourseFacultyOverride>())
                .GroupBy(o => o.CourseId)
                .ToDictionary(g => g.Key, g => g.Select(o => o.FacultyId).ToHashSet());

string NormalizeCourseCodeLocal(string? code)
{
    if (string.IsNullOrWhiteSpace(code)) return string.Empty;
    return code.Trim().ToUpperInvariant();
}

            var coursesById = store.Courses.ToDictionary(c => c.Id);
            var facultiesById = store.Faculties.ToDictionary(f => f.Id);
            var slotsById = store.Slots.ToDictionary(s => s.Id);

            bool IsGeneralFaculty(Faculty f)
                => f.DepartmentId == GeneralDeptId || f.IsGeneralStudies;

            var generalFaculties = store.Faculties
                .Where(IsGeneralFaculty)
                .ToList();

            if (generalFaculties.Count == 0)
                return;

            var facultySlotBusy = new HashSet<(int facultyId, int slotId)>();
            foreach (var a in store.Assignments)
            {
                facultySlotBusy.Add((a.FacultyId, a.SlotId));
            }

            var fixedAssignments = new List<Assignment>(store.Assignments.Count);

            foreach (var a in store.Assignments)
            {
                if (!coursesById.TryGetValue(a.CourseId, out var course) ||
                    !slotsById.TryGetValue(a.SlotId, out var slot) ||
                    !facultiesById.TryGetValue(a.FacultyId, out var currentFac))
                {
                    fixedAssignments.Add(a);
                    continue;
                }

                bool courseIsGeneral =
                    course.IsGeneralCourse ||
                    IsGeneralStudiesDept(store, course.DepartmentId);

                if (!courseIsGeneral)
                {
                    fixedAssignments.Add(a);
                    continue;
                }

overridesByCourse.TryGetValue(course.Id, out var allowedFacultyIds);

if ((allowedFacultyIds == null || allowedFacultyIds.Count == 0)
    && store.CourseCodeFacultyOverrides != null
    && !string.IsNullOrWhiteSpace(course.CourseCode))
{
    var codeNorm = NormalizeCourseCodeLocal(course.CourseCode);

    // Dept-scoped overrides take precedence over global ones
    var deptScoped = store.CourseCodeFacultyOverrides
        .Where(o =>
            NormalizeCourseCodeLocal(o.CourseCode) == codeNorm &&
            o.ScopeDepartmentId == course.DepartmentId)
        .Select(o => o.FacultyId)
        .Distinct()
        .ToList();

    var global = store.CourseCodeFacultyOverrides
        .Where(o =>
            NormalizeCourseCodeLocal(o.CourseCode) == codeNorm &&
            o.ScopeDepartmentId == 0)
        .Select(o => o.FacultyId)
        .Distinct()
        .ToList();

    var chosen = deptScoped.Count > 0 ? deptScoped : global;

    if (chosen.Count > 0)
        allowedFacultyIds = chosen.ToHashSet();
}
                bool currentIsGeneral = IsGeneralFaculty(currentFac);

                bool currentAllowed =
                    (allowedFacultyIds != null && allowedFacultyIds.Count > 0)
                        ? allowedFacultyIds.Contains(currentFac.Id)
                        : currentIsGeneral;

                if (currentAllowed)
                {
                    fixedAssignments.Add(a);
                    continue;
                }

                Faculty? replacement = null;

                if (allowedFacultyIds != null && allowedFacultyIds.Count > 0)
                {
                    replacement = store.Faculties
                        .Where(f =>
                            allowedFacultyIds.Contains(f.Id) &&
                            !facultySlotBusy.Contains((f.Id, a.SlotId)))
                        .OrderBy(f => f.Id)
                        .FirstOrDefault();
                }
                else
                {
                    replacement = generalFaculties
                        .Where(f => !facultySlotBusy.Contains((f.Id, a.SlotId)))
                        .OrderBy(f => f.Id)
                        .FirstOrDefault();
                }

                if (replacement is null)
                {
                    fixedAssignments.Add(a);
                    continue;
                }

                facultySlotBusy.Remove((a.FacultyId, a.SlotId));
                facultySlotBusy.Add((replacement.Id, a.SlotId));

                var replaced = new Assignment(
                    a.DepartmentId,
                    a.CourseId,
                    a.SectionIndex,
                    a.SlotId,
                    replacement.Id,
                    a.RoomId,
                    status: a.Status,
                    kind: a.Kind
                );

                fixedAssignments.Add(replaced);
            }

            store.Assignments.Clear();
            foreach (var fa in fixedAssignments)
                store.Assignments.Add(fa);
        }
    }
}
