using System.Linq;
using MiniTrainerScheduler.Models;

namespace MiniTrainerScheduler.Services
{
    public static partial class SectionPlanner
    {
        /// <summary>
        /// </summary>
        public static (double theoryHours, double labHours, bool preferSameTeacher)
            GetHoursSplit(DataStore store, Course c)
        {
            if (store is null || c is null)
                return (0.0, 0.0, true);

            var split = store.CourseHourSplits?.FirstOrDefault(x => x.CourseId == c.Id);
            if (split != null)
            {
                var th = split.TheoryHours;
                var lab = split.PracticalHours;

                if (th < 0) th = 0;
                if (lab < 0) lab = 0;

                if (th <= 0 && lab <= 0)
                    th = c.HoursPerWeek;

                return (th, lab, split.PreferSameInstructor);
            }

            bool isPractical =
                store.PracticalCourseIds != null &&
                store.PracticalCourseIds.Contains(c.Id);

            if (isPractical)
            {
                return (0.0, c.HoursPerWeek, true);
            }

            var name = (c.Name ?? string.Empty).Trim();
            if ((name.Contains("\u062A\u062C\u0645\u064A\u0639 \u0627\u0644\u062D\u0627\u0633\u0628") ||
                 name.Contains("Computer Assembly", System.StringComparison.OrdinalIgnoreCase) ||
                 name.Contains("Computer Skills Cluster", System.StringComparison.OrdinalIgnoreCase))
                && c.HoursPerWeek >= 2)
            {
                return (1.0, c.HoursPerWeek - 1.0, true);
            }

            return (c.HoursPerWeek, 0.0, true);
        }

        public static (int theorySections, int labSections) CalculateTheoryAndLabSections(
           int totalStudents,
           bool hasTheory,
           bool hasLab,
           int theoryCap = 35,
           int labCap = 15,
           int minSectionSize = 10)
        {
            if (totalStudents < minSectionSize)
                return (0, 0);

            int theorySections = 0;
            int labSections = 0;

            if (hasTheory)
            {
                theorySections = (int)Math.Ceiling(totalStudents / (double)theoryCap);
                if (theorySections < 1) theorySections = 1;
            }

            if (hasLab)
            {
                labSections = (int)Math.Ceiling(totalStudents / (double)labCap);
                if (labSections < 1) labSections = 1;
            }

            return (theorySections, labSections);
        }
    }
}
