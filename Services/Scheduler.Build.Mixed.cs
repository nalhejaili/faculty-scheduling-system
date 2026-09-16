using System;
using System.Collections.Generic;
using System.Linq;
using MiniTrainerScheduler.Models;

namespace MiniTrainerScheduler.Services
{
    public static partial class Scheduler
    {
        public sealed class MixedPick
        {
            public Course Course { get; }
            public int Section { get; }
            public double Hours { get; }
            public bool IsLab { get; }
            public int? ForcedFacultyId { get; }
            public int? ForcedSlotId { get; }
            public int? ForcedRoomId { get; }
            /// <summary>
            /// </summary>
            public IReadOnlyCollection<int>? PreferredSlotIds { get; }
            public bool IsManual { get; }
            public bool PreferSameInstructor { get; }

            public bool SkipAutoComplement { get; }

            public MixedPick(
                Course course,
                int section,
                double hours,
                bool isLab,
                int? forcedFacultyId,
                int? forcedSlotId,
                int? forcedRoomId,
                IReadOnlyCollection<int>? preferredSlotIds,
                bool isManual,
                bool preferSameInstructor,
                bool skipAutoComplement = false)
            {
                Course = course;
                Section = section;
                Hours = hours;
                IsLab = isLab;
                ForcedFacultyId = forcedFacultyId;
                ForcedSlotId = forcedSlotId;
                ForcedRoomId = forcedRoomId;
                PreferredSlotIds = preferredSlotIds;
                IsManual = isManual;
                PreferSameInstructor = preferSameInstructor;
                SkipAutoComplement = skipAutoComplement;
            }
        }

        public static class Build
        {
            public static void Mixed(
                DataStore store,
                ScheduleKind kind,
                IEnumerable<MixedPick> picksInput)
            {
                if (store is null) throw new ArgumentNullException(nameof(store));
                if (picksInput is null) picksInput = Enumerable.Empty<MixedPick>();

                var manualAssignments = store.Assignments
                    .Where(a => string.Equals(a.Status, "MANUAL", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                store.Assignments.Clear();
                store.PlanningIssues.Clear();
                const int MAX_PLANNING_ISSUES = 300;

                var picks = new List<MixedPick>();

                foreach (var grp in picksInput.GroupBy(p => (p.Course.Id, p.Section)))
                {
                    var p0 = grp.First();
                    var c = p0.Course;

                    double thIn = grp.Where(x => !x.IsLab).Sum(x => x.Hours);
                    double labIn = grp.Where(x => x.IsLab).Sum(x => x.Hours);

                    // (theoryHours, labHours, preferSameTeacher)
                    var split = SectionPlanner.GetHoursSplit(store, c);
                    double th = split.theoryHours;
                    double lab = split.labHours;
                    bool preferSame = split.preferSameTeacher;

                    picks.AddRange(grp);

                    bool skipAuto = grp.Any(x => x.SkipAutoComplement || x.IsManual);

                    if (!skipAuto)
                    {
                        if (th > 0 && thIn <= 0)
                        {
                            picks.Add(new MixedPick(
                                c,
                                p0.Section,
                                th,
                                isLab: false,
                                p0.ForcedFacultyId,
                                p0.ForcedSlotId,
                                p0.ForcedRoomId,
                                p0.PreferredSlotIds,
                                p0.IsManual,
                                preferSame));
                        }

                        if (lab > 0 && labIn <= 0)
                        {
                            picks.Add(new MixedPick(
                                c,
                                p0.Section,
                                lab,
                                isLab: true,
                                p0.ForcedFacultyId,
                                p0.ForcedSlotId,
                                p0.ForcedRoomId,
                                p0.PreferredSlotIds,
                                p0.IsManual,
                                preferSame));
                        }
                    }

                    if (!skipAuto && thIn <= 0 && labIn <= 0 && th <= 0 && lab <= 0)
                    {
                        picks.Add(new MixedPick(
                            c,
                            p0.Section,
                            c.HoursPerWeek,
                            isLab: false,
                            p0.ForcedFacultyId,
                            p0.ForcedSlotId,
                            p0.ForcedRoomId,
                                p0.PreferredSlotIds,
                            p0.IsManual,
                            preferSameInstructor: true));
                    }
                }

                var slots = store.Slots
                    .OrderBy(s => DayOrder(s.Day))
                    .ThenBy(s => s.Start)
                    .ToList();

                var slotById = slots.ToDictionary(s => s.Id);

                picks = picks
                    .OrderByDescending(p => p.IsManual || p.ForcedFacultyId.HasValue || p.ForcedSlotId.HasValue || p.ForcedRoomId.HasValue)
                    .ThenByDescending(p => p.Hours)
                    .ThenBy(p => p.Course.Id)
                    .ThenBy(p => p.Section)
                    .ThenBy(p => p.IsLab ? 1 : 0)
                    .ToList();

                var rooms = store.Rooms.ToList();
                var roomSlotBusy = new HashSet<(int roomId, int slotId)>();
                bool RoomFree(int roomId, int slotId) => !roomSlotBusy.Contains((roomId, slotId));

                var load = store.Faculties.ToDictionary(f => f.Id, _ => 0.0);

                var rng = new Random(unchecked(Environment.TickCount * 31 + DateTime.Now.Millisecond));
                var facultyRandomOffset = store.Faculties.ToDictionary(f => f.Id, _ => rng.NextDouble());

                var maxByFaculty = store.FacultyLoadOverrides
                    .ToDictionary(x => x.FacultyId, x => (double)x.MaxHours);

                var facByDept = store.Faculties
                    .GroupBy(f => f.DepartmentId)
                    .ToDictionary(g => g.Key, g => g.Select(f => f.Id).ToList());

                var facById = store.Faculties.ToDictionary(f => f.Id);

                var allowedSlotsByCourse = store.CourseSlotOverrides
                    .GroupBy(o => o.CourseId)
                    .ToDictionary(
                        g => g.Key,
                        g => g.Select(o => o.SlotId).ToHashSet());

                var facultyBlockedSlots = new HashSet<(int facultyId, int slotId)>();

                foreach (var b in store.FacultySlotBlocks)
                {
                    facultyBlockedSlots.Add((b.FacultyId, b.SlotId));
                }
                var facultySlotBusy = new HashSet<(int facultyId, int slotId)>();

                bool IsFacultyFreeAt(int facultyId, int slotId)
                    => !facultySlotBusy.Contains((facultyId, slotId))
                       && !facultyBlockedSlots.Contains((facultyId, slotId));

                bool IsSlotAllowedForCourse(Course c, Slot s)
                    => allowedSlotsByCourse.Count == 0
                       || !allowedSlotsByCourse.TryGetValue(c.Id, out var set)
                       || set.Count == 0
                       || set.Contains(s.Id);

                double minSlotHours = Math.Max(
                    1.0,
                    slots.Select(s => (s.End - s.Start).TotalHours)
                         .DefaultIfEmpty(1.0)
                         .Min());

                int TwoHourBlockSlots()
                    => Math.Max(1, (int)Math.Round(2.0 / minSlotHours));

                var preferredFacultyForPair =
                    new Dictionary<(int courseId, int section), int>();

                var theoryFacultyOrderByCourse = new Dictionary<int, List<int>>();
                var extraLabRoundRobinIndexByCourse = new Dictionary<int, int>();

                var cohortBusy = new HashSet<(int deptId, int level, int section, int slotId)>();
                bool CohortFree(int deptId, int level, int section, int slotId)
                    => !cohortBusy.Contains((deptId, level, section, slotId));
                void ReserveCohort(int deptId, int level, int section, int slotId)
                    => cohortBusy.Add((deptId, level, section, slotId));

                var courseById = store.Courses.ToDictionary(c => c.Id);

                foreach (var ma in manualAssignments)
                {
                    store.Assignments.Add(ma);

                    if (slotById.TryGetValue(ma.SlotId, out var ms))
                    {
                        facultySlotBusy.Add((ma.FacultyId, ma.SlotId));

                        if (load.ContainsKey(ma.FacultyId))
                            load[ma.FacultyId] += (ms.End - ms.Start).TotalHours;

                        if (ma.RoomId.HasValue)
                            roomSlotBusy.Add((ma.RoomId.Value, ma.SlotId));

                        if (courseById.TryGetValue(ma.CourseId, out var mc))
                            ReserveCohort(mc.DepartmentId, mc.Level, ma.SectionIndex, ma.SlotId);
                        else
                            ReserveCohort(ma.DepartmentId, level: 0, ma.SectionIndex, ma.SlotId);
                    }
                }

                foreach (var pick in picks)
                {
                    var course = pick.Course;

                    int totalUnitsNeeded =
                        kind == ScheduleKind.Finals
                            ? Math.Max(
                                1,
                                (int)Math.Round(2.0 / minSlotHours))
                            : Math.Max(
                                1,
                                (int)Math.Ceiling(pick.Hours / minSlotHours));

                    int manualUnitsForPick = manualAssignments.Count(a =>
                        a.CourseId == course.Id &&
                        a.SectionIndex == pick.Section &&
                        string.Equals(
                            a.Kind,
                            pick.IsLab ? "LAB" : "THEORY",
                            StringComparison.OrdinalIgnoreCase));

                    int unitsToPlace = totalUnitsNeeded - manualUnitsForPick;

                    if (unitsToPlace <= 0)
                        continue;

                    int blockSlots = TwoHourBlockSlots();

                    bool placed = false;
                    bool allowCross = pick.IsManual;

string NormalizeCourseCodeLocal(string? code)
{
    if (string.IsNullOrWhiteSpace(code)) return string.Empty;
    return code.Trim().ToUpperInvariant();
}

var fixedFacultyIdsByCourseId = store.CourseFacultyOverrides
    .Where(o => o.CourseId == course.Id)
    .Select(o => o.FacultyId)
    .Where(id => id > 0 && facById.ContainsKey(id))
    .Distinct()
    .ToHashSet();

string courseCodeNorm = NormalizeCourseCodeLocal(course.CourseCode);

var fixedFacultyIdsByCode = new HashSet<int>();
string fixedCodeScopeLabel = string.Empty;

if (!string.IsNullOrEmpty(courseCodeNorm))
{
    var deptScoped = store.CourseCodeFacultyOverrides
        .Where(o => NormalizeCourseCodeLocal(o.CourseCode) == courseCodeNorm && o.ScopeDepartmentId == course.DepartmentId)
        .Select(o => o.FacultyId)
        .Where(id => id > 0 && facById.ContainsKey(id))
        .Distinct()
        .ToList();

    var global = store.CourseCodeFacultyOverrides
        .Where(o => NormalizeCourseCodeLocal(o.CourseCode) == courseCodeNorm && o.ScopeDepartmentId == 0)
        .Select(o => o.FacultyId)
        .Where(id => id > 0 && facById.ContainsKey(id))
        .Distinct()
        .ToList();

    var chosen = deptScoped.Count > 0 ? deptScoped : global;

    if (chosen.Count > 0)
    {
        fixedFacultyIdsByCode = chosen.ToHashSet();
        fixedCodeScopeLabel = deptScoped.Count > 0 ? "Scope: same department" : "Scope: all departments";
    }
}

var fixedFacultyIds = fixedFacultyIdsByCourseId.Count > 0 ? fixedFacultyIdsByCourseId : fixedFacultyIdsByCode;

bool fixedByCourseId = fixedFacultyIdsByCourseId.Count > 0;
bool fixedByCode = !fixedByCourseId && fixedFacultyIdsByCode.Count > 0;

bool hasFixedFaculty = fixedFacultyIds.Count > 0;

IEnumerable<int> CandidateFaculties(Course c, int? forcedFacultyId)
                    {
                        var yielded = new HashSet<int>();


                        if (hasFixedFaculty)
                        {
                            if (forcedFacultyId is int pff && fixedFacultyIds.Contains(pff) && yielded.Add(pff))
                                yield return pff;

                            foreach (var id in fixedFacultyIds.OrderBy(x => x))
                            {
                                if (yielded.Add(id))
                                    yield return id;
                            }

                            yield break;
                        }

                        if (forcedFacultyId is int pf && facById.ContainsKey(pf))
                        {
                            yielded.Add(pf);
                            yield return pf;
                        }

                        if (facByDept.TryGetValue(c.DepartmentId, out var list))
                        {
                            foreach (var id in list)
                            {
                                if (yielded.Add(id))
                                    yield return id;
                            }
                        }

                        if (c.IsGeneralCourse)
                        {
                            foreach (var id in store.Faculties.Select(f => f.Id))
                            {
                                if (yielded.Add(id))
                                    yield return id;
                            }
                        }
                    }

                    IEnumerable<int> OrderedWithPreferred(IEnumerable<int> ids)
                    {
                        var baseOrder = ids;

                        if (pick.PreferSameInstructor
                            && preferredFacultyForPair.TryGetValue(
                                (course.Id, pick.Section),
                                out var pfid))
                        {
                            baseOrder = new[] { pfid }
                                .Concat(ids.Where(x => x != pfid));
                        }

                        return baseOrder;
                    }

                    List<int> candidates;

                    // NOTE: CandidateFaculties signature is (Course c, int? forcedFacultyId)
                    candidates = CandidateFaculties(course, forcedFacultyId: null)
                        .Distinct()
                        .Where(fid =>
                        {
                            if (!maxByFaculty.ContainsKey(fid)) return true;
                            return load[fid] < maxByFaculty[fid] - 1e-6;
                        })
                        .Where(fid =>
                        {
                            if (hasFixedFaculty) return true;

                            if (allowCross) return true;

                            var f = facById[fid];

                            bool facultyIsGeneral =
                                f.IsGeneralStudies || IsGeneralStudiesDept(store, f.DepartmentId);

                            bool courseIsGeneral =
                                course.IsGeneralCourse || IsGeneralStudiesDept(store, course.DepartmentId);

                            if (courseIsGeneral)
                                return facultyIsGeneral;

                            if (facultyIsGeneral)
                                return false;

                            return f.DepartmentId == course.DepartmentId;
                        })
                        .OrderBy(fid =>
                        {
                            var baseLoad = load.TryGetValue(fid, out var l) ? l : 0.0;
                            var offset = facultyRandomOffset.TryGetValue(fid, out var r) ? r : 0.0;
                            return baseLoad + offset * 0.01;
                        })
                        .ToList();

var preferredFaculty = pick.ForcedFacultyId;

if (hasFixedFaculty && preferredFaculty is int pfFixed && !fixedFacultyIds.Contains(pfFixed))
    preferredFaculty = null;

if (preferredFaculty is int pf && facById.ContainsKey(pf))
{
    if (!hasFixedFaculty || fixedFacultyIds.Contains(pf))
    {
        candidates.Remove(pf);
        candidates.Insert(0, pf);
    }
}
                    if (pick.SkipAutoComplement)
                    {
                        if (theoryFacultyOrderByCourse.TryGetValue(course.Id, out var theoryFacs)
                            && theoryFacs.Count > 0)
                        {
                            extraLabRoundRobinIndexByCourse.TryGetValue(course.Id, out var startIndex);

                            var reordered = new List<int>();
                            int idx = startIndex;
                            int attempts = 0;

                            while (attempts < theoryFacs.Count)
                            {
                                var fid = theoryFacs[idx % theoryFacs.Count];
                                if (candidates.Contains(fid) && !reordered.Contains(fid))
                                {
                                    reordered.Add(fid);
                                }

                                idx++;
                                attempts++;
                            }

                            foreach (var fid in candidates)
                            {
                                if (!reordered.Contains(fid))
                                    reordered.Add(fid);
                            }

                            candidates = reordered;

                            extraLabRoundRobinIndexByCourse[course.Id] =
                                (startIndex + 1) % theoryFacs.Count;
                        }
                    }

                    candidates = OrderedWithPreferred(candidates).ToList();

                    var feasible = new List<Slot>();

                    var preferredSlotsSet = (pick.PreferredSlotIds != null && pick.PreferredSlotIds.Count > 0)
                        ? pick.PreferredSlotIds.ToHashSet()
                        : null;

                    bool usedFallbackForPreferredSlots = false;
                    bool usedFallbackForPreferredRoom = false;
                    bool usedFallbackForPreferredFaculty = false;

                    foreach (var facultyId in candidates)
                    {
                        feasible = slots
                            .Where(s =>
                                IsSlotAllowedForCourse(course, s)
                                && IsFacultyFreeAt(facultyId, s.Id)
                                && CohortFree(course.DepartmentId, course.Level, pick.Section, s.Id))
                            .ToList();

                        int? preferredStart = pick.ForcedSlotId;

                        List<Slot>? exactPreferred = null;
                        if (preferredSlotsSet != null)
                        {
                            var prefSlots = feasible.Where(s => preferredSlotsSet.Contains(s.Id)).ToList();
                            if (prefSlots.Count == unitsToPlace)
                            {
                                exactPreferred = prefSlots
                                    .OrderBy(s => DayOrder(s.Day))
                                    .ThenBy(s => s.Start)
                                    .ToList();
                            }

                            if (!preferredStart.HasValue && prefSlots.Count > 0)
                                preferredStart = prefSlots[0].Id;
                        }

                        List<Slot> chosen;

                        if (exactPreferred != null)
                        {
                            chosen = exactPreferred;
                        }
                        else
                        {
                            var feasiblePreferred = preferredSlotsSet != null
                                ? feasible.Where(s => preferredSlotsSet.Contains(s.Id)).ToList()
                                : feasible;

                            chosen = PickSlotsTwoHours_OneBlockPerDay(
                                feasiblePreferred,
                                unitsToPlace,
                                blockSlots,
                                preferredStart);

                            if (chosen.Count != unitsToPlace && preferredSlotsSet != null)
                            {
                                usedFallbackForPreferredSlots = true;
                                chosen = PickSlotsTwoHours_OneBlockPerDay(
                                    feasible,
                                    unitsToPlace,
                                    blockSlots,
                                    preferredStart);
                            }

                            if (chosen.Count != unitsToPlace
                                && Math.Round(pick.Hours) >= 4.0 - 1e-6)
                            {
                                var block4h = TryTakeFixedHourBlockAnyDay(
                                    (preferredSlotsSet != null && !usedFallbackForPreferredSlots)
                                        ? feasiblePreferred
                                        : feasible,
                                    4.0,
                                    minSlotHours);

                                if (block4h.Count == unitsToPlace)
                                    chosen = block4h;
                            }

                            if (chosen.Count != unitsToPlace)
                            {
                                var singles = TryTakeSinglesLatestFirst(
                                    (preferredSlotsSet != null && !usedFallbackForPreferredSlots)
                                        ? feasiblePreferred
                                        : feasible,
                                    unitsToPlace);

                                if (singles.Count == unitsToPlace)
                                    chosen = singles;
                            }
                        }

                        if (maxByFaculty.TryGetValue(facultyId, out var limitHours))
                        {
                            double addHours =
                                chosen.Sum(s => (s.End - s.Start).TotalHours);

                            if (load[facultyId] + addHours > limitHours + 1e-6)
                                continue;
                        }

                        if (chosen.Count == unitsToPlace)
                        {
                            if (preferredFaculty is int pf2 && pf2 != facultyId)
                                usedFallbackForPreferredFaculty = true;

                            foreach (var s in chosen)
                                ReserveCohort(course.DepartmentId, course.Level, pick.Section, s.Id);

                            int? preferredRoomId = pick.ForcedRoomId;
                            if (preferredRoomId is int pr && pr <= 0) preferredRoomId = null;

                            if (preferredRoomId.HasValue && !rooms.Any(r => r.Id == preferredRoomId.Value))
                            {
                                usedFallbackForPreferredRoom = true;
                                preferredRoomId = null;
                            }

                            var deptRoomIds = rooms
                                .Where(r => r.DepartmentId.HasValue && r.DepartmentId.Value == course.DepartmentId)
                                .Select(r => r.Id)
                                .Distinct()
                                .ToList();

                            if (preferredRoomId.HasValue && !deptRoomIds.Contains(preferredRoomId.Value))
                            {
                                usedFallbackForPreferredRoom = true;
                                preferredRoomId = null;
                            }

                            IEnumerable<int> RoomOrder()
                            {
                                if (preferredRoomId.HasValue)
                                    yield return preferredRoomId.Value;

                                foreach (var rid in deptRoomIds)
                                    if (!preferredRoomId.HasValue || rid != preferredRoomId.Value)
                                        yield return rid;
                            }

                            int? singleRoomId = null;
                            if (chosen.Count > 0 && rooms.Count > 0)
                            {
                                foreach (var rid in RoomOrder())
                                {
                                    if (chosen.All(x => RoomFree(rid, x.Id)))
                                    {
                                        singleRoomId = rid;
                                        break;
                                    }
                                }

                                if (preferredRoomId.HasValue && singleRoomId.HasValue && singleRoomId.Value != preferredRoomId.Value)
                                    usedFallbackForPreferredRoom = true;
                            }

                            foreach (var s in chosen)
                            {
                                int? roomId = singleRoomId;

                                if (!roomId.HasValue)
                                {
                                    foreach (var rid in RoomOrder())
                                    {
                                        if (RoomFree(rid, s.Id))
                                        {
                                            roomId = rid;
                                            break;
                                        }
                                    }
                                }

                                if (preferredRoomId.HasValue && roomId != preferredRoomId)
                                    usedFallbackForPreferredRoom = true;

                                store.Assignments.Add(new Assignment(
                                    course.DepartmentId,
                                    course.Id,
                                    pick.Section,
                                    s.Id,
                                    facultyId,
                                    roomId,
                                    status: "OK",
                                    kind: pick.IsLab ? "LAB" : "THEORY"));

                                facultySlotBusy.Add((facultyId, s.Id));

                                if (roomId.HasValue)
                                    roomSlotBusy.Add((roomId.Value, s.Id));

                                load[facultyId] +=
                                    (s.End - s.Start).TotalHours;
                            }

                            if (pick.PreferSameInstructor)
                            {
                                preferredFacultyForPair[(course.Id, pick.Section)] =
                                    facultyId;
                            }

                            if (!pick.IsLab)
                            {
                                if (!theoryFacultyOrderByCourse.TryGetValue(course.Id, out var list))
                                {
                                    list = new List<int>();
                                    theoryFacultyOrderByCourse[course.Id] = list;
                                }

                                if (!list.Contains(facultyId))
                                    list.Add(facultyId);
                            }

                            placed = true;

                            if (store.PlanningIssues.Count < MAX_PLANNING_ISSUES)
                            {
                                if (usedFallbackForPreferredFaculty)
                                {
                                    store.PlanningIssues.Add(new PlanningIssue
                                    {
                                        Severity = IssueSeverity.Info,
                                        Code = IssueCode.Other,
                                        DepartmentId = course.DepartmentId,
                                        Level = course.Level,
                                        CourseId = course.Id,
                                        SectionIndex = pick.Section,
                                        Message =
                                            $"The faculty preference for course \"{course.Name}\" {(pick.IsLab ? "(Lab)" : "(Lecture)")} (section {pick.Section}) was overridden, and an alternative faculty member was assigned automatically.",
                                        Context =
                                            $"SoftPref:Faculty;Dept={course.DepartmentId};Level={course.Level};Course={course.Id};Section={pick.Section}"
                                    });
                                }

                                if (usedFallbackForPreferredSlots)
                                {
                                    store.PlanningIssues.Add(new PlanningIssue
                                    {
                                        Severity = IssueSeverity.Info,
                                        Code = IssueCode.Other,
                                        DepartmentId = course.DepartmentId,
                                        Level = course.Level,
                                        CourseId = course.Id,
                                        SectionIndex = pick.Section,
                                        Message =
                                            $"The time-slot preference for course \"{course.Name}\" {(pick.IsLab ? "(Lab)" : "(Lecture)")} (section {pick.Section}) was overridden, and an alternative time slot was assigned automatically.",
                                        Context =
                                            $"SoftPref:Slot;Dept={course.DepartmentId};Level={course.Level};Course={course.Id};Section={pick.Section}"
                                    });
                                }

                                if (usedFallbackForPreferredRoom)
                                {
                                    store.PlanningIssues.Add(new PlanningIssue
                                    {
                                        Severity = IssueSeverity.Info,
                                        Code = IssueCode.Other,
                                        DepartmentId = course.DepartmentId,
                                        Level = course.Level,
                                        CourseId = course.Id,
                                        SectionIndex = pick.Section,
                                        Message =
                                            $"The room preference for course \"{course.Name}\" {(pick.IsLab ? "(Lab)" : "(Lecture)")} (section {pick.Section}) was overridden, and an alternative room was assigned automatically.",
                                        Context =
                                            $"SoftPref:Room;Dept={course.DepartmentId};Level={course.Level};Course={course.Id};Section={pick.Section}"
                                    });
                                }

                            }

                            break;
                        }
                    }

                    if (!placed)
                    {
                        var allowedSlots = slots
                            .Where(s => IsSlotAllowedForCourse(course, s))
                            .ToList();

                        string reason;

                        if (allowedSlots.Count == 0)
                        {
                            reason =
                                "No time slots are allowed for this course under the current constraints.";
                        }
                        else
{
    if (candidates.Count == 0)
    {
        reason = hasFixedFaculty
            ? "No faculty member is currently available from the locked assignment list (possibly due to workload limits or other constraints)."
            : "No candidate faculty member is currently available (possibly due to workload limits or other constraints).";
    }
    else
    {
        int freeSlotsCount = allowedSlots.Count(s =>
            candidates.Any(fid =>
                IsFacultyFreeAt(fid, s.Id)
                && CohortFree(course.DepartmentId, course.Level, pick.Section, s.Id)));

        reason = freeSlotsCount == 0
            ? "All allowed time slots conflict with either the faculty member or the cohort schedule."
            : "No suitable consecutive block was found to satisfy the two-hours-per-day requirement.";
    }

    if (hasFixedFaculty)
{
    var fixedNames = string.Join(", ",
        fixedFacultyIds.Select(fid =>
            facById.TryGetValue(fid, out var f) ? f.Name : fid.ToString()));

    if (fixedByCode)
    {
        var codeLabel = string.IsNullOrEmpty(courseCodeNorm) ? "unspecified code" : $"code {courseCodeNorm}";
        var scopeLabel = string.IsNullOrEmpty(fixedCodeScopeLabel) ? "" : $" ({fixedCodeScopeLabel})";
        reason = $"The course is restricted by {codeLabel}{scopeLabel} to the following faculty members: {fixedNames}. {reason}";
    }
    else
    {
        reason = $"The course is restricted to the following faculty members: {fixedNames}. {reason}";
    }
}
}
if (store.PlanningIssues.Count >= MAX_PLANNING_ISSUES)
                        {
                        }
                        else
                        {
                            store.PlanningIssues.Add(new PlanningIssue
                            {
                                Severity = IssueSeverity.Warning,
                                Message =
                                    $"Unable to generate a {(pick.IsLab ? "(Lab)" : "(Lecture)")} section for course \"{course.Name}\" (level {course.Level}). Reason: {reason}",
                                Context =
                                    $"Dept={course.DepartmentId};Level={course.Level};Course={course.Id};Section={pick.Section}"
                            });
                        }
                    }
                }

                FixGeneralStudiesAssignments(store);

                if (!store.CourseHourSplits.Any())
                {
                    store.PlanningIssues.Add(new PlanningIssue
                    {
                        Severity = IssueSeverity.Info,
                        Message =
                            "Notice: no CourseHourSplits were loaded from JSON; all courses will be treated as lecture-based unless configured manually.",
                        Context = "ImportCourseHourSplits"
                    });
                }
            }
        }
    }
}
