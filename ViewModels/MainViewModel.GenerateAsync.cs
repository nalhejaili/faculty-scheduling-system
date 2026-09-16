using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using MiniTrainerScheduler.Models;
using MiniTrainerScheduler.Services;

namespace MiniTrainerScheduler.ViewModels
{
    public sealed partial class MainViewModel
    {
        public bool IsBusy { get; private set; }

        private void SetBusy(bool busy)
        {
            if (IsBusy == busy) return;
            IsBusy = busy;
            OnPropertyChanged(nameof(IsBusy));
            RaiseAllCanExec();
        }

        public async Task GenerateAsync()
        {
            if (Store is null)
            {
                MessageBox.Show("The data store is not initialized.", "Notice");
                return;
            }
            var store = Store;

            int? lockedDeptId = null;
            if (IsSupervisor)
            {
                lockedDeptId = GetSupervisorDepartment()?.Id;
                if (lockedDeptId is null)
                {
                    MessageBox.Show(
                        "The supervisor account is not linked to any department.\n\nPlease ask the system administrator to assign a department to this account, then sign in again.",
                        "Supervisor Permissions",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }
            }


            var planRows = PlanRows?.ToList() ?? new();
            var queued = QueuedPlan?.ToList() ?? new();
            var demands = store.Demands?.ToList() ?? new();

            if (lockedDeptId is int did)
            {
                bool CourseInDept(int courseId)
                {
                    var c = store.Courses.FirstOrDefault(x => x.Id == courseId);
                    return c != null && c.DepartmentId == did;
                }

                planRows = planRows.Where(r => CourseInDept(r.CourseId)).ToList();
                queued = queued.Where(r => CourseInDept(r.CourseId)).ToList();
                demands = demands.Where(d => d.DepartmentId == did).ToList();
            }


            const int TheoryCap = 35;
            const int LabCap = 15;

            foreach (var row in planRows)
            {
                if (row.StudentsThisTerm <= 0)
                    continue;

                int totalStudents = row.StudentsThisTerm;
                if (row.Repeaters > 0)
                    totalStudents += row.Repeaters;

                var course = store.Courses.FirstOrDefault(c => c.Id == row.CourseId);
                if (course is null) continue;

                var split = SectionPlanner.GetHoursSplit(store, course);
                double th = split.theoryHours;
                double lab = split.labHours;

                int sections;
                if (lab > 0)
                {
                    sections = (int)Math.Ceiling(totalStudents / (double)LabCap);
                }
                else
                {
                    sections = (int)Math.Ceiling(totalStudents / (double)TheoryCap);
                }

                if (sections < 1) sections = 1;
                row.SectionsRequested = sections;
            }

            if (queued.Count > 0)
            {
                foreach (var q in queued)
                {
                    var src = planRows.FirstOrDefault(p => p.CourseId == q.CourseId);
                    if (src != null && src.SectionsRequested > 0)
                    {
                        q.SectionsRequested = src.SectionsRequested;
                    }
                }
            }

            bool hasQueued = queued.Count > 0;
            bool hasDemands = demands.Count > 0;
            bool hasPlanRows = CanGenerate();

            if (!hasQueued && !hasPlanRows && !hasDemands)
            {
                MessageBox.Show("Add courses to the selected plan, specify the required sections, or enter the student counts.", "Notice");
                return;
            }
            store.PlanningIssues?.Clear();

	            const bool ApplyPlanPrefsToHardOverrides = false;
	            if (!IsSupervisor && ApplyPlanPrefsToHardOverrides)
            {
                foreach (var r in planRows)
                {
                    if (r.PreferredFacultyId is int fid &&
                        !store.CourseFacultyOverrides.Any(o => o.CourseId == r.CourseId && o.FacultyId == fid))
                        store.CourseFacultyOverrides.Add(new CourseFacultyOverride(r.CourseId, fid));

                    if (r.PreferredSlotId is int sid &&
                        !store.CourseSlotOverrides.Any(o => o.CourseId == r.CourseId && o.SlotId == sid))
                        store.CourseSlotOverrides.Add(new CourseSlotOverride(r.CourseId, sid));
                }

                foreach (var r in queued)
                {
                    if (r.PreferredFacultyId is int fid &&
                        !store.CourseFacultyOverrides.Any(o => o.CourseId == r.CourseId && o.FacultyId == fid))
                        store.CourseFacultyOverrides.Add(new CourseFacultyOverride(r.CourseId, fid));

                    if (r.PreferredSlotId is int sid &&
                        !store.CourseSlotOverrides.Any(o => o.CourseId == r.CourseId && o.SlotId == sid))
                        store.CourseSlotOverrides.Add(new CourseSlotOverride(r.CourseId, sid));
                }
            }

	            static int[]? NormalizePreferredSlots(IReadOnlyList<int>? slots, int? fallbackSingle)
	            {
	                if (slots != null && slots.Count > 0)
	                    return slots.Where(x => x > 0).Distinct().ToArray();
	
	                if (fallbackSingle is int sid && sid > 0)
	                    return new[] { sid };

	                return null;
	            }

            IEnumerable<Scheduler.MixedPick> ExpandToMixed(
                Course c,
                int sections,
                int? forcedFacultyId,
                int? forcedSlotId,
                int? forcedRoomId,
                IReadOnlyList<int>? theoryPreferredSlots,
                IReadOnlyList<int>? labPreferredSlots,
                bool isManual)
            {
                var (th, lab, preferSame) = SectionPlanner.GetHoursSplit(store, c);

                int labSections = Math.Max(1, sections);

                int theorySections;
                if (th > 0 && lab > 0)
                {
                    const int LabCapLocal = 15;
                    const int TheoryCapLocal = 35;

                    int approxStudents = labSections * LabCapLocal;
                    theorySections = (int)Math.Ceiling(approxStudents / (double)TheoryCapLocal);
                }
                else
                {
                    theorySections = labSections;
                }

                if (th > 0 && theorySections < 1) theorySections = 1;
                if (lab > 0 && labSections < 1) labSections = 1;

                for (int s = 1; s <= theorySections; s++)
                {
	                    var thSlots = NormalizePreferredSlots(theoryPreferredSlots, forcedSlotId);
	                    var labSlots = NormalizePreferredSlots(labPreferredSlots, forcedSlotId);

	                    int? thHint = forcedSlotId;
	                    if (!thHint.HasValue && thSlots != null && thSlots.Length > 0) thHint = thSlots[0];
	
	                    int? labHint = forcedSlotId;
	                    if (!labHint.HasValue && labSlots != null && labSlots.Length > 0) labHint = labSlots[0];

                    if (th > 0)
                        yield return new Scheduler.MixedPick(
                            course: c,
                            section: s,
                            hours: th,
                            isLab: false,
                            forcedFacultyId: forcedFacultyId,
	                            forcedSlotId: thHint,
                                forcedRoomId: forcedRoomId,
                                preferredSlotIds: thSlots,
                            isManual: isManual,
                            preferSameInstructor: preferSame);

                    if (lab > 0)
                        yield return new Scheduler.MixedPick(
                            course: c,
                            section: s,
                            hours: lab,
                            isLab: true,
                            forcedFacultyId: forcedFacultyId,
	                            forcedSlotId: labHint,
                                forcedRoomId: forcedRoomId,
                                preferredSlotIds: labSlots,
                            isManual: isManual,
                            preferSameInstructor: preferSame);
                }

                if (lab > 0 && labSections > theorySections)
                {
                    for (int s = theorySections + 1; s <= labSections; s++)
                    {
	                        var labSlots = NormalizePreferredSlots(labPreferredSlots, forcedSlotId);
	                        int? labHint = forcedSlotId;
	                        if (!labHint.HasValue && labSlots != null && labSlots.Length > 0) labHint = labSlots[0];

                        yield return new Scheduler.MixedPick(
                            course: c,
                            section: s,
                            hours: lab,
                            isLab: true,
                            forcedFacultyId: forcedFacultyId,
	                            forcedSlotId: labHint,
                                forcedRoomId: forcedRoomId,
                                preferredSlotIds: labSlots,
                            isManual: isManual,
                            preferSameInstructor: preferSame,
                            skipAutoComplement: true);
                    }
                }
            }


            List<Scheduler.MixedPick> mixedPicks;

            if (hasQueued)
            {

                mixedPicks = new List<Scheduler.MixedPick>();

                foreach (var courseGroup in queued
                    .Where(r => r.SectionsRequested > 0)
                    .GroupBy(r => r.CourseId))
                {
                    var course = store.Courses.First(x => x.Id == courseGroup.Key);
                    var (th, lab, preferSame) = SectionPlanner.GetHoursSplit(store, course);

	                    var prefsBySection = new Dictionary<int, (int? facultyId, int? slotId, int? roomId, int[]? thSlots, int[]? labSlots)>();
                    int labSections = 0;

                    foreach (var r in courseGroup)
                    {
                        int start = r.SectionIndex > 0 ? r.SectionIndex : 1;
                        int count = r.SectionsRequested > 0 ? r.SectionsRequested : 1;

                        int end = start + count - 1;
                        if (end > labSections) labSections = end;

	                        var thSlots = NormalizePreferredSlots(r.TheorySlotIds, r.PreferredSlotId);
	                        var labSlots = NormalizePreferredSlots(r.PracticalSlotIds, r.PreferredSlotId);
	
	                        for (int s = start; s <= end; s++)
	                            prefsBySection[s] = (r.PreferredFacultyId, r.PreferredSlotId, r.PreferredRoomId, thSlots, labSlots);
                    }

                    if (labSections <= 0) continue;

                    int theorySections;
                    if (th > 0.0001 && lab > 0.0001)
                    {
                        int approxStudents = labSections * 15;
                        theorySections = (int)Math.Ceiling(approxStudents / 35.0);
                        if (theorySections < 1) theorySections = 1;
                    }
                    else
                    {
                        theorySections = labSections;
                    }

	                    prefsBySection.TryGetValue(1, out var basePref);

                    for (int s = 1; s <= labSections; s++)
                    {
                        var pref = prefsBySection.TryGetValue(s, out var p) ? p : basePref;
	                        int? forcedFacultyId = (pref.facultyId is int fid && fid > 0) ? fid : null;
                            int? forcedSlotId = (pref.slotId is int sid && sid > 0) ? sid : null;
                            int? forcedRoomId = (pref.roomId is int rid && rid > 0) ? rid : null;

	                        int? thHint = forcedSlotId;
	                        if (!thHint.HasValue && pref.thSlots != null && pref.thSlots.Length > 0) thHint = pref.thSlots[0];
	
	                        int? labHint = forcedSlotId;
	                        if (!labHint.HasValue && pref.labSlots != null && pref.labSlots.Length > 0) labHint = pref.labSlots[0];

                        if (s <= theorySections)
                        {
	                            if (th > 0.0001)
	                                mixedPicks.Add(new Scheduler.MixedPick(course, s, th, isLab: false, forcedFacultyId, thHint, forcedRoomId, preferredSlotIds: pref.thSlots, isManual: true, preferSameInstructor: preferSame, skipAutoComplement: false));

	                            if (lab > 0.0001)
	                                mixedPicks.Add(new Scheduler.MixedPick(course, s, lab, isLab: true, forcedFacultyId, labHint, forcedRoomId, preferredSlotIds: pref.labSlots, isManual: true, preferSameInstructor: preferSame, skipAutoComplement: false));
                        }
                        else
                        {
	                            if (lab > 0.0001)
	                                mixedPicks.Add(new Scheduler.MixedPick(course, s, lab, isLab: true, forcedFacultyId, labHint, forcedRoomId, preferredSlotIds: pref.labSlots, isManual: true, preferSameInstructor: preferSame, skipAutoComplement: true));
                        }
                    }
                }
            }
            else if (hasDemands)
            {
                var rules = new PlannerRules
                {
                    MinSectionSize = 10,
                    MaxSectionSize = 40,
                    MaxPracticalSectionSize = 15,
                    PracticalCourseIds = new HashSet<int>(store.PracticalCourseIds ?? Enumerable.Empty<int>()),
                    CourseCapOverrides = new Dictionary<int, int>(store.CourseCapOverrides ?? new Dictionary<int, int>()),
                };

                var plan = SectionPlanner.ComputeSections(demands, rules);

var courseById = store.Courses.ToDictionary(c => c.Id);

var baseRowsByCourseId = planRows
    .Where(r => r.SectionIndex is null)
    .GroupBy(r => r.CourseId)
    .ToDictionary(g => g.Key, g => g.First());

var overrides = planRows
    .Where(r => r.SectionIndex is not null)
    .GroupBy(r => (r.CourseId, r.SectionIndex!.Value))
    .ToDictionary(g => g.Key, g => g.Last());

mixedPicks = new List<Scheduler.MixedPick>();
foreach (var s in plan.Sections)
{
    if (!courseById.TryGetValue(s.CourseId, out var c))
        throw new InvalidOperationException($"CourseId {s.CourseId} not found.");

    var split = SectionPlanner.GetHoursSplit(store, c);

    baseRowsByCourseId.TryGetValue(s.CourseId, out var baseRow);
    overrides.TryGetValue((s.CourseId, s.SectionIndex), out var overrideRow);

	    int? forcedFacultyId = overrideRow?.PreferredFacultyId ?? baseRow?.PreferredFacultyId;
	    int? forcedSlotId = overrideRow?.PreferredSlotId ?? baseRow?.PreferredSlotId;
        int? forcedRoomId = overrideRow?.PreferredRoomId ?? baseRow?.PreferredRoomId;

    if (forcedFacultyId is int f && f <= 0) forcedFacultyId = null;
    if (forcedSlotId is int sl && sl <= 0) forcedSlotId = null;
    if (forcedRoomId is int rr && rr <= 0) forcedRoomId = null;
	    IReadOnlyList<int>? thPrefList = overrideRow?.TheorySlotIds ?? baseRow?.TheorySlotIds;
	    IReadOnlyList<int>? labPrefList = overrideRow?.PracticalSlotIds ?? baseRow?.PracticalSlotIds;
	    var thSlots = NormalizePreferredSlots(thPrefList, forcedSlotId);
	    var labSlots = NormalizePreferredSlots(labPrefList, forcedSlotId);

	    int? thHint = forcedSlotId;
	    if (!thHint.HasValue && thSlots != null && thSlots.Length > 0) thHint = thSlots[0];
	    int? labHint = forcedSlotId;
	    if (!labHint.HasValue && labSlots != null && labSlots.Length > 0) labHint = labSlots[0];

	    bool isManualPick = (forcedFacultyId is int) || (forcedSlotId is int) || (thSlots != null && thSlots.Length > 0) || (labSlots != null && labSlots.Length > 0);

    if (split.theoryHours > 0.0001)
    {
	        mixedPicks.Add(new Scheduler.MixedPick(
	            course: c,
	            section: s.SectionIndex,
	            hours: split.theoryHours,
	            isLab: false,
	            forcedFacultyId: forcedFacultyId,
	            forcedSlotId: thHint,
                                forcedRoomId: forcedRoomId,
                                preferredSlotIds: thSlots,
	            isManual: isManualPick,
	            preferSameInstructor: split.preferSameTeacher,
	            skipAutoComplement: false));
    }

    if (split.labHours > 0.0001)
    {
	        mixedPicks.Add(new Scheduler.MixedPick(
	            course: c,
	            section: s.SectionIndex,
	            hours: split.labHours,
	            isLab: true,
	            forcedFacultyId: forcedFacultyId,
	            forcedSlotId: labHint,
                                forcedRoomId: forcedRoomId,
                                preferredSlotIds: labSlots,
	            isManual: isManualPick,
	            preferSameInstructor: split.preferSameTeacher,
	            skipAutoComplement: false));
    }
}

}
            else
            {
                mixedPicks = planRows
                    .Where(r => r.SectionsRequested > 0)
                    .SelectMany(r =>
	                    {
	                        bool isManualPick = (r.PreferredFacultyId is int)
	                                           || (r.PreferredSlotId is int)
	                                           || (r.TheorySlotIds != null && r.TheorySlotIds.Count > 0)
	                                           || (r.PracticalSlotIds != null && r.PracticalSlotIds.Count > 0);

	                        return ExpandToMixed(
                                store.Courses.First(c => c.Id == r.CourseId),
                                r.SectionsRequested,
                                r.PreferredFacultyId,
                                r.PreferredSlotId,
                                r.PreferredRoomId,
                                r.TheorySlotIds,
                                r.PracticalSlotIds,
                                isManual: isManualPick);
	                    })
                    .ToList();
            }


            var tempManualized = new List<(Assignment a, string oldStatus)>();
            if (lockedDeptId is int didLock)
            {
                foreach (var a in store.Assignments.Where(a => a.DepartmentId != didLock))
                {
                    if (!string.Equals(a.Status, "MANUAL", StringComparison.OrdinalIgnoreCase))
                    {
                        tempManualized.Add((a, a.Status));
                        a.Status = "MANUAL";
                    }
                }
            }

            SetBusy(true);
            try
            {
                await Task.Run(() => Scheduler.Build.Mixed(store, SelectedKind, mixedPicks));
            }
            finally
            {
                foreach (var item in tempManualized)
                    item.a.Status = item.oldStatus;

                SetBusy(false);
            }

            RefreshRows();

            if (store.PlanningIssues?.Any(i => i.Severity != IssueSeverity.Info) == true)
            {
                var issues = store.PlanningIssues
                                  .Where(i => i.Severity != IssueSeverity.Info)
                                  .Take(10)
                                  .Select(i => "• " + i.Message);

                var extra = store.PlanningIssues.Count > 10
                    ? $"\n... and {store.PlanningIssues.Count - 10} more items."
                    : string.Empty;

                MessageBox.Show(
                    "Some sections could not be generated:\n" + string.Join("\n", issues) + extra,
                    "Generation Notice",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }


            // 4.4: Auto-distribute students after generating schedule (when student plans exist)
            await TryAutoDistributeStudentsAfterGenerateAsync();

        }
    }
}
