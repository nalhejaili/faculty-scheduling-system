using TrainerScheduler.Services;
﻿using System;
using System.Linq;
using System.Reflection;
using System.Collections;
using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows;
using MiniTrainerScheduler.Models;
using TrainerScheduler.Network;

namespace MiniTrainerScheduler.ViewModels
{
    public sealed partial class MainViewModel
    {
        private ICommand? _clearScheduleCommand;
        public ICommand ClearScheduleCommand => _clearScheduleCommand ??= CreateClearCommand();

        private ICommand? _clearDepartmentCommand;
        public ICommand ClearDepartmentCommand => _clearDepartmentCommand ??= CreateClearDepartmentCommand();

        private ICommand CreateClearCommand()
        {
            var t = typeof(RelayCommand);

            var c1 = t.GetConstructor(new[] { typeof(Action<object?>) });
            if (c1 != null) return (ICommand)c1.Invoke(new object?[] { new Action<object?>(_ => AdminClearPlanAndGenerated()) });

            var c2 = t.GetConstructor(new[] { typeof(Action) });
            if (c2 != null) return (ICommand)c2.Invoke(new object?[] { new Action(AdminClearPlanAndGenerated) });

            var c3 = t.GetConstructor(new[] { typeof(Action<object?>), typeof(Predicate<object?>) });
            if (c3 != null) return (ICommand)c3.Invoke(new object?[] {
                new Action<object?>(_ => AdminClearPlanAndGenerated()),
                new Predicate<object?>(_ => true)
            });

            var c4 = t.GetConstructor(new[] { typeof(Action), typeof(Func<bool>) });
            if (c4 != null) return (ICommand)c4.Invoke(new object?[] {
                new Action(AdminClearPlanAndGenerated),
                new Func<bool>(() => true)
            });

            throw new InvalidOperationException("Unsupported RelayCommand signature.");
        }


        private ICommand CreateClearDepartmentCommand()
        {
            var t = typeof(RelayCommand);

            var c1 = t.GetConstructor(new[] { typeof(Action<object?>) });
            if (c1 != null) return (ICommand)c1.Invoke(new object?[] { new Action<object?>(_ => ClearDepartmentGeneratedOnly()) });

            var c2 = t.GetConstructor(new[] { typeof(Action) });
            if (c2 != null) return (ICommand)c2.Invoke(new object?[] { new Action(ClearDepartmentGeneratedOnly) });

            var c3 = t.GetConstructor(new[] { typeof(Action<object?>), typeof(Predicate<object?>) });
            if (c3 != null) return (ICommand)c3.Invoke(new object?[] {
                new Action<object?>(_ => ClearDepartmentGeneratedOnly()),
                new Predicate<object?>(_ => true)
            });

            var c4 = t.GetConstructor(new[] { typeof(Action), typeof(Func<bool>) });
            if (c4 != null) return (ICommand)c4.Invoke(new object?[] {
                new Action(ClearDepartmentGeneratedOnly),
                new Func<bool>(() => true)
            });

            throw new InvalidOperationException("Unsupported RelayCommand signature.");
        }


        /// <summary>
        /// Admin-only:
        /// - clears the plan UI lists (PlanRows/QueuedPlan)
        /// - deletes ALL generated assignments across ALL departments from SQLite (keeps MANUAL)
        /// - bumps server revision so LAN clients re-pull
        /// </summary>
        public async void AdminClearPlanAndGenerated()
        {
            if (!IsAdmin)
            {
                MessageBox.Show("This action is available only to the system administrator.", "Permissions", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var res = MessageBox.Show(
                "All scheduling results for all departments and levels will be deleted, including generated and manual entries.\n\nDo you want to continue?",
                "Confirm Clear",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (res != MessageBoxResult.Yes)
                return;

            try
            {
                // 1) Clear plan UI (does not touch DB)
                ResetCollection("PlanRows");
                ResetCollection("QueuedPlan");

                // 2) Clear DB generated rows (all departments)
                var (ok, err, removed) = await _sqlite.ClearAllGeneratedAssignmentsAsync();
                if (!ok)
                {
                    MessageBox.Show($"Unable to clear the database.\n\n{err}", "SQLite", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // 3) Reload assignments from DB (admin sees ALL)
                await _sqlite.TryLoadAssignmentsAsync(Store, scopeDepartmentId: null);
                RefreshRows();

                // 4) Bump LAN revision if this machine is the server
                try
                {
                    var lanSettings = LanSettingsStore.LoadOrDefault();
                    if (lanSettings.Mode == LanMode.Server && LanServerManager.IsRunning)
                        await _sqlite.BumpServerRevisionAsync();
                }
                catch
                {
                    // ignore
                }

                SetLanStatus($"Scheduling results cleared (Removed={removed})");
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        /// <summary>
        /// Supervisor button:
        /// - deletes ALL generated assignments for the supervisor's department (all levels) (keeps MANUAL)
        /// - calls the server in LAN client mode, so results don't come back
        /// </summary>
        public async void ClearDepartmentGeneratedOnly()
        {
            // Only meaningful for supervisors; admins have a stronger button.
            if (!IsSupervisor)
            {
                MessageBox.Show("This action is intended for supervisors only.", "Permissions", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            int deptId = GetSupervisorDepartment()?.Id ?? 0;
            if (deptId <= 0)
            {
                MessageBox.Show("Unable to determine the supervisor department.", "LAN", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var res = MessageBox.Show(
                "All scheduling results for your department and all its levels will be deleted, including generated and manual entries.\n\nDo you want to continue?",
                "Confirm Clear",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (res != MessageBoxResult.Yes)
                return;

            var lanSettings = LanSettingsStore.LoadOrDefault();

            try
            {
                // LAN client: delete on server
                if (lanSettings.Mode == LanMode.Client && LanClientContext.IsAuthenticated)
                {
                    SetLanStatus("Clearing the department timetable on the server...");
                    var (ok, err, removed) = await LanDataClient.ClearAssignmentsByDepartmentAsync(deptId);
                    if (!ok)
                    {
                        SetLanStatusErrorThrottled($"Failed to clear the department timetable on the server: {err}");
                        MessageBox.Show(err, "LAN", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    // Pull to confirm and update UI
                    await LanDataClient.TryPullAssignmentsAndBlocksIntoStoreAsync(Store);
                    RefreshRows();
                    SetLanStatus($"Department results cleared (Removed={removed}) — Results={Store.Assignments.Count}");
                    return;
                }

                // Local/Server (rare for supervisor): clear locally in SQLite but enforce scope
                var (lok, lerr, lremoved) = await _sqlite.ClearGeneratedAssignmentsForDepartmentAsync(deptId, scopeDepartmentId: deptId);
                if (!lok)
                {
                    MessageBox.Show(lerr, "SQLite", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                await _sqlite.TryLoadAssignmentsAsync(Store, scopeDepartmentId: deptId);
                RefreshRows();

                try
                {
                    if (lanSettings.Mode == LanMode.Server && LanServerManager.IsRunning)
                        await _sqlite.BumpServerRevisionAsync();
                }
                catch (Exception ex)
            {
                DiagnosticsLog.TryWrite(ex);
            }

                SetLanStatus($"Department results cleared locally (Removed={lremoved})");
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// </summary>
        /// <summary>
        ///
        /// </summary>
        public async void ClearGeneratedOnly()
        {
            int? deptId = IsSupervisor
                ? GetSupervisorDepartment()?.Id
                : SelectedDepartment?.Id;

            int? level = SelectedLevel;


            var lanSettingsEarly = LanSettingsStore.LoadOrDefault();
            if (lanSettingsEarly.Mode == LanMode.Client && LanClientContext.IsAuthenticated)
            {
                if (!(deptId is int _did && level is int _lv && _lv > 0))
                {
                    MessageBox.Show("Select the department and level before clearing results (LAN).", "LAN", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
            }

            if (Store?.Assignments != null)
            {
                if (deptId is int did && level is int lv && lv > 0 && Store.Courses is not null && Store.Courses.Count > 0)
                {
                    var courseIds = Store.Courses
                        .Where(c => c.DepartmentId == did && c.Level == lv)
                        .Select(c => c.Id)
                        .ToHashSet();

                    if (courseIds.Count > 0)
                    {
                        var keep = Store.Assignments
                            .Where(a =>
                                a.DepartmentId != did ||
                                !courseIds.Contains(a.CourseId) ||
                                string.Equals(a.Status, "MANUAL", StringComparison.OrdinalIgnoreCase))
                            .ToList();

                        Store.Assignments.Clear();
                        foreach (var a in keep)
                            Store.Assignments.Add(a);
                    }
                    else
                    {
                        if (IsSupervisor)
                        {
                            var keep = Store.Assignments
                                .Where(a => a.DepartmentId != did ||
                                            string.Equals(a.Status, "MANUAL", StringComparison.OrdinalIgnoreCase))
                                .ToList();

                            Store.Assignments.Clear();
                            foreach (var a in keep)
                                Store.Assignments.Add(a);
                        }
                        else
                        {
                            var manual = Store.Assignments
                                .Where(a => string.Equals(a.Status, "MANUAL", StringComparison.OrdinalIgnoreCase))
                                .ToList();

                            Store.Assignments.Clear();
                            foreach (var m in manual)
                                Store.Assignments.Add(m);
                        }
                    }
                }
                else
                {
                    if (IsSupervisor)
                    {
                        if (deptId is int sdid)
                        {
                            var keep = Store.Assignments
                                .Where(a => a.DepartmentId != sdid ||
                                            string.Equals(a.Status, "MANUAL", StringComparison.OrdinalIgnoreCase))
                                .ToList();

                            Store.Assignments.Clear();
                            foreach (var a in keep)
                                Store.Assignments.Add(a);
                        }
                    }
                    else
                    {
                        var manual = Store.Assignments
                            .Where(a => string.Equals(a.Status, "MANUAL", StringComparison.OrdinalIgnoreCase))
                            .ToList();

                        Store.Assignments.Clear();
                        foreach (var m in manual)
                            Store.Assignments.Add(m);
                    }
                }
            }

            ResetCollection("Rows");
            ResetCollection("GeneratedRows");
            ResetCollection("PreviewRows");
            ResetCollection("FacultyTotals");
            ResetCollection("Issues");
            ResetCollection("PlanningIssues");
            ResetCollection("StudentSchedule");
            ResetCollection("WeekGrid");
            ResetCollection("WeeklyBoard");
            ResetCollection("BoardCells");
            ResetCollection("CalendarCells");

            OnPropertyChanged(nameof(Store));
            OnPropertyChanged(nameof(Rows));

            try
            {
                var lanSettings = lanSettingsEarly;
                if (lanSettings.Mode == LanMode.Client && LanClientContext.IsAuthenticated)
                {
                    if (deptId is int did && level is int lv && lv > 0)
                    {
                        SetLanStatus($"Clearing level {lv} results on the server...");
                        var (ok, err, removed) = await LanDataClient.ClearAssignmentsByLevelAsync(did, lv);
                        if (!ok)
                        {
                            SetLanStatusErrorThrottled($"Failed to clear scheduling results on the server: {err}");
                            MessageBox.Show(
                                $"The results were cleared locally, but they could not be cleared on the server.\n\n{err}\n\nThe results may reappear after refresh.",
                                "LAN",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                        }
                        else
                        {
                            SetLanStatus($"Server results cleared (Removed={removed})...");
                            try
                            {
                                var rev = await LanDataClient.TryGetRevisionAsync();
                                if (rev is not null) _lanLastRevision = rev.Value.revision;
                            }
                            catch
                            {
                                // ignore
                            }

                            try
                            {
                                var store = Store;
                                if (store is not null)
                                {
                                    await LanDataClient.TryPullAssignmentsAndBlocksIntoStoreAsync(store);
                                    RefreshRows();
                                    SetLanStatus($"Refresh confirmed after clearing — Results={store.Assignments.Count} Blocks={store.FacultySlotBlocks.Count}");
                                }
                            }
                            catch
                            {
                                // ignore
                            }
                        }
                    }

                    return;
                }

                if (!IsSupervisor && deptId is int adid && level is int alv && alv > 0)
                {
                    var (ok, err, _) = await _sqlite.ClearAssignmentsByDeptLevelAsync(adid, alv, scopeDepartmentId: null);
                    if (!ok)
                    {
                        MessageBox.Show(
                            $"The results were cleared locally, but the database could not be updated.\n\n{err}",
                            "SQLite",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                        return;
                    }

                    SetLanStatus($"SQLite results cleared (Department {adid}, Level {alv}).");

                    try
                    {
                        if (lanSettings.Mode == LanMode.Server && LanServerManager.IsRunning)
                            await _sqlite.BumpServerRevisionAsync();
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
        }


        public void ClearAll()
        {
            if (IsSupervisor)
            {
                MessageBox.Show(
                    "The supervisor account does not have permission to clear all data.\n\nYou can only clear the results for your department.",
                    "Supervisor Permissions",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                ClearGeneratedOnly();
                return;
            }

            Store?.Assignments?.Clear();

            ResetCollection("Rows");
            ResetCollection("PlanRows");
            ResetCollection("QueuedPlan");
            ResetCollection("GeneratedRows");
            ResetCollection("PreviewRows");
            ResetCollection("FacultyTotals");
            ResetCollection("Issues");
            ResetCollection("PlanningIssues");
            ResetCollection("StudentSchedule");
            ResetCollection("WeekGrid");
            ResetCollection("WeeklyBoard");
            ResetCollection("BoardCells");
            ResetCollection("CalendarCells");

            ResetIntish("SelectedDepartmentId");
            ResetIntish("SelectedLevel");
            ResetIntish("SelectedKindIndex");

            OnPropertyChanged(nameof(PlanRows));
            OnPropertyChanged(nameof(QueuedPlan));
        }

        // ===== Helpers =====
        private void ResetCollection(string propName)
        {
            var p = GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (p == null) return;

            var val = p.GetValue(this);

            if (val is IList list)
            {
                list.Clear();
                OnPropertyChanged(propName);
                return;
            }

            var pt = p.PropertyType;
            if (pt.IsGenericType && pt.GetGenericTypeDefinition() == typeof(ObservableCollection<>))
            {
                var empty = Activator.CreateInstance(pt);
                if (p.CanWrite) p.SetValue(this, empty);
                else
                {
                    var field = GetType().GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                        .FirstOrDefault(f => f.FieldType == pt && f.Name.Contains(propName, StringComparison.OrdinalIgnoreCase));
                    if (field != null) field.SetValue(this, empty);
                }
                OnPropertyChanged(propName);
            }
        }

        private void ResetValueIfExists(string propName, object? value)
        {
            var p = GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (p == null || !p.CanWrite) return;
            p.SetValue(this, value);
            OnPropertyChanged(propName);
        }

        private void ResetIntish(string propName)
        {
            var p = GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (p == null || !p.CanWrite) return;

            var t = p.PropertyType;
            if (t == typeof(int)) p.SetValue(this, default(int));
            else if (t == typeof(int?)) p.SetValue(this, null);
            else if (t == typeof(byte)) p.SetValue(this, default(byte));
            else if (t == typeof(byte?)) p.SetValue(this, null);
            else if (t == typeof(short)) p.SetValue(this, default(short));
            else if (t == typeof(short?)) p.SetValue(this, null);

            OnPropertyChanged(propName);
        }
    }
}
