using System;
using System.Collections.Generic;
using System.Linq;
using MiniTrainerScheduler.Models;

namespace MiniTrainerScheduler.Services
{
    /// <summary>
    /// Prevents placing two *different* courses for the same student cohort
    /// (DepartmentId + Level) at the *same Slot*.
    /// It still allows placing multiple sections of the *same* course at the same Slot.
    /// </summary>
    public sealed class CohortSlotGuard
    {
        private readonly DataStore _store;

        // key: (dept, level, slotId)  ->  distinct courseIds already occupying that slot for this cohort
        private readonly Dictionary<(int dept, int level, int slotId), HashSet<int>> _cohortSlotCourses
            = new Dictionary<(int, int, int), HashSet<int>>();

        private CohortSlotGuard(DataStore store)
        {
            _store = store;
        }

        public static CohortSlotGuard FromStore(DataStore store)
        {
            var guard = new CohortSlotGuard(store);
            guard.SeedFromExistingAssignments();
            return guard;
        }

        /// <summary>True if putting this course at slotId would violate student-cohort non-overlap.</summary>
        public bool IsBlocked(int courseId, int slotId)
        {
            var (dept, level) = GetCohort(courseId);
            var key = (dept, level, slotId);
            if (!_cohortSlotCourses.TryGetValue(key, out var coursesHere) || coursesHere.Count == 0)
                return false;

            // Allow overlapping WITH THE SAME COURSE (multiple sections in parallel is fine).
            return !coursesHere.Contains(courseId);
        }

        /// <summary>Reserve the (dept, level, slotId) for the given courseId.</summary>
        public void Reserve(int courseId, int slotId)
        {
            var (dept, level) = GetCohort(courseId);
            var key = (dept, level, slotId);
            if (!_cohortSlotCourses.TryGetValue(key, out var set))
            {
                set = new HashSet<int>();
                _cohortSlotCourses[key] = set;
            }
            set.Add(courseId);
        }

        /// <summary>Try to choose a cohort-safe slot. Returns null if none fit.</summary>
        public int? ChooseCohortSafeSlot(int courseId, IEnumerable<Slot> candidateSlots, int? preferredSlotId = null)
        {
            // 1) Respect a preferred slot if it doesn't violate cohort rule
            if (preferredSlotId is int pref && !IsBlocked(courseId, pref))
                return pref;

            // 2) Otherwise pick first candidate that doesn't violate
            foreach (var s in candidateSlots)
            {
                if (!IsBlocked(courseId, s.Id))
                    return s.Id;
            }
            return null;
        }

        private void SeedFromExistingAssignments()
        {
            // Seed guard from what already exists in the store.Assignments
            foreach (var a in _store.Assignments)
            {
                // "a.CourseId" is expected in Assignment model. If it's named differently in your project,
                // change here accordingly.
                var courseId = a.CourseId;
                var slotId = a.SlotId;
                if (slotId == 0 || courseId == 0) continue;
                Reserve(courseId, slotId);
            }
        }

        private (int dept, int level) GetCohort(int courseId)
        {
            // Courses table is expected to contain DepartmentId and Level
            var c = _store.Courses.FirstOrDefault(x => x.Id == courseId);
            if (c == null) return (0, 0);
            return (c.DepartmentId, c.Level);
        }
    }
}
