using System;
using System.Collections.Generic;
using System.Linq;
using MiniTrainerScheduler.Models;

namespace MiniTrainerScheduler.Services
{
    public sealed class PlannerRules
    {
        public int MinSectionSize { get; set; } = 10;
        public int MaxSectionSize { get; set; } = 35;
        public int MaxPracticalSectionSize { get; set; } = 15;

        public Dictionary<int, int> CourseCapOverrides { get; set; } = new Dictionary<int, int>();

        public HashSet<int> PracticalCourseIds { get; set; } = new HashSet<int>();
    }

    public sealed class SectionPlannerResult
    {
        public List<SectionPlanEntry> Sections { get; } = new List<SectionPlanEntry>();
        public List<PlanningIssue> Issues { get; } = new List<PlanningIssue>();
    }

    public static partial class SectionPlanner
    {
        public static SectionPlannerResult ComputeSections(IEnumerable<IntakeDemand> demands, PlannerRules rules)
        {
            var result = new SectionPlannerResult();
            if (demands == null) return result;

            foreach (var g in demands.GroupBy(d => new { d.Term, d.Program, d.Level, d.CourseId }))
            {
                var key = g.Key;
                int D = g.Sum(x => Math.Max(0, x.DemandCount) + Math.Max(0, x.RepeatersCount));

                bool isPractical = rules.PracticalCourseIds.Contains(key.CourseId);
                int capDefault = isPractical ? rules.MaxPracticalSectionSize : rules.MaxSectionSize;
                int cap = rules.CourseCapOverrides.TryGetValue(key.CourseId, out var ov) ? Math.Min(capDefault, ov) : capDefault;
                cap = Math.Max(rules.MinSectionSize, cap);

                if (D < rules.MinSectionSize)
                {
                    result.Issues.Add(new PlanningIssue {
                        Severity = IssueSeverity.Warning,
                        Message  = $"Demand {D} < Min {rules.MinSectionSize}: no section opened.",
                        Context  = Ctx(key)
                    });
                    continue;
                }

                var sizes = ComputeBalancedSizes(D, rules.MinSectionSize, cap);

                int idx = 0;
                foreach (var s in sizes)
                {
                    result.Sections.Add(new SectionPlanEntry {
                        Term = key.Term,
                        Program = key.Program,
                        Level = key.Level,
                        CourseId = key.CourseId,
                        SectionIndex = ++idx,
                        Size = s,
                        Type = SectionType.Normal
                    });
                }
            }
            return result;
        }

        public static List<int> RebalanceFixedN(IList<int> current, int min, int cap, out bool feasible)
        {
            int D = current?.Sum() ?? 0;
            int N = current?.Count ?? 0;

            var minN = (int)Math.Ceiling(D / (double)cap);
            var maxN = (int)Math.Floor(D / (double)min);

            feasible = (N >= minN && N <= Math.Max(minN, maxN));
            if (!feasible)
            {
                return ComputeBalancedSizes(D, min, cap);
            }

            return ComputeBalancedSizes(D, min, cap, N);
        }

        public static List<int> ComputeBalancedSizes(int D, int min, int cap, int? preferredN = null)
        {
            if (D < min) return new List<int>();

            int minN = (int)Math.Ceiling(D / (double)cap);
            int maxN = (int)Math.Floor(D / (double)min);
            if (maxN < minN) maxN = minN;

            int N;
            if (preferredN.HasValue)
            {
                N = Math.Max(minN, Math.Min(maxN, preferredN.Value));
            }
            else
            {
                double target = (min + cap) / 2.0;
                N = (int)Math.Round(D / target);
                if (N < minN) N = minN;
                if (N > maxN) N = maxN;
                if (N <= 0) N = minN > 0 ? minN : 1;
            }

            int a = D / N;
            int r = D % N;

            var sizes = new List<int>(N);
            for (int i = 0; i < N; i++)
            {
                int sz = a + (i < r ? 1 : 0);
                sizes.Add(sz);
            }
            return sizes;
        }

        private static string Ctx(dynamic key) => $"{key.Term}/{key.Program}/L{key.Level}/C{key.CourseId}";
    }
}
