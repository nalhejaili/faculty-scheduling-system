using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;

using MiniTrainerScheduler.Models;
using MiniTrainerScheduler.Services;

namespace MiniTrainerScheduler.ViewModels
{
    public sealed partial class MainViewModel
    {
        public ICommand BuildQueuedPlanFromExpectedRegistrationCommand { get; }

        private async void BuildQueuedPlanFromExpectedRegistration()
        {
            try
            {
                if (Store is null)
                {
                    MessageBox.Show("The data store is not ready yet.", "Plan from Expected Registration", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (SelectedDepartment is null)
                {
                    MessageBox.Show("Select a department first.", "Plan from Expected Registration", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (Store.StudentPlans.Count == 0 || Store.Students.Count == 0)
                {
                    try
                    {
                        await _sqlite.TryLoadStudentsAsync(Store, SelectedDepartment.Id);
                        await _sqlite.TryLoadStudentPlansAsync(Store, SelectedDepartment.Id);
                    }
                    catch
                    {
                    }
                }

                if (Store.StudentPlans.Count == 0)
                {
                    var res = MessageBox.Show(
                        "No student plan from the expected registration report is currently loaded.\n\nWould you like to import it now?",
                        "Notice",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Information);

                    if (res == MessageBoxResult.Yes)
                    {
                        await ImportExpectedRegistrationCsvAsync();

                        var scopeDeptId = SelectedDepartment.Id;

                        Store.Students.Clear();
                        Store.StudentPlans.Clear();
                        Store.StudentEnrollments.Clear();

                        await _sqlite.TryLoadStudentsAsync(Store, scopeDeptId);
                        await _sqlite.TryLoadStudentPlansAsync(Store, scopeDeptId);
                        await _sqlite.TryLoadStudentEnrollmentsAsync(Store, scopeDeptId);
                    }

                    if (Store.StudentPlans.Count == 0)
                        return;
                }

                var deptId = SelectedDepartment.Id;


                var studentIds = Store.Students
                    .Where(s => s.DepartmentId == deptId)
                    .Select(s => s.Id)
                    .ToHashSet();

                if (studentIds.Count == 0)
                {
                    var total = Store.Students.Count;
                    if (total == 0)
                    {
                        MessageBox.Show(
                            "No loaded students were found.\n\n" +
                            "The application attempted to load students from SQLite automatically, but no data was found.\n\n" +
                            "Please import the expected registration report (CSV) and try again.",
                            "Plan from Expected Registration", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }

                    var byDept = Store.Students
                        .Where(s => s.DepartmentId.HasValue)
                        .GroupBy(s => s.DepartmentId!.Value)
                        .Select(g => new { DeptId = g.Key, Count = g.Count() })
                        .OrderByDescending(x => x.Count)
                        .Take(8)
                        .ToList();

                    var hint = string.Join("\n", byDept.Select(x =>
                    {
                        var name = Store.Departments.FirstOrDefault(d => d.Id == x.DeptId)?.Name ?? x.DeptId.ToString();
                        return $"- {name}: {x.Count}";
                    }));

                    MessageBox.Show(
                        $"There are currently no students for the selected department ({SelectedDepartment.Name}).\n" +
                        $"Total loaded students: {total}\n\n" +
                        "Departments with loaded students:\n" + hint + "\n\n" +
                        "Select the correct department and try again.",
                        "Plan from Expected Registration", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var plans = Store.StudentPlans
                    .Where(p => studentIds.Contains(p.StudentId))
                    .ToList();

                if (plans.Count == 0)
                {
                    MessageBox.Show("No expected courses were found for students in this department within the imported data.",
                        "Plan from Expected Registration", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var courseToStudents = plans
                    .GroupBy(p => p.CourseId)
                    .ToDictionary(g => g.Key, g => g.Select(x => x.StudentId).Distinct().Count());

                var updatedRows = 0;
                var rowsWithDemand = 0;
                foreach (var row in PlanRows)
                {
                    if (row is null) continue;

                    var students = courseToStudents.TryGetValue(row.CourseId, out var cnt) ? cnt : 0;
                    row.StudentsThisTerm = students;
                    row.Repeaters = 0;

                    var course = Store.Courses.FirstOrDefault(c => c.Id == row.CourseId);
                    if (course != null && students > 0)
                    {
                        var split = SectionPlanner.GetHoursSplit(Store, course);
                        var cap = split.labHours > 0 ? 15 : 35;
                        var sections = (int)Math.Ceiling(students / (double)cap);
                        row.SectionsRequested = Math.Max(1, sections);
                        rowsWithDemand++;
                    }

                    updatedRows++;
                }

                var selectedLevelNullable = SelectedLevel;
                if (selectedLevelNullable is null || selectedLevelNullable.Value <= 0)
                {
                    MessageBox.Show("Select a level first.", "Plan from Expected Registration", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                var selectedLevel = selectedLevelNullable.Value;

                var removed = 0;
                for (int i = QueuedPlan.Count - 1; i >= 0; i--)
                {
                    var q = QueuedPlan[i];
                    if (q.DepartmentId == deptId && q.Level == selectedLevel)
                    {
                        QueuedPlan.RemoveAt(i);
                        removed++;
                    }
                }

                var added = 0;
                var totalSections = 0;
                foreach (var course in Store.Courses.Where(c => c.DepartmentId == deptId && c.Level == selectedLevel))
                {
                    if (!courseToStudents.TryGetValue(course.Id, out var students) || students <= 0)
                        continue;

                    var split = SectionPlanner.GetHoursSplit(Store, course);
                    var cap = split.labHours > 0 ? 15 : 35;
                    var sections = (int)Math.Ceiling(students / (double)cap);
                    sections = Math.Max(1, sections);

                    var row = new QueuedPlanRow
                    {
                        DepartmentId = deptId,
                        Level = selectedLevel,
                        CourseId = course.Id,
                        CourseName = course.Name,
                        StudentsThisTerm = students,
                        Repeaters = 0,
                        IsPractical = split.labHours > 0,
                        TheoryHoursRequired = split.theoryHours,
                        PracticalHoursRequired = split.labHours,
                        SectionsRequested = sections,
                        PreferredFacultyId = null,
                        PreferredSlotId = null
                    };

                    QueuedPlan.Add(row);
                    added++;
                    totalSections += sections;
                }

                OnPropertyChanged(nameof(PlanRows));
                OnPropertyChanged(nameof(QueuedPlan));
                RaiseAllCanExec();

                MessageBox.Show(
                    "The plan has been prepared from the expected registration data.\n\n" +
                    $"- Department students: {studentIds.Count}\n" +
                    $"- Updated rows (PlanRows): {updatedRows}\n" +
                    $"- Courses with student demand: {rowsWithDemand}\n" +
                    $"- Added to QueuedPlan for level {selectedLevel}: {added} course(s) / {totalSections} section(s)\n\n" +
                    "Next step: click Generate Timetable to produce the sections and scheduling results.",
                    "Plan from Expected Registration",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
