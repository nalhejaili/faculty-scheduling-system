using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;

namespace MiniTrainerScheduler.ViewModels
{
    public sealed partial class MainViewModel
    {
        public ICommand AddSectionOverrideCommand { get; }
        public ICommand RemoveSectionOverrideCommand { get; }

        private void AddSectionOverride(CoursePlanRow row)
        {
            if (row is null) return;

            var courseId = row.CourseId;

            var baseRow = PlanRows.FirstOrDefault(r => r.CourseId == courseId && r.SectionIndex is null) ?? row;

            var nextIndex = PlanRows
                .Where(r => r.CourseId == courseId && r.SectionIndex is not null)
                .Select(r => r.SectionIndex!.Value)
                .DefaultIfEmpty(1)
                .Max() + 1;

            var newRow = new CoursePlanRow
            {
                CourseId = baseRow.CourseId,
                CourseName = baseRow.CourseName,
                HoursPerWeek = baseRow.HoursPerWeek,
                IsGeneralCourse = baseRow.IsGeneralCourse,
                IsPractical = baseRow.IsPractical,

                TheoryHoursRequired = baseRow.TheoryHoursRequired,
                PracticalHoursRequired = baseRow.PracticalHoursRequired,
                TheorySlotIds = new List<int>(baseRow.TheorySlotIds ?? new List<int>()),
                PracticalSlotIds = new List<int>(baseRow.PracticalSlotIds ?? new List<int>()),

                StudentsThisTerm = 0,
                Repeaters = 0,
                SectionsRequested = 1,

                PreferredFacultyId = baseRow.PreferredFacultyId,
                PreferredSlotId = baseRow.PreferredSlotId,
                PreferredRoomId = baseRow.PreferredRoomId,

                SectionIndex = nextIndex
            };

            var insertAt = PlanRows.Count;
            for (var i = PlanRows.Count - 1; i >= 0; i--)
            {
                if (PlanRows[i].CourseId == courseId)
                {
                    insertAt = i + 1;
                    break;
                }
            }

            PlanRows.Insert(insertAt, newRow);
        }

        private void RemoveSectionOverride(CoursePlanRow row)
        {
            if (row is null) return;
            if (row.SectionIndex is null) return;

            PlanRows.Remove(row);
        }
    }
}
