using System.Collections.Generic;
using System.Linq;
using MiniTrainerScheduler.Models;

namespace MiniTrainerScheduler.Services
{
    /// <summary>
    /// </summary>
    internal static class CohortGuard
    {
        private static readonly Dictionary<(int deptId, int level), HashSet<int>> Busy = new();

        private static readonly Dictionary<int, List<(int deptId, int level)>> CourseCohorts = new();

        public static void Init(IEnumerable<(Course course, int sections)> selected)
        {
            Busy.Clear();
            CourseCohorts.Clear();

            var activeDeptsByLevel = selected
                .Where(x => !x.course.IsGeneralCourse)
                .GroupBy(x => x.course.Level)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(x => x.course.DepartmentId).Distinct().ToList()
                );

            foreach (var (course, _) in selected)
            {
                var cohorts = new List<(int deptId, int level)>();

                if (course.IsGeneralCourse)
                {
                    if (activeDeptsByLevel.TryGetValue(course.Level, out var depts))
                        cohorts.AddRange(depts.Select(d => (d, course.Level)));
                }
                else
                {
                    cohorts.Add((course.DepartmentId, course.Level));
                }

                CourseCohorts[course.Id] = cohorts;
            }
        }

        public static bool IsFree(Course course, int slotId)
        {
            if (!CourseCohorts.TryGetValue(course.Id, out var cohorts)) return true;
            foreach (var key in cohorts)
            {
                if (Busy.TryGetValue(key, out var set) && set.Contains(slotId))
                    return false;
            }
            return true;
        }

        public static void Reserve(Course course, int slotId)
        {
            if (!CourseCohorts.TryGetValue(course.Id, out var cohorts)) return;
            foreach (var key in cohorts)
            {
                if (!Busy.TryGetValue(key, out var set))
                {
                    set = new HashSet<int>();
                    Busy[key] = set;
                }
                set.Add(slotId);
            }
        }
    }
}
