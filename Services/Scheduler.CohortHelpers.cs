using System;
using System.Collections.Generic;
using System.Linq;
using MiniTrainerScheduler.Models;

namespace MiniTrainerScheduler.Services
{
    /// <summary>
    /// Small helpers you can call *inside* your existing Scheduler.BuildSchedule(..)
    /// to enforce the cohort (DepartmentId+Level) non-overlap rule.
    /// </summary>
    public static class SchedulerCohortHelpers
    {
        /// <summary>
        /// Returns a function you can reuse while scheduling that chooses the first slot
        /// that doesn't violate the cohort rule (or returns null if none fit).
        /// 
        /// Typical use *inside* BuildSchedule:
        /// 
        ///   var guard = CohortSlotGuard.FromStore(store);
        ///   Func<int, IEnumerable<Slot>, int?, int?> pickSafeSlot = SchedulerCohortHelpers.MakeCohortSafePicker(guard);
        /// 
        ///   // later, when you need a slot for a course:
        ///   var slotId = pickSafeSlot(course.Id, candidateSlots, forcedSlotId);
        ///   if (slotId == null) { /* log PlanningIssue and continue; */ }
        ///   else { guard.Reserve(course.Id, slotId.Value); /* then add Assignment */ }
        /// </summary>
        public static Func<int, IEnumerable<Slot>, int?, int?> MakeCohortSafePicker(CohortSlotGuard guard)
        {
            return (courseId, candidateSlots, preferredSlotId) =>
            {
                return guard.ChooseCohortSafeSlot(courseId, candidateSlots, preferredSlotId);
            };
        }
    }
}
