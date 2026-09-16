using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using MiniTrainerScheduler.Models;
using MiniTrainerScheduler.Services;

namespace MiniTrainerScheduler.ViewModels
{
    public sealed partial class MainViewModel
    {
        void Generate()
        {
            if (Store is null)
            {
                MessageBox.Show("The data store is not initialized.", "Notice");
                return;
            }

            var store = Store;

            IEnumerable<CoursePlanRow> planRows = (PlanRows as IEnumerable<CoursePlanRow>) ?? Array.Empty<CoursePlanRow>();
            IEnumerable<QueuedPlanRow> queued = (QueuedPlan as IEnumerable<QueuedPlanRow>) ?? Array.Empty<QueuedPlanRow>();
            IEnumerable<IntakeDemand> demands = Store.Demands ?? Enumerable.Empty<IntakeDemand>();

            bool hasQueued = queued.Any();
            bool hasDemands = demands.Any();
            bool hasPlanRows = CanGenerate();

            if (!hasQueued && !hasPlanRows && !hasDemands)
            {
                MessageBox.Show("Add courses to the selected plan, specify the required sections, or enter the student counts.", "Notice");
                return;
            }

            store.PlanningIssues?.Clear();

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

            IEnumerable<(Course course, int sections, int? forcedFacultyId, int? forcedSlotId, bool isManual)> picks;

            if (hasQueued)
            {
                picks = queued
                    .Where(r => r.SectionsRequested > 0)
                    .Select(r => (
                        course: store.Courses.First(c => c.Id == r.CourseId),
                        sections: Math.Max(1, r.SectionsRequested),
                        forcedFacultyId: r.PreferredFacultyId,
                        forcedSlotId: r.PreferredSlotId,
                       isManual: (r.PreferredFacultyId is int || r.PreferredSlotId is int)

                    ));
            }
            else if (hasDemands)
            {
                var rules = new PlannerRules
                {
                    MinSectionSize = 10,
                    MaxSectionSize = 40,
                    MaxPracticalSectionSize = 15,
                    PracticalCourseIds = new HashSet<int>((store.PracticalCourseIds as IEnumerable<int>) ?? Enumerable.Empty<int>()),
                    CourseCapOverrides = new Dictionary<int, int>(store.CourseCapOverrides ?? new Dictionary<int, int>()),
                };

                var plan = SectionPlanner.ComputeSections(demands, rules);

                picks = plan.Sections
                    .GroupBy(s => s.CourseId)
                    .Select(g => (
                        course: store.Courses.First(c => c.Id == g.Key),
                        sections: g.Count(),
                        forcedFacultyId: (int?)null,
                        forcedSlotId: (int?)null,
                        isManual: false
                    ));
            }
            else
            {
                picks = planRows
                    .Where(r => r.SectionsRequested > 0)
                    .Select(r => (
                        course: store.Courses.First(c => c.Id == r.CourseId),
                        sections: r.SectionsRequested,
                        forcedFacultyId: r.PreferredFacultyId,
                        forcedSlotId: r.PreferredSlotId,
                        isManual: false
                    ));
            }

            Scheduler.BuildSchedule(store, SelectedKind, picks);

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
        }
    }
}
