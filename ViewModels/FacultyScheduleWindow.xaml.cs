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
using System.Windows.Media;

namespace MiniTrainerScheduler.Views
{
    public partial class FacultyScheduleWindow : Window
    {
        public FacultyScheduleWindow(DataStore store)
            : this(new FacultyScheduleViewModel(store))
        {
        }

        public FacultyScheduleWindow(FacultyScheduleViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        }

        private FacultyScheduleViewModel? Vm => DataContext as FacultyScheduleViewModel;

        private void RefreshBtn_Click(object sender, RoutedEventArgs e)
        {
            Vm?.RefreshRows();
        }

        private void ExportCsvBtn_Click(object sender, RoutedEventArgs e)
        {
            var vm = Vm;
            if (vm is null || !vm.Rows.Any()) return;

            var sfd = new SaveFileDialog
            {
                Filter = "CSV (*.csv)|*.csv",
                FileName = $"faculty_timetable_{vm.SelectedFaculty?.Name ?? "FacultyMember"}.csv"
            };
            if (sfd.ShowDialog() != true) return;

            using var sw = new StreamWriter(sfd.FileName, false, new UTF8Encoding(true));
            sw.WriteLine("Department,Course,Section,Time Slot,Hours,Room,Status");
            foreach (var r in vm.Rows)
                sw.WriteLine($"{r.Department},{r.Course},{r.Section},{r.Slot},{r.Hours},{r.Room},{r.Status}");

            MessageBox.Show("The file has been exported.", "CSV", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void PrintBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Vm?.BuildGeneralRows();
                if (TemplateRoot == null)
                    return;

                PrintWithScaling(TemplateRoot, "Faculty Timetable");
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Print Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void PrintSilent()
        {
            try
            {
                Vm?.BuildGeneralRows();
                if (TemplateRoot == null)
                    return;

                PrintWithScaling(TemplateRoot, "Faculty Timetable");
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), "Faculty Timetable Print Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// </summary>
        private static void PrintWithScaling(FrameworkElement visual, string jobName)
        {
            var dlg = new PrintDialog();
            if (dlg.ShowDialog() != true)
                return;

            var originalTransform = visual.LayoutTransform;

            visual.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            visual.Arrange(new Rect(0, 0, visual.DesiredSize.Width, visual.DesiredSize.Height));
            visual.UpdateLayout();

            double ow = visual.ActualWidth;
            double oh = visual.ActualHeight;

            double pw = dlg.PrintableAreaWidth;
            double ph = dlg.PrintableAreaHeight;

            double scale = Math.Min(pw / ow, ph / oh);
            if (scale > 1.0) scale = 1.0;

            visual.LayoutTransform = new ScaleTransform(scale, scale);

            var scaledSize = new Size(ow * scale, oh * scale);
            visual.Measure(scaledSize);
            visual.Arrange(new Rect(new Point(0, 0), scaledSize));
            visual.UpdateLayout();

            dlg.PrintVisual(visual, jobName);

            visual.LayoutTransform = originalTransform;
            visual.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            visual.Arrange(new Rect(0, 0, visual.DesiredSize.Width, visual.DesiredSize.Height));
            visual.UpdateLayout();
        }


        private void OpenSlotsPicker_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (Vm is not FacultyScheduleViewModel vm)
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
                int maxSlots = required <= 0 ? 1 : (int)Math.Ceiling(required);

                string kindAr = AcademicEnglishText.KindLabel(selectedKind);
                var preselected = vm.Store.Assignments
                    .Where(a => a.FacultyId == vm.SelectedFaculty.Id
                                && a.CourseId == vm.SelectedCourse.Id
                                && a.SectionIndex == vm.SelectedSectionIndex
                                && string.Equals(a.Kind ?? string.Empty, selectedKind, StringComparison.OrdinalIgnoreCase))
                    .Select(a => a.SlotId)
                    .Distinct()
                    .ToHashSet();

                var picker = new ManualSlotsPickerWindow(
                    vm.Store,
                    maxSlots,
                    preselectedSlotIds: preselected,
                    headerText: $"Select weekly class meeting times ({kindAr})",
                    requireExact: true)
                {
                    Owner = this
                };

                if (picker.ShowDialog() == true)
                {
                    var slots = picker.SelectedSlots;

                    if (slots is null || slots.Count == 0)
                        return;

                    var selectedSlotIds = slots.Select(s => s.Id).Distinct().ToHashSet();

                    var occupiedByOther = vm.Store.Assignments
                        .Where(a => a.FacultyId == vm.SelectedFaculty.Id
                                    && !(a.CourseId == vm.SelectedCourse.Id
                                         && a.SectionIndex == vm.SelectedSectionIndex
                                         && string.Equals(a.Kind ?? string.Empty, selectedKind, StringComparison.OrdinalIgnoreCase)))
                        .Select(a => a.SlotId)
                        .ToHashSet();

                    var blockedByFaculty = (vm.Store.FacultySlotBlocks ?? new System.Collections.Generic.List<FacultySlotBlock>())
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
                                    && string.Equals(a.Kind ?? string.Empty, selectedKind, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    foreach (var a in toRemove)
                        vm.Store.Assignments.Remove(a);

                    if (vm.SelectedSectionIndex <= 0)
                        vm.SelectedSectionIndex = 1;

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
                            selectedKind
                        );

                        ManualPlanBridge.RegisterManualAssignment(vm.Store, assignment);
                    }

                    // vm.RefreshRows();
                    // vm.BuildGeneralRows();
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
                vm.SelectedKind = kind;
        }

        private void AddBlockWithPicker_Click(object sender, RoutedEventArgs e)
        {
            if (Vm is not FacultyScheduleViewModel vm)
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
                Owner = this,
                Title = "Select Time Slots for Faculty Release"
            };

            var dialogResult = picker.ShowDialog();
            if (dialogResult != true)
                return;

            var selectedSlots = picker.SelectedSlots;
            if (selectedSlots == null || selectedSlots.Count == 0)
                return;

            if (vm.Store.FacultySlotBlocks == null)
                vm.Store.FacultySlotBlocks = new List<FacultySlotBlock>();

            int facultyId = vm.SelectedFaculty.Id;

            foreach (var slot in selectedSlots)
            {
                bool exists = vm.Store.FacultySlotBlocks
                    .Any(b => b.FacultyId == facultyId && b.SlotId == slot.Id);

                if (!exists)
                {
                    vm.Store.FacultySlotBlocks.Add(new FacultySlotBlock
                    {
                        FacultyId = facultyId,
                        SlotId = slot.Id,
                        Reason = vm.BlockReason
                    });
                }
            }

            if (vm.Store.Assignments != null && vm.Store.Assignments.Count > 0)
            {
                var blockedSlotIds = selectedSlots
                    .Select(s => s.Id)
                    .ToHashSet();

                var toRemove = vm.Store.Assignments
                    .Where(a => a.FacultyId == facultyId &&
                                blockedSlotIds.Contains(a.SlotId))
                    .ToList();

                foreach (var a in toRemove)
                    vm.Store.Assignments.Remove(a);
            }

            vm.RefreshRows();
            vm.BuildGeneralRows();
        }
    }
}
