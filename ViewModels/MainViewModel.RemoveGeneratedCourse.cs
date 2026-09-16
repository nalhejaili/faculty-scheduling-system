using TrainerScheduler.Services;
﻿using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using MiniTrainerScheduler.Models;
using MiniTrainerScheduler.Services;
using TrainerScheduler.Network;
using TrainerScheduler.Security;

namespace MiniTrainerScheduler.ViewModels
{
    public sealed partial class MainViewModel
    {
        public AssignmentRow? SelectedRow { get; set; }

        private ICommand? _removeGeneratedForCourseCommand;
        public ICommand RemoveGeneratedForCourseCommand =>
            _removeGeneratedForCourseCommand ??=
                new RelayCommand(
                    param => RemoveGeneratedForCourse(param as AssignmentRow),
                    param => CanRemoveGeneratedForCourse(param as AssignmentRow)
                );

        private bool CanRemoveGeneratedForCourse(AssignmentRow? row = null)
        {
            var target = row ?? SelectedRow;

            return Store?.Assignments != null
                   && Store.Assignments.Count > 0
                   && target != null
                   && target.Source != null;
        }

        private void RemoveGeneratedForCourse(AssignmentRow? row = null)
        {
            _ = RemoveGeneratedForCourseAsync(row);
        }

        private async Task RemoveGeneratedForCourseAsync(AssignmentRow? row = null)
        {
            if (!CanRemoveGeneratedForCourse(row))
                return;

            var store = Store!;
            var targetRow = row ?? SelectedRow!;
            var src = targetRow.Source!;

            var course = store.Courses.FirstOrDefault(c => c.Id == src.CourseId);
            var dept = store.Departments.FirstOrDefault(d => d.Id == src.DepartmentId);

            string courseName = course?.Name
                                ?? targetRow.Course
                                ?? $"Course ID {src.CourseId}";

            string deptName = dept?.Name
                              ?? targetRow.Department
                              ?? $"Department ID {src.DepartmentId}";

            var toRemove = store.Assignments
                .Where(a => a.DepartmentId == src.DepartmentId &&
                            a.CourseId == src.CourseId &&
                            a.SectionIndex == src.SectionIndex)
                .ToList();

            if (toRemove.Count == 0)
            {
                MessageBox.Show(
                    "There are no class entries for this course that can be deleted.",
                    "Notice",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var msg =
                $"{toRemove.Count} class entries will be deleted from the scheduling results for this course:\n\n" +
                $"Department: {deptName}\n" +
                $"Course: {courseName}\n" +
                $"Section: {src.SectionIndex}\n\n" +
                "Do you want to continue?";

            var result = MessageBox.Show(
                msg,
                "Confirm Delete from Scheduling Results",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
                return;

            // In LAN Client mode, apply delete on the server so the change propagates to Admin + other clients.
            try
            {
                var lanSettings = LanSettingsStore.LoadOrDefault();
                if (lanSettings.Mode == LanMode.Client && LanClientContext.IsAuthenticated)
                {
                    SetLanStatus("Deleting the course from the server...");

                    var deptId = src.DepartmentId;
                    if (AuthContext.IsSupervisor && AuthContext.Current?.DepartmentId is int did)
                        deptId = did;

                    var (ok, err, removedCount) = await LanDataClient.DeleteGeneratedForCourseSectionAsync(deptId, src.CourseId, src.SectionIndex);
                    if (!ok)
                    {
                        SetLanStatusErrorThrottled("Failed to delete the course on the server.", null);
                        MessageBox.Show($"Unable to delete the course on the server.\n\n{err}", "LAN", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    // Pull fresh data (server truth).
                    await LanDataClient.TryPullAssignmentsAndBlocksIntoStoreAsync(store);
                    RefreshRows();
                    OnPropertyChanged(nameof(Store));
                    SetLanStatus($"Deleted {removedCount} class entries from the server.");
                    return;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "LAN", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Server/Local mode:
            // - Persist delete to SQLite so it doesn't come back.
            // - If this machine is the LAN server, bump revision so clients re-pull.
            try
            {
                int? scopeDeptId = null;
                if (AuthContext.IsSupervisor && AuthContext.Current?.DepartmentId is int sdid)
                    scopeDeptId = sdid;

                var (ok, err, removed) = await _sqlite.DeleteGeneratedAssignmentsForCourseSectionAsync(
                    src.DepartmentId,
                    src.CourseId,
                    src.SectionIndex,
                    scopeDepartmentId: scopeDeptId);

                if (!ok)
                {
                    MessageBox.Show(err, "SQLite", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                await _sqlite.TryLoadAssignmentsAsync(store, scopeDepartmentId: scopeDeptId);
                RefreshRows();
                OnPropertyChanged(nameof(Store));

                try
                {
                    var lanSettings = LanSettingsStore.LoadOrDefault();
                    if (lanSettings.Mode == LanMode.Server && LanServerManager.IsRunning)
                        await _sqlite.BumpServerRevisionAsync();
                }
                catch (Exception ex)
            {
                DiagnosticsLog.TryWrite(ex);
            }

                SetLanStatus($"Deleted {removed} class entries.");
            }
            catch
            {
                // fallback: still remove from memory
                foreach (var a in toRemove)
                    store.Assignments.Remove(a);

                RefreshRows();
                OnPropertyChanged(nameof(Store));
            }
        }

    }
}
