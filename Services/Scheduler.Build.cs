using System;
using System.Collections.Generic;
using System.Linq;
using MiniTrainerScheduler.Models;

namespace MiniTrainerScheduler.Services
{
    public static partial class Scheduler
    {
        public static void BuildSchedule(
            DataStore store,
            ScheduleKind kind,
            IEnumerable<(Course course, int sections, int? forcedFacultyId, int? forcedSlotId, bool isManual)> selected)
        {
            if (store is null) throw new ArgumentNullException(nameof(store));

            var manualAssignments = store.Assignments
                .Where(a => string.Equals(a.Status, "MANUAL", StringComparison.OrdinalIgnoreCase))
                .ToList();

            store.Assignments.Clear();
            store.PlanningIssues.Clear();
            const int MAX_PLANNING_ISSUES = 300;

            var slots = store.Slots
                .OrderBy(s => DayOrder(s.Day))
                .ThenBy(s => s.Start)
                .ToList();

            var slotById = slots.ToDictionary(s => s.Id);

            var plan = selected
                .OrderByDescending(x => x.course.IsGeneralCourse)
                .ThenBy(x => x.course.Level)
                .ThenBy(x => x.course.Name)
                .ToList();

            double minSlotHours = Math.Max(1.0, slots.Select(SlotHours).DefaultIfEmpty(1.0).Min());

            var rooms = store.Rooms.ToList();
            var roomSlotBusy = new HashSet<(int roomId, int slotId)>();
            bool RoomFree(int roomId, int slotId) => !roomSlotBusy.Contains((roomId, slotId));

            var load = store.Faculties.ToDictionary(f => f.Id, _ => 0.0);
            var maxByFaculty = store.FacultyLoadOverrides
                .ToDictionary(x => x.FacultyId, x => (double)x.MaxHours);

            var facultyBlockedSlots = new HashSet<(int facultyId, int slotId)>();
            foreach (var b in store.FacultySlotBlocks)
                facultyBlockedSlots.Add((b.FacultyId, b.SlotId));

            var allowedSlotsByCourse = store.CourseSlotOverrides
                .GroupBy(o => o.CourseId)
                .ToDictionary(g => g.Key, g => g.Select(o => o.SlotId).ToHashSet());

            var facById = store.Faculties.ToDictionary(f => f.Id);

            var isGeneralFacultyById = store.Faculties.ToDictionary(
                f => f.Id,
                f => f.IsGeneralStudies || IsGeneralStudiesDept(store, f.DepartmentId)
            );

            var facultySlotBusy = new HashSet<(int facultyId, int slotId)>();

            foreach (var b in store.FacultySlotBlocks)
            {
                facultySlotBusy.Add((b.FacultyId, b.SlotId));
            }
bool IsFacultyFreeAt(int facultyId, int slotId)
                => !facultySlotBusy.Contains((facultyId, slotId))
                   && !facultyBlockedSlots.Contains((facultyId, slotId));

            bool IsSlotAllowedForCourse(DataStore db, Course c, Slot s)
                => allowedSlotsByCourse.Count == 0
                   || !allowedSlotsByCourse.TryGetValue(c.Id, out var set)
                   || set.Count == 0
                   || set.Contains(s.Id);

            var nextSectionByCourse = new Dictionary<int, int>();

            var cohortBusy = new HashSet<(int deptId, int level, int section, int slotId)>();

            bool CohortFree(int deptId, int level, int section, int slotId)
                => !cohortBusy.Contains((deptId, level, section, slotId));

            void ReserveCohort(int deptId, int level, int section, int slotId)
                => cohortBusy.Add((deptId, level, section, slotId));

            foreach (var ma in manualAssignments)
            {
                if (!slotById.TryGetValue(ma.SlotId, out var slot))
                    continue;

                facultySlotBusy.Add((ma.FacultyId, ma.SlotId));
                if (load.ContainsKey(ma.FacultyId))
                    load[ma.FacultyId] += SlotHours(slot);

                if (ma.RoomId.HasValue)
                    roomSlotBusy.Add((ma.RoomId.Value, ma.SlotId));

                var courseForManual = store.Courses.FirstOrDefault(c => c.Id == ma.CourseId);
                if (courseForManual != null)
                {
                    ReserveCohort(courseForManual.DepartmentId, courseForManual.Level, ma.SectionIndex, ma.SlotId);

                    if (!nextSectionByCourse.TryGetValue(courseForManual.Id, out var currentNext)
                        || ma.SectionIndex >= currentNext)
                    {
                        nextSectionByCourse[courseForManual.Id] = ma.SectionIndex + 1;
                    }
                }

                store.Assignments.Add(ma);
            }


            foreach (var pick in plan)
            {
                var course = pick.course;
                int sectionsCount = Math.Max(1, pick.sections);

                int blockSlots = Math.Max(1, (int)Math.Round(2.0 / minSlotHours));
                int unitSlotsPerSection = kind == ScheduleKind.Finals
                    ? Math.Max(1, (int)Math.Round(2.0 / minSlotHours))
                    : Math.Max(1, (int)Math.Ceiling((double)course.HoursPerWeek / minSlotHours));

                bool courseIsGeneralLike = course.IsGeneralCourse || IsGeneralStudiesDept(store, course.DepartmentId);

                for (int it = 0; it < sectionsCount; it++)
                {
                    int section = nextSectionByCourse.TryGetValue(course.Id, out var nx) ? nx : 1;
                    nextSectionByCourse[course.Id] = section + 1;

                    bool placed = false;


                    int manualSlotsForSection = manualAssignments.Count(a =>
                        a.CourseId == course.Id &&
                        a.SectionIndex == section);

                    int remainingSlotsForSection = unitSlotsPerSection - manualSlotsForSection;

                    if (remainingSlotsForSection <= 0)
                    {
                        placed = true;
                        continue;
                    }

                    const int GeneralDeptId = 6;

                    bool isGeneralCourse = course.IsGeneralCourse;

                    List<int> candidates;

                    if (pick.isManual && pick.forcedFacultyId is int pf && facById.ContainsKey(pf))
                    {
                        candidates = new List<int> { pf };
                    }
                    else
                    {
                        IEnumerable<Faculty> query;

                        if (isGeneralCourse)
                        {
                            query = store.Faculties.Where(f => f.DepartmentId == GeneralDeptId);
                        }
                        else
                        {
                            query = store.Faculties.Where(f =>
                                f.DepartmentId == course.DepartmentId &&
                                f.DepartmentId != GeneralDeptId);
                        }

                        candidates = query
                            .Where(f =>
                            {
                                if (!maxByFaculty.TryGetValue(f.Id, out var max))
                                    return true;

                                var current = load.TryGetValue(f.Id, out var l) ? l : 0.0;
                                return current < max - 1e-6;
                            })
                            .OrderBy(f => load.TryGetValue(f.Id, out var l) ? l : 0.0)
                            .Select(f => f.Id)
                            .ToList();
                    }


                    foreach (var facultyId in candidates)
                    {
                        var feasible = slots
                            .Where(s => IsSlotAllowedForCourse(store, course, s)
                                        && IsFacultyFreeAt(facultyId, s.Id)
                                        && CohortFree(course.DepartmentId, course.Level, section, s.Id))
                            .ToList();

                        if (pick.isManual && pick.forcedSlotId is int fs)
                        {
                            feasible = feasible
                                .Where(s => s.Id == fs)
                                .ToList();
                        }

                        if (feasible.Count == 0)
                            continue;

                        int? preferredStart = pick.forcedSlotId;

                        var chosen = PickSlotsTwoHours_OneBlockPerDay(
                            feasible,
                            remainingSlotsForSection,
                            blockSlots,
                            preferredStart);

                        if (chosen.Count != remainingSlotsForSection &&
                            Math.Round((double)course.HoursPerWeek) >= 4.0 - 1e-6)
                        {
                            var block4h = TryTakeFixedHourBlockAnyDay(feasible, 4.0, minSlotHours);
                            if (block4h.Count == remainingSlotsForSection)
                                chosen = block4h;
                        }

                        if (chosen.Count != remainingSlotsForSection)
                        {
                            var singles = TryTakeSinglesLatestFirst(feasible, remainingSlotsForSection);
                            if (singles.Count == remainingSlotsForSection)
                                chosen = singles;
                        }

                        if (!pick.isManual &&
                            maxByFaculty.TryGetValue(facultyId, out var limitHours))
                        {
                            double addHours = chosen.Sum(s => SlotHours(s));
                            if (load[facultyId] + addHours > limitHours + 1e-6)
                                continue;
                        }

                        if (chosen.Count == remainingSlotsForSection)
                        {

                            if (chosen.Count != remainingSlotsForSection && Math.Round((double)course.HoursPerWeek) >= 4.0 - 1e-6)
                            {
                                var block4h = TryTakeFixedHourBlockAnyDay(feasible, 4.0, minSlotHours);
                                if (block4h.Count == remainingSlotsForSection) chosen = block4h;
                            }

                            if (chosen.Count != remainingSlotsForSection)
                            {
                                var singles = TryTakeSinglesLatestFirst(feasible, remainingSlotsForSection);
                                if (singles.Count == remainingSlotsForSection) chosen = singles;
                            }

                            if (!pick.isManual && maxByFaculty.TryGetValue(facultyId, out var facultyLimitHours))
                            {
                                double addHours = chosen.Sum(s => SlotHours(s));
                                if (load[facultyId] + addHours > facultyLimitHours + 1e-6)
                                    continue;
                            }


                            if (chosen.Count == remainingSlotsForSection)
                            {
                                foreach (var s in chosen)
                                    ReserveCohort(course.DepartmentId, course.Level, section, s.Id);

                                int deptId = course.DepartmentId;
                                var candidateRooms = rooms.Where(r => r.DepartmentId.HasValue && r.DepartmentId.Value == deptId).ToList();

                                int? singleRoomId = null;
                                if (candidateRooms.Count > 0)
                                {
                                    var canUse = candidateRooms.Select(r => r.Id)
                                                               .FirstOrDefault(rid => chosen.All(x => RoomFree(rid, x.Id)));
                                    if (canUse != 0) singleRoomId = canUse;
                                }

                                foreach (var s in chosen)
                                {
                                    if (facultyBlockedSlots.Contains((facultyId, s.Id)))
                                        continue;

                                    int? roomId = singleRoomId;

                                    if (!roomId.HasValue && candidateRooms.Count > 0)
                                    {
                                        var found = candidateRooms.Select(r => r.Id)
                                                                  .FirstOrDefault(rid => RoomFree(rid, s.Id));
                                        if (found != 0) roomId = found;
                                    }


                                    store.Assignments.Add(new Assignment(
                                        course.DepartmentId,
                                        course.Id,
                                        section,
                                        s.Id,
                                        facultyId,
                                        roomId,
                                        "OK"
                                    ));

                                    facultySlotBusy.Add((facultyId, s.Id));
                                    if (roomId.HasValue) roomSlotBusy.Add((roomId.Value, s.Id));
                                    load[facultyId] += SlotHours(s);
                                }

                                placed = true;
                                break;
                            }
                        }

                        if (!placed)
                        {
                            if (manualSlotsForSection > 0)
                                continue;

                            var allowedSlots = slots
                                .Where(s => IsSlotAllowedForCourse(store, course, s))
                                .ToList();

                            string reason;

                            if (allowedSlots.Count == 0)
                            {
                                reason = "No permitted time slots are available for this course under the current constraints.";
                            }
                            else if (candidates.Count == 0)
                            {
                                reason = pick.isManual && pick.forcedFacultyId is int ff
                                    ? $"The manually selected faculty member (ID={ff}) is missing or invalid."
                                    : "No faculty member is available, or all available faculty have reached their maximum teaching load.";
                            }
                            else
                            {
                                int freeSlotsCount = allowedSlots.Count(s =>
                                    candidates.Any(fid =>
                                        IsFacultyFreeAt(fid, s.Id) &&
                                        CohortFree(course.DepartmentId, course.Level, section, s.Id)));

                                reason = freeSlotsCount == 0
                                    ? "All permitted time slots are in conflict, either with the faculty member or with the student cohort."
                                    : "No suitable consecutive block was found for the two-hours-per-day requirement.";
                            }

                            if (store.PlanningIssues.Count < MAX_PLANNING_ISSUES)
                            {
                                store.PlanningIssues.Add(new PlanningIssue
                                {
                                    Severity = IssueSeverity.Warning,
                                    Message = $"Unable to generate a section for course \"{course.Name}\" (level {course.Level}) for section #{section}. Reason: {reason}",
                                    Context = $"Dept={course.DepartmentId};Level={course.Level};Course={course.Id};Section={section}"
                                });
                            }
                        }
                    }
                }

                foreach (var ma in manualAssignments)
                {
                    if (!store.Assignments.Contains(ma))
                        store.Assignments.Add(ma);
                }
                FixGeneralStudiesAssignments(store);


                if (store.FacultySlotBlocks.Count > 0)
                {
                    var blockedPairs = new HashSet<(int facultyId, int slotId)>(
                        store.FacultySlotBlocks.Select(b => (b.FacultyId, b.SlotId)));

                    var filtered = store.Assignments
                        .Where(a => !blockedPairs.Contains((a.FacultyId, a.SlotId)))
                        .ToList();

                    store.Assignments.Clear();
                    foreach (var a in filtered)
                        store.Assignments.Add(a);
                }
            }
        }
    }
}
