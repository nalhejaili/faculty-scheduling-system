using System;
using System.Linq;
using MiniTrainerScheduler.Models;

namespace MiniTrainerScheduler.Services
{
    internal static class ManualPlanBridge
    {
        public static event Action<Assignment>? ManualAssignmentRegistered;

        public static void RegisterManualAssignment(DataStore store, Assignment a)
        {
            if (store is null || a is null) return;

            if (!store.Assignments.Contains(a))
                store.Assignments.Add(a);

            if (a.FacultyId > 0 &&
                !store.CourseFacultyOverrides.Any(o => o.CourseId == a.CourseId &&
                                                      o.FacultyId == a.FacultyId))
            {
                store.CourseFacultyOverrides.Add(
                    new CourseFacultyOverride(a.CourseId, a.FacultyId));
            }

            if (a.SlotId > 0 &&
                !store.CourseSlotOverrides.Any(o => o.CourseId == a.CourseId &&
                                                    o.SlotId == a.SlotId))
            {
                store.CourseSlotOverrides.Add(
                    new CourseSlotOverride(a.CourseId, a.SlotId));
            }

            ManualAssignmentRegistered?.Invoke(a);
        }
    }
}
