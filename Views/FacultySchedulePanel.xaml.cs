using Microsoft.Win32;
using MiniTrainerScheduler.Models;
using MiniTrainerScheduler.Services;
using MiniTrainerScheduler.ViewModels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;

namespace MiniTrainerScheduler.Views
{
    public partial class FacultySchedulePanel : UserControl
    {
        public FacultySchedulePanel(DataStore store, int? scopeDepartmentId = null)
        {
            InitializeComponent();
            DataContext = new FacultyScheduleViewModel(store, scopeDepartmentId);
        }

        private async void RefreshBtn_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is FacultyScheduleViewModel vm)
            {
                await vm.ReloadFromDatabaseAsync();
            }
        }

        private void ExportCsvBtn_Click(object sender, RoutedEventArgs e)
        {
            var vm = DataContext as FacultyScheduleViewModel;
            if (vm is null || !vm.Rows.Any())
                return;

            var sfd = new SaveFileDialog
            {
                Filter = "CSV (*.csv)|*.csv",
                FileName = $"FacultyTimetable_{vm.SelectedFaculty?.Name ?? "FacultyMember"}.csv"
            };
            if (sfd.ShowDialog() != true)
                return;

            using var sw = new StreamWriter(sfd.FileName, false, new UTF8Encoding(true));
            sw.WriteLine("Department,Course,Section,Time Slot,Hours,Room,Status");
            foreach (var r in vm.Rows)
                sw.WriteLine($"{r.Department},{r.Course},{r.Section},{r.Slot},{r.Hours},{r.Room},{r.Status}");

            MessageBox.Show("The file has been exported.", "CSV",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void PrintBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (DataContext is not FacultyScheduleViewModel vm)
                    return;

                vm.RefreshRows();
                vm.BuildGeneralRows();

                var win = new FacultyScheduleWindow(vm);
                win.PrintSilent();
                win.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), "Print Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenSlotsPicker_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (DataContext is not FacultyScheduleViewModel vm)
                    return;

                if (vm.Store is null)
                {
                    MessageBox.Show("The data store is not initialized.", "Notice",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (vm.SelectedCourse is null)
                {
                    MessageBox.Show("Please select a course first.", "Notice",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                if (vm.SelectedFaculty is null)
                {
                    MessageBox.Show("Please select a faculty member first.", "Notice",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                string selectedKind = vm.SelectedKind;
                if (string.IsNullOrWhiteSpace(selectedKind))
                    selectedKind = "THEORY";

                var (theoryHours, labHours, _) = SectionPlanner.GetHoursSplit(vm.Store, vm.SelectedCourse);
                double required = selectedKind == "LAB" ? labHours : theoryHours;
                if (required <= 0)
                {
                    MessageBox.Show(
                        selectedKind == "LAB"
                            ? "This course does not include lab hours."
                            : "This course does not include lecture hours.",
                        "Notice",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                int maxSlots = (int)Math.Ceiling(required);
                if (maxSlots <= 0) maxSlots = 1;

                if (vm.SelectedSectionIndex <= 0)
                    vm.SelectedSectionIndex = 1;

                var preselected = vm.Store.Assignments
                    .Where(a => a.FacultyId == vm.SelectedFaculty.Id
                                && a.CourseId == vm.SelectedCourse.Id
                                && a.SectionIndex == vm.SelectedSectionIndex
                                && string.Equals(a.Kind ?? string.Empty, selectedKind, StringComparison.OrdinalIgnoreCase)
                                && (vm.ScopeDepartmentId is null || a.DepartmentId == vm.ScopeDepartmentId.Value))
                    .Select(a => a.SlotId)
                    .Distinct()
                    .ToList();

                string kindAr = AcademicEnglishText.KindLabel(selectedKind);
                var picker = new ManualSlotsPickerWindow(
                    vm.Store,
                    maxSlots,
                    preselectedSlotIds: preselected,
                    headerText: $"Select weekly class meeting times ({kindAr})",
                    requireExact: true);

                var ownerWindow = Window.GetWindow(this);
                if (ownerWindow != null)
                    picker.Owner = ownerWindow;

                if (picker.ShowDialog() == true)
                {
                    var slots = picker.SelectedSlots;

                    var selectedSlotIds = slots.Select(s => s.Id).Distinct().ToHashSet();

                    var occupiedByOther = vm.Store.Assignments
                        .Where(a => a.FacultyId == vm.SelectedFaculty.Id
                                    && (vm.ScopeDepartmentId is null || a.DepartmentId == vm.ScopeDepartmentId.Value)
                                    && !(a.CourseId == vm.SelectedCourse.Id
                                         && a.SectionIndex == vm.SelectedSectionIndex
                                         && string.Equals(a.Kind ?? string.Empty, selectedKind, StringComparison.OrdinalIgnoreCase)))
                        .Select(a => a.SlotId)
                        .ToHashSet();

                    var blockedByFaculty = (vm.Store.FacultySlotBlocks ?? new List<FacultySlotBlock>())
                        .Where(b => b.FacultyId == vm.SelectedFaculty.Id)
                        .Select(b => b.SlotId)
                        .ToHashSet();

                    var conflicts = selectedSlotIds
                        .Where(id => occupiedByOther.Contains(id) || blockedByFaculty.Contains(id))
                        .Distinct()
                        .ToList();

                    if (conflicts.Count > 0)
                    {
                        var labels = conflicts
                            .Select(id => vm.Store.Slots.FirstOrDefault(s => s.Id == id))
                            .Where(s => s != null)
                            .Select(s => AcademicEnglishText.Slot(s!))
                            .Take(12)
                            .ToList();

                        string msg = "A conflicting time slot cannot be selected for this faculty member.\n\n" +
                                     "These time slots are already occupied by another class or blocked by a faculty release:\n" +
                                     string.Join("\n", labels);

                        MessageBox.Show(msg, "Time Conflict", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    var toRemove = vm.Store.Assignments
                        .Where(a => a.FacultyId == vm.SelectedFaculty.Id
                                    && a.CourseId == vm.SelectedCourse.Id
                                    && a.SectionIndex == vm.SelectedSectionIndex
                                    && string.Equals(a.Kind ?? string.Empty, selectedKind, StringComparison.OrdinalIgnoreCase)
                                    && (vm.ScopeDepartmentId is null || a.DepartmentId == vm.ScopeDepartmentId.Value))
                        .ToList();
                    foreach (var a in toRemove)
                        vm.Store.Assignments.Remove(a);

                    foreach (var slot in slots)
                    {
                        var assignment = new Assignment(
                            vm.SelectedCourse.DepartmentId,
                            vm.SelectedCourse.Id,
                            vm.SelectedSectionIndex,
                            slot.Id,
                            vm.SelectedFaculty.Id,
                            null,
                            "MANUAL",
                            selectedKind   // ← LAB / THEORY
                        );

                        ManualPlanBridge.RegisterManualAssignment(vm.Store, assignment);
                    }

                    // Refresh UI and auto-save to SQLite so other accounts/devices can see the change.
                    vm.RefreshRows();
                    vm.BuildGeneralRows();
                    vm.RequestAutoSave();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.ToString(),
                    "Time-Slot Picker Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void SelectKind_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not FacultyScheduleViewModel vm)
                return;

            if (sender is not Button btn)
                return;

            var kind = btn.Tag?.ToString();
            if (kind is "THEORY" or "LAB")
            {
                vm.SelectedKind = kind;

                OpenSlotsPicker_Click(sender, e);
            }
        }

        private void AddBlockWithPicker_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not FacultyScheduleViewModel vm)
                return;

            if (vm.SelectedFaculty is null)
            {
                MessageBox.Show("Please select a faculty member first.", "Notice",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (vm.Store is null || vm.Store.Slots == null || vm.Store.Slots.Count == 0)
            {
                MessageBox.Show("No time slots are available in the system.", "Notice",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int maxSlots = vm.Store.Slots.Count;

            var picker = new ManualSlotsPickerWindow(vm.Store, maxSlots)
            {
                Title = "Select Time Slots for Faculty Release"
            };

            var ownerWindow = Window.GetWindow(this);
            if (ownerWindow != null)
                picker.Owner = ownerWindow;

            var dialogResult = picker.ShowDialog();
            if (dialogResult != true)
                return;

            var selectedSlots = picker.SelectedSlots;
            if (selectedSlots == null || selectedSlots.Count == 0)
                return;

            if (vm.Store.FacultySlotBlocks == null)
                vm.Store.FacultySlotBlocks = new List<FacultySlotBlock>();

            foreach (var slot in selectedSlots)
            {
                bool exists = vm.Store.FacultySlotBlocks
                    .Any(b => b.FacultyId == vm.SelectedFaculty.Id && b.SlotId == slot.Id);

                if (!exists)
                {
                    vm.Store.FacultySlotBlocks.Add(new FacultySlotBlock
                    {
                        FacultyId = vm.SelectedFaculty.Id,
                        SlotId = slot.Id,
                        Reason = vm.BlockReason
                    });
                }

                if (vm.Store.Assignments != null)
                {
                    var toRemove = vm.Store.Assignments
                        .Where(a => a.FacultyId == vm.SelectedFaculty.Id &&
                                    a.SlotId == slot.Id &&
                                    (vm.ScopeDepartmentId is null || a.DepartmentId == vm.ScopeDepartmentId.Value))
                        .ToList();

                    foreach (var a in toRemove)
                        vm.Store.Assignments.Remove(a);
                }
            }

            vm.RefreshRows();
            vm.BuildGeneralRows();
            vm.RequestAutoSave();
        }

        private void RemoveBlockWithPicker_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not FacultyScheduleViewModel vm)
                return;

            if (vm.SelectedFaculty is null)
            {
                MessageBox.Show("Please select a faculty member first.", "Notice",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (vm.Store is null || vm.Store.Slots == null || vm.Store.Slots.Count == 0)
            {
                MessageBox.Show("No time slots are available in the system.", "Notice",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var existingBlocks = vm.Store.FacultySlotBlocks?
                .Where(b => b.FacultyId == vm.SelectedFaculty.Id)
                .ToList();

            if (existingBlocks == null || existingBlocks.Count == 0)
            {
                MessageBox.Show("No release records are registered for this faculty member.", "Notice",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            int maxSlots = vm.Store.Slots.Count;

            var picker = new ManualSlotsPickerWindow(vm.Store, maxSlots)
            {
                Title = "Select Time Slots to Remove Faculty Release"
            };

            var ownerWindow = Window.GetWindow(this);
            if (ownerWindow != null)
                picker.Owner = ownerWindow;

            var dialogResult = picker.ShowDialog();
            if (dialogResult != true)
                return;

            var selectedSlots = picker.SelectedSlots;
            if (selectedSlots == null || selectedSlots.Count == 0)
                return;

            if (vm.Store.FacultySlotBlocks == null)
                return;

            foreach (var slot in selectedSlots)
            {
                vm.Store.FacultySlotBlocks.RemoveAll(b =>
                    b.FacultyId == vm.SelectedFaculty.Id &&
                    b.SlotId == slot.Id);
            }

            vm.RefreshRows();
            vm.BuildGeneralRows();
            vm.RequestAutoSave();
        }

        private void ClearAllBlocks_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not FacultyScheduleViewModel vm)
                return;

            if (vm.SelectedFaculty is null)
            {
                MessageBox.Show("Please select a faculty member first.", "Notice",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (vm.Store?.FacultySlotBlocks == null)
                return;

            var hasBlocks = vm.Store.FacultySlotBlocks
                .Any(b => b.FacultyId == vm.SelectedFaculty.Id);

            if (!hasBlocks)
            {
                MessageBox.Show("No faculty release records are registered for this faculty member.", "Notice",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var confirm = MessageBox.Show(
                "Do you want to remove all faculty release records for this faculty member?",
                "Confirm Remove All",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes)
                return;

            vm.Store.FacultySlotBlocks.RemoveAll(b => b.FacultyId == vm.SelectedFaculty.Id);

            vm.RefreshRows();
            vm.BuildGeneralRows();
            vm.RequestAutoSave();
        }

    }
}
