using Microsoft.Win32;
using MiniTrainerScheduler.Services;
using System;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using TrainerScheduler.Security;

namespace MiniTrainerScheduler.ViewModels
{
    public sealed partial class MainViewModel
    {
        // Footer-friendly status line
        public string StudentsStatusText =>
            $"Students: {Store?.Students.Count ?? 0} | Plans: {Store?.StudentPlans.Count ?? 0} | Enrollments: {Store?.StudentEnrollments.Count ?? 0}";

        public ICommand ImportStudentsJsonCommand { get; }
public ICommand ImportStudentPlansJsonCommand { get; }
public ICommand ExportStudentsJsonCommand { get; }
public ICommand ExportStudentEnrollmentsJsonCommand { get; }
        public ICommand DistributeStudentsCommand { get; }
private void NotifyStudentsChanged()
        {
            OnPropertyChanged(nameof(StudentsStatusText));
        }

        private async Task ImportStudentsJsonAsync()
        {
            if (!AuthContext.IsAdmin)
            {
                MessageBox.Show("You do not have permission to import students.", "Permissions", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var ofd = new OpenFileDialog
            {
                Filter = "JSON (*.json)|*.json",
                Title = "Select the students JSON file — it may also contain plans."
            };
            if (ofd.ShowDialog() != true) return;

            try
            {
                // Import (students + optional plans in the same file)
                var report = JsonImportService.ImportStudentsAndPlans(Store, ofd.FileName,
                    clearExistingStudents: true,
                    clearExistingPlans: true);

                // Persist (students always)
                var (okS, errS) = await _sqlite.SaveStudentsAsync(Store, scopeDepartmentId: null);
                if (!okS)
                    throw new InvalidOperationException($"SQLite: unable to save students.\n{errS}");

                // Persist plans only if imported any
                if (report.PlansImported > 0)
                {
                    var (okP, errP) = await _sqlite.SaveStudentPlansAsync(Store, scopeDepartmentId: null);
                    if (!okP)
                        throw new InvalidOperationException($"SQLite: unable to save student plans.\n{errP}");
                }

                NotifyStudentsChanged();

                // refresh the Student Schedule view (no extra message boxes)
                await RefreshStudentScheduleAsync(showMessageIfEmpty: false);

                var warn = report.Warnings.Count == 0 ? "" : "\n\nWarnings:\n- " + string.Join("\n- ", report.Warnings.Take(8)) + (report.Warnings.Count > 8 ? "\n..." : "");
                MessageBox.Show(
                    $"Students imported successfully.\n" +
                    $"Students: {report.StudentsImported} (Skipped: {report.StudentsSkipped})\n" +
                    $"Plans: {report.PlansImported} (Skipped: {report.PlansSkipped})" +
                    warn,
                    "Students (JSON)",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), "Student JSON Import Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task ImportStudentPlansJsonAsync()
        {
            if (!AuthContext.IsAdmin)
            {
                MessageBox.Show("You do not have permission to import student plans.", "Permissions", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var ofd = new OpenFileDialog
            {
                Filter = "JSON (*.json)|*.json",
                Title = "Select the student plans file (StudentPlans/Requests) — JSON"
            };
            if (ofd.ShowDialog() != true) return;

            try
            {
                var report = JsonImportService.ImportPlansOnly(Store, ofd.FileName, clearExisting: true);

                var (okP, errP) = await _sqlite.SaveStudentPlansAsync(Store, scopeDepartmentId: null);
                if (!okP)
                    throw new InvalidOperationException($"SQLite: unable to save student plans.\n{errP}");

                NotifyStudentsChanged();

                // refresh the Student Schedule view (no extra message boxes)
                await RefreshStudentScheduleAsync(showMessageIfEmpty: false);

                var warn = report.Warnings.Count == 0 ? "" : "\n\nWarnings:\n- " + string.Join("\n- ", report.Warnings.Take(8)) + (report.Warnings.Count > 8 ? "\n..." : "");
                MessageBox.Show(
                    $"Student plans imported successfully.\n" +
                    $"Plans: {report.PlansImported} (Skipped: {report.PlansSkipped})" +
                    warn,
                    "Student Plans (JSON)",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), "Student Plans JSON Import Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExportStudentsJson()
        {
            if (!AuthContext.IsAdmin) return;

            var sfd = new SaveFileDialog
            {
                Filter = "JSON (*.json)|*.json",
                Title = "Export Students + Plans (JSON)",
                FileName = "students_bundle.json"
            };
            if (sfd.ShowDialog() != true) return;

            try
            {
                var payload = new
                {
                    exportedUtc = DateTime.UtcNow,
                    students = Store.Students.OrderBy(s => s.Id).ToList(),
                    studentPlans = Store.StudentPlans
                        .OrderBy(p => p.StudentId).ThenBy(p => p.CourseId).ToList()
                };

                var opts = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };

                File.WriteAllText(sfd.FileName, JsonSerializer.Serialize(payload, opts));
                MessageBox.Show("Export completed.", "Students (JSON)", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), "JSON Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExportStudentEnrollmentsJson()
        {
            if (!AuthContext.IsAdmin) return;

            var sfd = new SaveFileDialog
            {
                Filter = "JSON (*.json)|*.json",
                Title = "Export Student Enrollments — JSON",
                FileName = "student_enrollments.json"
            };
            if (sfd.ShowDialog() != true) return;

            try
            {
                var payload = new
                {
                    exportedUtc = DateTime.UtcNow,
                    studentEnrollments = Store.StudentEnrollments
                        .OrderBy(e => e.StudentId).ThenBy(e => e.AssignmentId).ToList()
                };

                var opts = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };

                File.WriteAllText(sfd.FileName, JsonSerializer.Serialize(payload, opts));
                MessageBox.Show("Export completed.", "Enrollments (JSON)", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), "JSON Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task DistributeStudentsAsync()
        {
            if (!AuthContext.IsAdmin)
            {
                MessageBox.Show("You do not have permission to assign students.", "Permissions", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (Store is null)
                return;

            if (Store.Students.Count == 0 || Store.StudentPlans.Count == 0)
            {
                MessageBox.Show("There are no student records or plans. Please import students and plans first.", "Students", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (Store.Assignments.Count == 0)
            {
                MessageBox.Show("There are no generated assignment results. Please generate the timetable first.", "Students", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Ensure Assignment.Id is populated (SQLite identity) so enrollments can reference real AssignmentId.
            if (Store.Assignments.Any(a => a.Id == 0))
            {
                await SaveScheduledResultsAsync();
                await TryLoadSavedResultsAsync(showMessageIfEmpty: false);
            }

            try
            {
                var svc = new EnrollmentService();
                var report = svc.Distribute(Store, new EnrollmentOptions
                {
                    TheoryCapacity = 35,
                    LabCapacity = 15,
                    EnforceDepartment = true,
                    EnforceLevel = true,
                    ResetExistingEnrollments = true
                });

                var (ok, err) = await _sqlite.SaveStudentEnrollmentsAsync(Store, scopeDepartmentId: null);
                if (!ok)
                {
                    MessageBox.Show("Student allocation completed, but saving the enrollments to SQLite failed.\n" + err, "SQLite", MessageBoxButton.OK, MessageBoxImage.Warning);
                }

                var (okU, errU) = await _sqlite.SaveStudentUnassignedPlansAsync(report.Unassigned, scopeDepartmentId: null);
                if (!okU)
                {
                    MessageBox.Show("Student allocation completed, but saving the unassigned report to SQLite failed.\n" + errU, "SQLite", MessageBoxButton.OK, MessageBoxImage.Warning);
                }

                // Keep the last report in memory so the Student tab can show it immediately
                SetLastEnrollmentReport(report);


                NotifyStudentsChanged();

                // refresh the Student Schedule view (no extra message boxes)
                await RefreshStudentScheduleAsync(showMessageIfEmpty: false);

                // Summary message
                var byReason = report.UnassignedByReason;
                var reasonsText = byReason.Count == 0
                    ? ""
                    : "\n\nUnassigned by reason:\n" + string.Join("\n", byReason.Select(kv => $"- {kv.Key}: {kv.Value}"));

                MessageBox.Show(
                    $"Student allocation completed.\nStudents: {report.StudentsProcessed}\nRequests: {report.PlansProcessed}\nEnrollments: {report.EnrollmentsCreated}\nUnassigned: {report.UnassignedCount}{reasonsText}",
                    "Student Assignment",
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
