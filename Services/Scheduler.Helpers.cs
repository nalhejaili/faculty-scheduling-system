// TrainerScheduler/Services/Scheduler.Helpers.cs
using System;
using System.Linq;
using MiniTrainerScheduler.Models;

namespace MiniTrainerScheduler.Services
{
    public static partial class Scheduler
    {
        private static int DayOrder(DayOfWeek d) => d switch
        {
            DayOfWeek.Sunday => 0,
            DayOfWeek.Monday => 1,
            DayOfWeek.Tuesday => 2,
            DayOfWeek.Wednesday => 3,
            DayOfWeek.Thursday => 4,
            DayOfWeek.Friday => 5,
            DayOfWeek.Saturday => 6,
            _ => 7
        };

        private static double SlotHours(Slot s) => (s.End - s.Start).TotalHours;

        public static double ComputeFacultyHoursFromSlots(DataStore store, int facultyId)
        {
            var q =
                from a in store.Assignments
                where a.FacultyId == facultyId
                join sl in store.Slots on a.SlotId equals sl.Id
                select (sl.End - sl.Start).TotalHours;

            return q.Sum();
        }

        private static bool IsLate(Slot slot) => slot.Start >= new TimeOnly(12, 0);
    }
}
