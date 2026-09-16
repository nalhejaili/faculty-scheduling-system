using Microsoft.Win32;
using MiniTrainerScheduler.Services;
using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using TrainerScheduler.Security;

namespace MiniTrainerScheduler.ViewModels
{
    public sealed partial class MainViewModel
    {
        public ICommand ValidateExpectedRegistrationCsvCommand { get; }

        private async Task ValidateExpectedRegistrationCsvAsync()
        {
            if (!AuthContext.IsAdmin)
            {
                MessageBox.Show("You do not have permission to perform this action.", "Permissions", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var ofd = new OpenFileDialog
            {
                Filter = "CSV (*.csv)|*.csv|All files (*.*)|*.*",
                Title = "Select the expected registration report (CSV)"
            };
            if (ofd.ShowDialog() != true) return;

            try
            {
                var rep = ExpectedRegistrationCsv.Validate(ofd.FileName);

                if (!rep.IsValid || rep.Schema is null)
                {
                    var msg = "The report schema could not be validated.\n\n" + string.Join("\n", rep.Errors.Select(e => "- " + e));
                    if (rep.Warnings.Count > 0)
                        msg += "\n\nWarnings:\n" + string.Join("\n", rep.Warnings.Select(w => "- " + w));

                    MessageBox.Show(msg, "CSV Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var s = rep.Schema;
                var sb = new StringBuilder();
                sb.AppendLine("The expected registration report schema has been validated successfully.");
                sb.AppendLine($"Delimiter: '{s.Delimiter}'");
                sb.AppendLine($"Data rows: {rep.DataRowsCount}");
                sb.AppendLine();
                sb.AppendLine("Mapping (key columns):");
                sb.AppendLine($"- Student number: {s.StudentNoIndex + 1}");
                sb.AppendLine($"- Student name: {s.StudentNameIndex + 1}");
                sb.AppendLine($"- Department: {s.DepartmentIndex + 1}");
                sb.AppendLine($"- Specialization: {s.SpecialtyIndex + 1}");
                sb.AppendLine($"- Academic term: {s.TermIndex + 1}");
                sb.AppendLine($"- Course code: {s.CourseCodeIndex + 1}");
                sb.AppendLine($"- Course title: {s.CourseNameIndex + 1}");
                sb.AppendLine($"- Schedule type: {s.ScheduleKindIndex + 1}");

                if (rep.Warnings.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("Warnings:");
                    foreach (var w in rep.Warnings.Take(8))
                        sb.AppendLine("- " + w);
                    if (rep.Warnings.Count > 8) sb.AppendLine("...");
                }

                MessageBox.Show(sb.ToString(), "CSV Validation", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            await Task.CompletedTask;
        }
    }
}
