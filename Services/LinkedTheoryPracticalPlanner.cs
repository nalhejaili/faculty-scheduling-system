using System;
using System.Collections.Generic;

namespace TrainerScheduler.Services
{
    /// <summary>
    /// </summary>
    public static class LinkedTheoryPracticalPlanner
    {
        /// <summary>
        /// example:
        /// totalStudents = 70, theorySections = 2, practicalGroupSize = 15
        /// </summary>
        public static int[] AllocatePracticalGroupsPerTheorySection(
            int totalStudents,
            int theorySections,
            int practicalGroupSize)
        {
            if (theorySections <= 0)
                throw new ArgumentOutOfRangeException(nameof(theorySections), "The number of lecture sections cannot be zero or negative.");

            if (practicalGroupSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(practicalGroupSize), "The lab group capacity must be greater than zero.");

            var result = new int[theorySections];

            int baseStudentsPerTheory = totalStudents / theorySections;
            int remainder = totalStudents % theorySections;

            for (int i = 0; i < theorySections; i++)
            {
                int studentsInThisTheorySection = baseStudentsPerTheory + (i < remainder ? 1 : 0);

                if (studentsInThisTheorySection <= 0)
                {
                    result[i] = 0;
                    continue;
                }

                int groups = (int)Math.Ceiling(
                    studentsInThisTheorySection / (double)practicalGroupSize);

                if (groups < 1)
                    groups = 1;

                result[i] = groups;
            }

            return result;
        }
    }
}
