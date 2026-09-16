using MiniTrainerScheduler.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using MiniTrainerScheduler.ViewModels;
using MiniTrainerScheduler.Views;
using MiniTrainerScheduler.Windows;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Controls.Primitives;
using TrainerScheduler.Security;
using TrainerScheduler.Services;
using TrainerScheduler.Network;

namespace MiniTrainerScheduler
{
    public partial class MainWindow : Window
    {
        public MainViewModel VM { get; }

        private FacultySchedulePanel? _facultySchedulePanel;
        private DepartmentSchedulePanel? _departmentSchedulePanel;

        private void SelectAndRenderMainTab(int index)
        {
            if (MainDisplayTabs is null) return;

            if (MainDisplayTabs.SelectedIndex != index)
                MainDisplayTabs.SelectedIndex = index;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    MainDisplayTabs.UpdateLayout();
                    MainDisplayTabs.InvalidateVisual();

                    if (index == 1)
                    {
                        DepartmentScheduleHost?.InvalidateMeasure();
                        DepartmentScheduleHost?.InvalidateVisual();
                    }
                    else if (index == 2)
                    {
                        FacultyScheduleHost?.InvalidateMeasure();
                        FacultyScheduleHost?.InvalidateVisual();
                    }
                }
                catch (Exception ex)
            {
                DiagnosticsLog.TryWrite(ex);
            }
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private int? GetScopedDepartmentIdForSupervisor()
        {
            if (!AuthContext.IsSupervisor) return null;
            var deptId = AuthContext.Current?.DepartmentId;
            return (deptId is null || deptId.Value <= 0) ? null : deptId;
        }

        private void EnsureDepartmentPanelLoaded(MainViewModel vm)
        {
            if (DepartmentScheduleHost is null)
            {
                if (MainDisplayTabs?.SelectedIndex == 1)
                {
                    _ = Dispatcher.BeginInvoke(new Action(() => EnsureDepartmentPanelLoaded(vm)),
                        System.Windows.Threading.DispatcherPriority.ContextIdle);
                }
                return;
            }
            if (_departmentSchedulePanel is not null)
            {
                if (AuthContext.IsSupervisor)
                {
                    var scope = GetScopedDepartmentIdForSupervisor();
                    if (scope is null) return;
                }
                DepartmentScheduleHost.Content = _departmentSchedulePanel;
                return;
            }

            if (vm.Store is null) return;
            var scopeDeptId = GetScopedDepartmentIdForSupervisor();
            if (AuthContext.IsSupervisor && scopeDeptId is null) return;

            _departmentSchedulePanel = new DepartmentSchedulePanel(vm.Store, scopeDeptId);
            DepartmentScheduleHost.Content = _departmentSchedulePanel;
        }

        private void EnsureFacultyPanelLoaded(MainViewModel vm)
        {
            if (FacultyScheduleHost is null)
            {
                if (MainDisplayTabs?.SelectedIndex == 2)
                {
                    _ = Dispatcher.BeginInvoke(new Action(() => EnsureFacultyPanelLoaded(vm)),
                        System.Windows.Threading.DispatcherPriority.ContextIdle);
                }
                return;
            }
            if (_facultySchedulePanel is not null)
            {
                FacultyScheduleHost.Content = _facultySchedulePanel;
                return;
            }

            if (vm.Store is null) return;
            var scopeDeptId = GetScopedDepartmentIdForSupervisor();
            if (AuthContext.IsSupervisor && scopeDeptId is null) return;

            _facultySchedulePanel = new FacultySchedulePanel(vm.Store, scopeDeptId);
            FacultyScheduleHost.Content = _facultySchedulePanel;
        }

        public MainWindow()
        {
            InitializeComponent();
            VM = new MainViewModel();
            DataContext = VM;

            Loaded += async (_, __) =>
            {
                await VM.InitializeDatabaseAsync();

                _ = Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        var idx = MainDisplayTabs?.SelectedIndex ?? 0;
                        if (idx == 1) { EnsureDepartmentPanelLoaded(VM); SelectAndRenderMainTab(1); }
                        else if (idx == 2) { EnsureFacultyPanelLoaded(VM); SelectAndRenderMainTab(2); }
                    }
                    catch (Exception ex)
            {
                DiagnosticsLog.TryWrite(ex);
            }
                }), System.Windows.Threading.DispatcherPriority.Background);
            };
        }

        private void MainDisplayTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!ReferenceEquals(sender, MainDisplayTabs)) return;
            if (DataContext is not MainViewModel vm) return;

            var idx = MainDisplayTabs.SelectedIndex;
            if (idx == 1)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    EnsureDepartmentPanelLoaded(vm);
                    SelectAndRenderMainTab(1);
                }), System.Windows.Threading.DispatcherPriority.ContextIdle);
            }
            else if (idx == 2)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    EnsureFacultyPanelLoaded(vm);
                    SelectAndRenderMainTab(2);
                }), System.Windows.Threading.DispatcherPriority.ContextIdle);
            }
        }

        private void PrintMenu_Click(object sender, RoutedEventArgs e)
{
    try
    {
        var idx = MainDisplayTabs?.SelectedIndex ?? 0;
        switch (idx)
        {
            case 0:
                PrintService.PrintElementUnmirrored(ResultsGrid, "Master Timetable");
                break;

            case 1:
                if (DepartmentScheduleHost?.Content is FrameworkElement deptEl)
                    PrintService.PrintElementUnmirrored(deptEl, "Department Timetable");
                break;

            case 2:
                if (FacultyScheduleHost?.Content is FrameworkElement facEl)
                    PrintService.PrintElementUnmirrored(facEl, "Faculty Timetable");
                break;

            case 3:
                if (StudentScheduleGrid is FrameworkElement grid1 && grid1.IsVisible)
                    PrintService.PrintElementUnmirrored(grid1, "Student Timetable");
                else if (StudentUnassignedGrid is FrameworkElement grid2 && grid2.IsVisible)
                    PrintService.PrintElementUnmirrored(grid2, "Unassigned Students");
                else
                    PrintService.PrintElementUnmirrored(StudentScheduleGrid, "Student Timetable");
                break;
        }
    }
    catch (Exception ex)
    {
        MessageBox.Show(ex.ToString(), "Error", MessageBoxButton.OK, MessageBoxImage.Error);
    }
}



                private async void OpenDeptSchedule_Click(object sender, RoutedEventArgs e)
        {
            if (!AuthContext.IsAdmin && !AuthContext.IsSupervisor)
            {
                MessageBox.Show("This screen is available only to the system administrator or department supervisor.", "Permissions", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (DataContext is not MainViewModel vm || vm.Store is null)
            {
                MessageBox.Show("The data store is not initialized.", "Notice", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int? scopeDeptId = null;
            if (AuthContext.IsSupervisor)
            {
                scopeDeptId = AuthContext.Current?.DepartmentId;
                if (scopeDeptId is null || scopeDeptId.Value <= 0)
                {
                    MessageBox.Show(
                        "The supervisor account is not linked to any department.\n\n" +
                        "Please ask the system administrator to assign a department to this account, then sign in again.",
                        "Supervisor Permissions",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }
            }

            // LAN Client: ensure we show the latest server-side results.
            try
            {
                var lanSettings = LanSettingsStore.LoadOrDefault();
                if (lanSettings.Mode == LanMode.Client && LanClientContext.IsAuthenticated)
                    await vm.TryLoadSavedResultsAsync(showMessageIfEmpty: false);
            }
            catch (Exception ex)
            {
                DiagnosticsLog.TryWrite(ex);
            }

            _departmentSchedulePanel = new DepartmentSchedulePanel(vm.Store, scopeDeptId);
            DepartmentScheduleHost.Content = _departmentSchedulePanel;

            SelectAndRenderMainTab(1);
        }

        private async void OpenResultsPanel_Click(object sender, RoutedEventArgs e)
{
    // LAN Client: pull latest results when opening results.
    try
    {
        if (DataContext is MainViewModel vm && vm.Store is not null)
        {
            var lanSettings = LanSettingsStore.LoadOrDefault();
            if (lanSettings.Mode == LanMode.Client && LanClientContext.IsAuthenticated)
                await vm.TryLoadSavedResultsAsync(showMessageIfEmpty: false);
        }
    }
    catch (Exception ex)
            {
                DiagnosticsLog.TryWrite(ex);
            }

    SelectAndRenderMainTab(0);
}

        private void OpenFacultyTotals_Click(object sender, RoutedEventArgs e)
        {
            if (!AuthContext.IsAdmin)
            {
                MessageBox.Show("This screen is available only to the system administrator.", "Permissions", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var mainVm = (MainViewModel)this.DataContext;
            var win = new FacultyTotalsWindow { DataContext = mainVm };
            win.Owner = this;
            win.Show();
        }
                private async void OpenFacultySchedule_Click(object sender, RoutedEventArgs e)
        {
            if (!AuthContext.IsAdmin && !AuthContext.IsSupervisor)
            {
                MessageBox.Show("This screen is available only to the system administrator or department supervisor.", "Permissions", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var mainVm = (MainViewModel)this.DataContext;

            if (mainVm.Store is null)
                return;

            int? scopeDeptId = null;
            if (AuthContext.IsSupervisor)
            {
                scopeDeptId = AuthContext.Current?.DepartmentId;
                if (scopeDeptId is null || scopeDeptId.Value <= 0)
                {
                    MessageBox.Show(
                        "The supervisor account is not linked to any department.\n\n" +
                        "Please ask the system administrator to assign a department to this account, then sign in again.",
                        "Supervisor Permissions",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }
            }

            // LAN Client: ensure we show the latest server-side results before building the faculty grid.
            try
            {
                var lanSettings = LanSettingsStore.LoadOrDefault();
                if (lanSettings.Mode == LanMode.Client && LanClientContext.IsAuthenticated)
                    await mainVm.TryLoadSavedResultsAsync(showMessageIfEmpty: false);
            }
            catch (Exception ex)
            {
                DiagnosticsLog.TryWrite(ex);
            }

            _facultySchedulePanel = new FacultySchedulePanel(mainVm.Store, scopeDeptId);
            FacultyScheduleHost.Content = _facultySchedulePanel;

            SelectAndRenderMainTab(2);

        }


        
private async void OpenStudentSchedule_Click(object sender, RoutedEventArgs e)
{
    if (!AuthContext.IsAdmin && !AuthContext.IsSupervisor)
    {
        MessageBox.Show("This screen is available only to the system administrator or department supervisor.", "Permissions", MessageBoxButton.OK, MessageBoxImage.Information);
        return;
    }

    if (DataContext is not MainViewModel vm)
        return;

    try
    {
        await vm.RefreshStudentScheduleAsync(showMessageIfEmpty: false);
    }
    catch (Exception ex)
            {
                DiagnosticsLog.TryWrite(ex);
            }

    SelectAndRenderMainTab(3);
}

private void OpenMasterBoard_Click(object sender, RoutedEventArgs e)
        {
            var mainVm = (MainViewModel)this.DataContext;
            var win = new MasterBoardWindow(mainVm.Store);
            win.Owner = this;
            win.Show();
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MiniTrainerScheduler.ViewModels.MainViewModel vm)
                vm.ClearAll();
        }

        private void AddAllDeptPlan_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MiniTrainerScheduler.ViewModels.MainViewModel vm)
                vm.AddAllCurrentDeptToPlan();
        }

        private void About_Click(object sender, RoutedEventArgs e)
        {
            var wnd = new MiniTrainerScheduler.Views.AboutWindow
            {
                Owner = this
            };
            wnd.ShowDialog();
        }

        private void ExportDiagnostics_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MiniTrainerScheduler.ViewModels.MainViewModel vm) return;
            try
            {
                var path = vm.ExportDiagnosticsReport();
                MessageBox.Show($"The diagnostic report has been saved to:\n{path}\n\nPlease share this file with your support contact.",
                    "Diagnostic Report", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void PlanFaculty_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox cb)
            {
                var grid = FindAncestor<DataGrid>(cb);
                if (grid != null)
                {
                    grid.CommitEdit(DataGridEditingUnit.Cell, true);
                    grid.CommitEdit(DataGridEditingUnit.Row, true);
                }
            }
        }

        private void PickPlanSlots_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (DataContext is not MainViewModel vm || vm.Store is null)
                {
                    MessageBox.Show("The data store is not initialized.", "Notice",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (vm.Store.Slots is null || vm.Store.Slots.Count == 0)
                {
                    MessageBox.Show("No time slots are available in the system.", "Notice",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (sender is not Button btn)
                    return;

                if (btn.DataContext is not CoursePlanRow row)
                    return;

                var tag = btn.Tag?.ToString() ?? "THEORY";
                bool isTheory = string.Equals(tag, "THEORY", StringComparison.OrdinalIgnoreCase);

                int required = isTheory ? row.TheorySlotsNeeded : row.PracticalSlotsNeeded;
                if (required <= 0)
                {
                    MessageBox.Show("This component does not contain selectable hours.", "Notice",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var otherSelected = isTheory ? row.PracticalSlotIds : row.TheorySlotIds;
                var preselected = isTheory ? row.TheorySlotIds : row.PracticalSlotIds;
                var header = isTheory ? "Select lecture time slots" : "Select lab time slots";

                while (true)
                {
                    var picker = new ManualSlotsPickerWindow(vm.Store, required, preselected, header, requireExact: true)
                    {
                        Owner = this,
                        Title = isTheory
                            ? $"Select lecture time slots — {row.CourseName}"
                            : $"Select lab time slots — {row.CourseName}"
                    };

                    if (picker.ShowDialog() != true)
                        break;

                    var ids = picker.SelectedSlots
                        .Select(s => s.Id)
                        .Distinct()
                        .Take(required)
                        .ToList();

                    var overlap = ids
                        .Intersect(otherSelected ?? Enumerable.Empty<int>())
                        .Distinct()
                        .ToList();

                    if (overlap.Count > 0)
                    {
                        MessageBox.Show(
                            "The same time slot cannot be used for both the lecture and lab components of this course.\n\nConflicting time slots:\n" + FormatSlotsForMessage(vm.Store, overlap),
                            "Time Conflict", MessageBoxButton.OK, MessageBoxImage.Warning);
                        preselected = ids;
                        continue;
                    }

                    if (isTheory)
                        row.TheorySlotIds = ids;
                    else
                        row.PracticalSlotIds = ids;

                    OptionalCoursesGrid?.CommitEdit(DataGridEditingUnit.Cell, true);
                    OptionalCoursesGrid?.CommitEdit(DataGridEditingUnit.Row, true);
                    break;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void PickQueuedSlots_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (DataContext is not MainViewModel vm || vm.Store is null)
                {
                    MessageBox.Show("The data store is not initialized.", "Notice",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (vm.Store.Slots is null || vm.Store.Slots.Count == 0)
                {
                    MessageBox.Show("No time slots are available in the system.", "Notice",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (sender is not Button btn)
                    return;

                if (btn.DataContext is not QueuedPlanRow row)
                    return;

                var tag = btn.Tag?.ToString() ?? "THEORY";
                bool isTheory = string.Equals(tag, "THEORY", StringComparison.OrdinalIgnoreCase);

                int required = isTheory ? row.TheorySlotsNeeded : row.PracticalSlotsNeeded;
                if (required <= 0)
                {
                    MessageBox.Show("This component does not contain selectable hours.", "Notice",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var otherSelected = isTheory ? row.PracticalSlotIds : row.TheorySlotIds;
                var preselected = isTheory ? row.TheorySlotIds : row.PracticalSlotIds;
                var header = isTheory ? "Select lecture time slots" : "Select lab time slots";

                while (true)
                {
                    var picker = new ManualSlotsPickerWindow(vm.Store, required, preselected, header, requireExact: true)
                    {
                        Owner = this,
                        Title = isTheory
                            ? $"Select lecture time slots — {row.CourseName}"
                            : $"Select lab time slots — {row.CourseName}"
                    };

                    if (picker.ShowDialog() != true)
                        break;

                    var ids = picker.SelectedSlots
                        .Select(s => s.Id)
                        .Distinct()
                        .Take(required)
                        .ToList();

                    var overlap = ids
                        .Intersect(otherSelected ?? Enumerable.Empty<int>())
                        .Distinct()
                        .ToList();

                    if (overlap.Count > 0)
                    {
                        MessageBox.Show(
                            "The same time slot cannot be used for both the lecture and lab components of this course.\n\nConflicting time slots:\n" + FormatSlotsForMessage(vm.Store, overlap),
                            "Time Conflict", MessageBoxButton.OK, MessageBoxImage.Warning);
                        preselected = ids;
                        continue;
                    }

                    if (isTheory)
                        row.TheorySlotIds = ids;
                    else
                        row.PracticalSlotIds = ids;

                    QueuedPlanGrid?.CommitEdit(DataGridEditingUnit.Cell, true);
                    QueuedPlanGrid?.CommitEdit(DataGridEditingUnit.Row, true);
                    break;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static T? FindAncestor<T>(DependencyObject child) where T : DependencyObject
        {
            var current = child;
            while (current != null)
            {
                if (current is T typed) return typed;
                current = VisualTreeHelper.GetParent(current);
            }
            return null;
        }

private void OpenDataAdmin_Click(object sender, RoutedEventArgs e)
{
    if (!AuthContext.IsAdmin)
    {
        MessageBox.Show("This screen is available only to the system administrator.", "Permissions", MessageBoxButton.OK, MessageBoxImage.Information);
        return;
    }

    try
    {
        var win = new MiniTrainerScheduler.Views.DataAdminWindow(VM)
        {
            Owner = this
        };
        win.ShowDialog();
    }
    catch (Exception ex)
    {
        MessageBox.Show($"Unable to open the Data Administration screen.\n\n{ex.Message}", "Data Administration",
            MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}

        private void OpenUserAdmin_Click(object sender, RoutedEventArgs e)
        {
            if (!AuthContext.IsAdmin)
            {
                MessageBox.Show("This screen is available only to the system administrator.", "Permissions", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                var win = new MiniTrainerScheduler.Views.UserAdminWindow
                {
                    Owner = this
                };
                win.ShowDialog();
            }
            catch (Exception ex)
            {
            UiError.Show("Users", "Unable to open the Users window.", ex);
            }
        }

        private void OpenLanSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var win = new MiniTrainerScheduler.Views.LanSettingsWindow
                {
                    Owner = this,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };
                win.ShowDialog();
            }
            catch (Exception ex)
            {
                UiError.Show("Network Settings", "Unable to open the Network Settings window.", ex);
            }
        }

        private void OpenCourseFacultyOverrides_Click(object sender, RoutedEventArgs e)
        {
            if (!AuthContext.IsAdmin && !AuthContext.IsSupervisor)
            {
                MessageBox.Show("This screen is available only to the administrator or supervisor.", "Permissions", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                if (DataContext is not MainViewModel vm)
                    return;

                var win = new MiniTrainerScheduler.Views.CourseFacultyOverridesWindow(vm)
                {
                    Owner = this,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };
                win.ShowDialog();
            }
            catch (Exception ex)
            {
                UiError.Show("Assign Faculty to Courses", "Unable to open the Assign Faculty to Courses window.", ex);
            }
        }

        private async void SaveScheduledResults_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MainViewModel vm)
                return;

            await vm.SaveScheduledResultsAsync();
        }

        private async void RefreshFromServer_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MainViewModel vm)
                return;

            await vm.RefreshLanFromServerAsync(showMessageIfNoChange: true);
        }

        private void ToggleAutoRefresh_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MainViewModel vm)
                return;

            vm.ToggleLanAutoRefresh();
        }

        private void Logout_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Clear current session
                AuthContext.Clear();

                // We will close this window and open a new one after successful login.
                Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;

                var login = new LoginWindow
                {
                    Owner = this,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };

                var ok = login.ShowDialog() == true;
                if (!ok)
                {
                    Application.Current.Shutdown();
                    return;
                }

                var newMain = new MainWindow();
                Application.Current.MainWindow = newMain;
                Application.Current.ShutdownMode = ShutdownMode.OnMainWindowClose;

                newMain.Show();
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Unable to sign out and return to the sign-in screen.\n\n{ex.Message}",
                    "Sign Out",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }




        private static string FormatSlotsForMessage(DataStore store, IEnumerable<int> slotIds)
        {
            if (slotIds is null) return string.Empty;

            var ids = slotIds.Distinct().ToList();
            if (ids.Count == 0) return string.Empty;

            try
            {
                if (store?.Slots is null) return string.Join(", ", ids);
                var byId = store.Slots.ToDictionary(s => s.Id);
                return string.Join("\n", ids.Select(id => byId.TryGetValue(id, out var slot) ? slot.ToString() : $"Slot #{id}"));
            }
            catch
            {
                return string.Join(", ", ids);
            }
        }
    }
}
