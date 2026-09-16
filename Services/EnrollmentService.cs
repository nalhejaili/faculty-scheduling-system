using System;
using System.Collections.Generic;
using System.Linq;
using MiniTrainerScheduler.Models;

namespace MiniTrainerScheduler.Services
{
    public sealed class EnrollmentOptions
    {
        public int TheoryCapacity { get; set; } = 35;
        public int LabCapacity { get; set; } = 15;

        public bool EnforceDepartment { get; set; } = true;
        public bool EnforceLevel { get; set; } = true;

        /// <summary>
        /// </summary>
        public bool ResetExistingEnrollments { get; set; } = true;
    }

    public sealed class EnrollmentReport
    {
        public int StudentsProcessed { get; set; }
        public int PlansProcessed { get; set; }
        public int EnrollmentsCreated { get; set; }

        public int UnassignedCount => Unassigned.Count;

        public List<UnassignedPlan> Unassigned { get; } = new();

        public Dictionary<string, int> UnassignedByReason =>
            Unassigned.GroupBy(x => x.Reason).ToDictionary(g => g.Key, g => g.Count());
    }

    public sealed record UnassignedPlan(int StudentId, int CourseId, string Kind, string Reason, string? Details = null);

    /// <summary>
    ///
    /// </summary>
    public sealed class EnrollmentService
    {
        private sealed class SectionGroup
        {
            public required int DepartmentId { get; init; }
            public required int CourseId { get; init; }
            public required int SectionIndex { get; init; }
            public required string Kind { get; init; } // THEORY / LAB / ""
            public required List<Assignment> Sessions { get; init; }

            public int Capacity(EnrollmentOptions opt)
                => string.Equals(Kind, "LAB", StringComparison.OrdinalIgnoreCase) ? opt.LabCapacity : opt.TheoryCapacity;

            public override string ToString() => $"{DepartmentId}:{CourseId}:{SectionIndex}:{Kind}";
        }

        public EnrollmentReport Distribute(DataStore store, EnrollmentOptions? options = null)
        {
            var opt = options ?? new EnrollmentOptions();
            var report = new EnrollmentReport();

            if (opt.ResetExistingEnrollments)
                store.StudentEnrollments.Clear();

            // Lookups
            var studentById = store.Students.ToDictionary(s => s.Id, s => s);
            var courseById = store.Courses.ToDictionary(c => c.Id, c => c);
            var slotById = store.Slots.ToDictionary(s => s.Id, s => s);

            // Build groups: one group = one section of one kind, with multiple sessions (slots)
            var groups = store.Assignments
                .GroupBy(a => new { a.DepartmentId, a.CourseId, a.SectionIndex, Kind = NormalizeKind(a.Kind) })
                .Select(g => new SectionGroup
                {
                    DepartmentId = g.Key.DepartmentId,
                    CourseId = g.Key.CourseId,
                    SectionIndex = g.Key.SectionIndex,
                    Kind = g.Key.Kind,
                    Sessions = g.ToList()
                })
                .ToList();

            // Pre-calc groups by (dept, course, kind)
            var groupsByKey = groups
                .GroupBy(g => (g.DepartmentId, g.CourseId, g.Kind))
                .ToDictionary(g => g.Key, g => g.OrderBy(x => x.SectionIndex).ToList());

            // Occupancy at group-level: unique students per group
            var groupOccupancy = new Dictionary<SectionGroup, HashSet<int>>();

            // Student schedule occupancy (slot overlaps)
            var studentSessionSlots = new Dictionary<int, List<Slot>>();

            // Plans: order per student: repeats first, then priority desc.
            // We process per-student to keep a coherent greedy behavior.
            var plansByStudent = store.StudentPlans
                .GroupBy(p => p.StudentId)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderByDescending(p => p.IsRepeat)
                          .ThenByDescending(p => p.Priority)
                          .ThenBy(p => p.CourseId)
                          .ToList()
                );

            foreach (var (studentId, plans) in plansByStudent)
            {
                report.StudentsProcessed++;

                if (!studentById.TryGetValue(studentId, out var student))
                {
                    foreach (var p in plans)
                        report.Unassigned.Add(new UnassignedPlan(studentId, p.CourseId, "", "Student record not found"));
                    continue;
                }

                if (!studentSessionSlots.TryGetValue(studentId, out var occupied))
                {
                    occupied = new List<Slot>();
                    studentSessionSlots[studentId] = occupied;
                }

                foreach (var plan in plans)
                {
                    report.PlansProcessed++;

                    if (!courseById.TryGetValue(plan.CourseId, out var course))
                    {
                        report.Unassigned.Add(new UnassignedPlan(studentId, plan.CourseId, "", "Course record not found"));
                        continue;
                    }

                    // Determine available kinds for this course in the student's dept (or all depts if not enforcing)
                    var candidateKinds = DetermineKinds(groups, student, course.Id, opt);

                    if (candidateKinds.Count == 0)
                    {
                        report.Unassigned.Add(new UnassignedPlan(studentId, plan.CourseId, "", "No timetable section is available for this course"));
                        continue;
                    }

                    foreach (var kind in candidateKinds)
                    {
                        var chosen = ChooseGroupForPlan(groupsByKey, student, course, kind, occupied, slotById, groupOccupancy, opt);
                        if (chosen is null)
                        {
                            // Determine best reason (capacity/conflict/department/level) - approximate
                            var reason = GuessUnassignedReason(groupsByKey, student, course, kind, occupied, slotById, groupOccupancy, opt);
                            report.Unassigned.Add(new UnassignedPlan(studentId, plan.CourseId, kind, reason));
                            continue;
                        }

                        // Mark occupancy and create enrollments for ALL sessions in the chosen group.
                        if (!groupOccupancy.TryGetValue(chosen, out var occSet))
                        {
                            occSet = new HashSet<int>();
                            groupOccupancy[chosen] = occSet;
                        }
                        if (!occSet.Add(studentId))
                        {
                            // already assigned, skip
                            continue;
                        }

                        foreach (var session in chosen.Sessions)
                        {
                            // Add enrollment row (per session)
                            store.StudentEnrollments.Add(new StudentEnrollment
                            {
                                StudentId = studentId,
                                AssignmentId = session.Id,
                                CreatedUtc = DateTime.UtcNow
                            });

                            // Add occupied slot
                            if (slotById.TryGetValue(session.SlotId, out var sl))
                                occupied.Add(sl);
                        }

                        report.EnrollmentsCreated += chosen.Sessions.Count;
                    }
                }
            }

            return report;
        }

        private static string NormalizeKind(string? kind)
        {
            if (string.IsNullOrWhiteSpace(kind)) return "THEORY";
            return kind.Trim().ToUpperInvariant();
        }

        private static List<string> DetermineKinds(List<SectionGroup> groups, Student student, int courseId, EnrollmentOptions opt)
        {
            // Note: we include THEORY by default if there are any assignments with blank kind.
            var kinds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var g in groups)
            {
                if (g.CourseId != courseId) continue;

                // dept enforcement is applied later; here we gather kinds globally for the course
                kinds.Add(g.Kind);
            }

            // If only THEORY exists, list will be [THEORY]. If LAB also exists, we return THEORY then LAB.
            var list = kinds.Select(NormalizeKind).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            list.Sort((a, b) =>
            {
                // prefer THEORY first
                if (a == "THEORY" && b != "THEORY") return -1;
                if (b == "THEORY" && a != "THEORY") return 1;
                return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
            });
            return list;
        }

        private static SectionGroup? ChooseGroupForPlan(
            Dictionary<(int deptId, int courseId, string kind), List<SectionGroup>> groupsByKey,
            Student student,
            Course course,
            string kind,
            List<Slot> occupied,
            Dictionary<int, Slot> slotById,
            Dictionary<SectionGroup, HashSet<int>> groupOccupancy,
            EnrollmentOptions opt)
        {
            // Candidate departments: either student's department (if enforce + known) or any dept that has the course
            IEnumerable<int> depts;
            if (opt.EnforceDepartment && student.DepartmentId.HasValue)
                depts = new[] { student.DepartmentId.Value };
            else
                depts = groupsByKey.Keys.Where(k => k.courseId == course.Id && k.kind.Equals(kind, StringComparison.OrdinalIgnoreCase))
                                        .Select(k => k.deptId)
                                        .Distinct()
                                        .ToList();

            foreach (var deptId in depts)
            {
                if (!groupsByKey.TryGetValue((deptId, course.Id, NormalizeKind(kind)), out var groups))
                    continue;

                // Filter by level if needed
                if (opt.EnforceLevel && student.Level.HasValue)
                {
                    if (course.Level != student.Level.Value)
                        return null; // level mismatch for this course
                }

                // Choose least-filled group that doesn't conflict and has capacity
                var candidates = groups
                    .Select(g => new
                    {
                        Group = g,
                        Count = groupOccupancy.TryGetValue(g, out var set) ? set.Count : 0
                    })
                    .OrderBy(x => x.Count) // greedy balance
                    .ThenBy(x => x.Group.SectionIndex)
                    .ToList();

                foreach (var c in candidates)
                {
                    var cap = c.Group.Capacity(opt);
                    if (c.Count >= cap) continue;
                    if (Conflicts(c.Group, occupied, slotById)) continue;

                    return c.Group;
                }
            }

            return null;
        }

        private static string GuessUnassignedReason(
            Dictionary<(int deptId, int courseId, string kind), List<SectionGroup>> groupsByKey,
            Student student,
            Course course,
            string kind,
            List<Slot> occupied,
            Dictionary<int, Slot> slotById,
            Dictionary<SectionGroup, HashSet<int>> groupOccupancy,
            EnrollmentOptions opt)
        {
            // Dept / level checks first
            if (opt.EnforceDepartment && student.DepartmentId.HasValue)
            {
                var dept = student.DepartmentId.Value;
                if (!groupsByKey.ContainsKey((dept, course.Id, NormalizeKind(kind))))
                    return "Department/level restriction";
            }

            if (opt.EnforceLevel && student.Level.HasValue && course.Level != student.Level.Value)
                return "Department/level restriction";

            // Try to see if conflicts are the issue
            var anyConflict = false;
            var anyCapacity = false;

            foreach (var key in groupsByKey.Keys.Where(k => k.courseId == course.Id && k.kind.Equals(NormalizeKind(kind), StringComparison.OrdinalIgnoreCase)))
            {
                var groups = groupsByKey[key];
                foreach (var g in groups)
                {
                    var count = groupOccupancy.TryGetValue(g, out var set) ? set.Count : 0;
                    if (count < g.Capacity(opt)) anyCapacity = true;
                    if (!Conflicts(g, occupied, slotById)) continue;
                    anyConflict = true;
                }
            }

            if (anyConflict) return "Time conflict";
            if (!anyCapacity) return "Section capacity is full";
            return "No suitable section is available";
        }

        private static bool Conflicts(SectionGroup group, List<Slot> occupied, Dictionary<int, Slot> slotById)
        {
            foreach (var s in group.Sessions)
            {
                if (!slotById.TryGetValue(s.SlotId, out var slot)) continue;
                foreach (var occ in occupied)
                {
                    if (SlotsOverlap(slot, occ)) return true;
                }
            }
            return false;
        }

        private static bool SlotsOverlap(Slot a, Slot b)
        {
            if (a.Day != b.Day) return false;
            // TimeOnly doesn't include date
            return a.Start < b.End && b.Start < a.End;
        }
    }
}
