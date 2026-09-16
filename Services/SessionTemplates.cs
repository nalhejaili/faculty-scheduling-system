using System;
using System.Collections.Generic;

namespace TrainerScheduler.Services
{
    /// <summary>
    /// </summary>
    public enum SessionKind
    {
        Theory,
        Practical
    }

    /// <summary>
    /// </summary>
    public sealed class SessionTemplate
    {
        public int CourseId { get; }
        public int SectionIndex { get; }
        public SessionKind Kind { get; }
        /// <summary>
        /// </summary>
        public int DurationSlots { get; }

        public SessionTemplate(int courseId, int sectionIndex, SessionKind kind, int durationSlots)
        {
            if (durationSlots <= 0)
                throw new ArgumentOutOfRangeException(nameof(durationSlots), "DurationSlots must be >= 1.");

            CourseId = courseId;
            SectionIndex = sectionIndex;
            Kind = kind;
            DurationSlots = durationSlots;
        }

        public override string ToString()
        {
            return $"Course={CourseId}, Sec={SectionIndex}, Kind={Kind}, Hours={DurationSlots}";
        }
    }

    /// <summary>
    /// </summary>
    public static class SessionTemplateBuilder
    {
        /// <summary>
        /// </summary>
        public static IReadOnlyList<SessionTemplate> BuildForCourse(
            int courseId,
            int sectionsCount,
            int theoryHoursPerSection,
            int practicalHoursPerSection)
        {
            if (sectionsCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(sectionsCount));

            var result = new List<SessionTemplate>();

            for (int section = 1; section <= sectionsCount; section++)
            {
                if (theoryHoursPerSection > 0)
                {
                    foreach (var duration in SplitTheory(theoryHoursPerSection))
                    {
                        result.Add(new SessionTemplate(
                            courseId,
                            section,
                            SessionKind.Theory,
                            duration));
                    }
                }

                if (practicalHoursPerSection > 0)
                {
                    foreach (var duration in SplitPractical(practicalHoursPerSection))
                    {
                        result.Add(new SessionTemplate(
                            courseId,
                            section,
                            SessionKind.Practical,
                            duration));
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// </summary>
        private static IEnumerable<int> SplitTheory(int hours)
        {
            if (hours <= 0)
                yield break;

            // 1 -> 1
            // 2 -> 2
            // 3 -> 2 + 1
            // 4 -> 2 + 2
            // 5 -> 2 + 2 + 1
            // 6 -> 2 + 2 + 2
            while (hours > 1)
            {
                if (hours == 3)
                {
                    yield return 2;
                    hours -= 2;
                    break;
                }

                yield return 2;
                hours -= 2;
            }

            if (hours == 1)
                yield return 1;
        }

        /// <summary>
        /// - 1 -> 1
        /// - 2 -> 2
        /// - 3 -> 3
        /// - 4 -> 2 + 2
        /// - 5 -> 2 + 3
        /// - 6 -> 3 + 3
        /// </summary>
        private static IEnumerable<int> SplitPractical(int hours)
        {
            if (hours <= 0)
                yield break;

            while (hours > 6)
            {
                yield return 3;
                hours -= 3;
            }

            switch (hours)
            {
                case 1:
                    yield return 1;
                    break;
                case 2:
                    yield return 2;
                    break;
                case 3:
                    yield return 3;
                    break;
                case 4:
                    yield return 2;
                    yield return 2;
                    break;
                case 5:
                    yield return 2;
                    yield return 3;
                    break;
                case 6:
                    yield return 3;
                    yield return 3;
                    break;
            }
        }
    }
}
