using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace MiniTrainerScheduler.ViewModels
{
    public sealed partial class MainViewModel
    {
        void AddCurrentDeptToPlan()
        {
            if (SelectedDepartment is null || SelectedLevel is null)
            {
                MessageBox.Show("Please select the department and level first.");
                return;
            }

            var selected = SelectedPlanRow;
            if (selected is null)
            {
                MessageBox.Show("Please select a course from the list first.");
                return;
            }

            var baseRow = PlanRows.FirstOrDefault(p => p.CourseId == selected.CourseId && p.SectionIndex is null) ?? selected;

            var overrides = PlanRows
                .Where(p => p.CourseId == selected.CourseId && p.SectionIndex is not null)
                .OrderBy(p => p.SectionIndex)
                .ToList();

            AddCourseToQueuedPlanFromPlan(baseRow, overrides);

            RaiseAllCanExec();
            MessageBox.Show($"\"{baseRow.CourseName}\" has been added to the plan.", "Completed");
        }

        void AddCourseToQueuedPlanFromPlan(CoursePlanRow baseRow, IReadOnlyList<CoursePlanRow> overrides)
        {
            if (SelectedDepartment is null || SelectedLevel is null) return;

            int overrideMax = overrides.Count == 0 ? 0 : overrides.Max(o => o.SectionIndex ?? 0);
            int totalSections = Math.Max(Math.Max(1, baseRow.SectionsRequested), overrideMax);

            if (totalSections <= 0) return;

            for (int i = QueuedPlan.Count - 1; i >= 0; i--)
            {
                var q = QueuedPlan[i];
                if (q.DepartmentId == SelectedDepartment.Id &&
                    q.Level == SelectedLevel.Value &&
                    q.CourseId == baseRow.CourseId)
                {
                    QueuedPlan.RemoveAt(i);
                }
            }

            var ovBySection = new Dictionary<int, CoursePlanRow>();
            foreach (var o in overrides)
            {
                if (o.SectionIndex is int idx && idx > 0)
                    ovBySection[idx] = o;
            }

            for (int s = 1; s <= totalSections; s++)
            {
                ovBySection.TryGetValue(s, out var ov);

                var thSlots = (ov?.TheorySlotIds != null && ov.TheorySlotIds.Count > 0)
                    ? ov.TheorySlotIds
                    : baseRow.TheorySlotIds;

                var prSlots = (ov?.PracticalSlotIds != null && ov.PracticalSlotIds.Count > 0)
                    ? ov.PracticalSlotIds
                    : baseRow.PracticalSlotIds;

                QueuedPlan.Add(new QueuedPlanRow
                {
                    DepartmentId = SelectedDepartment.Id,
                    DepartmentName = SelectedDepartment.Name,
                    Level = SelectedLevel.Value,

                    CourseId = baseRow.CourseId,
                    CourseName = baseRow.CourseName,

                    IsPractical = baseRow.IsPractical,

                    TheoryHoursRequired = baseRow.TheoryHoursRequired,
                    PracticalHoursRequired = baseRow.PracticalHoursRequired,
                    TheorySlotIds = new List<int>(thSlots ?? new List<int>()),
                    PracticalSlotIds = new List<int>(prSlots ?? new List<int>()),

                    SectionsRequested = 1,
                    SectionIndex = s,

                    PreferredFacultyId = ov?.PreferredFacultyId ?? baseRow.PreferredFacultyId,
                    PreferredSlotId = ov?.PreferredSlotId ?? baseRow.PreferredSlotId,
                    PreferredRoomId = ov?.PreferredRoomId ?? baseRow.PreferredRoomId,

                    StudentsThisTerm = (s == 1) ? baseRow.StudentsThisTerm : 0,
                    Repeaters = (s == 1) ? baseRow.Repeaters : 0
                });
            }
        }
    }
}
