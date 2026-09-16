using System;
using System.IO;
using System.Linq;
using System.Text;
using TrainerScheduler.Security;

namespace MiniTrainerScheduler.ViewModels
{
    public sealed partial class MainViewModel
    {
        /// <summary>
        /// </summary>
        public string ExportDiagnosticsReport()
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("TrainerScheduler - Diagnostics");
                sb.AppendLine($"UTC:   {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine($"Local: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine();

                sb.AppendLine($"Auth: IsAdmin={IsAdmin} IsSupervisor={IsSupervisor}");
                var lockedDeptId = AuthContext.Current?.DepartmentId;
                sb.AppendLine($"Auth: LockedDepartmentId={(lockedDeptId.HasValue ? lockedDeptId.Value.ToString() : "(none)")}");
                sb.AppendLine();

                sb.AppendLine($"SelectedDepartment: {(SelectedDepartment == null ? "(none)" : $"{SelectedDepartment.Name}({SelectedDepartment.Id})")}");
                sb.AppendLine($"SelectedLevel: {SelectedLevel}");
                sb.AppendLine();

                sb.AppendLine($"Store counts: Departments={Store.Departments.Count}, Courses={Store.Courses.Count}, Faculties={Store.Faculties.Count}, Slots={Store.Slots.Count}, Rooms={Store.Rooms.Count}");
                sb.AppendLine($"UI counts: PlanRows={PlanRows.Count}, QueuedPlan={QueuedPlan.Count}, ResultsRows={Rows.Count}");
                sb.AppendLine();

                sb.AppendLine("QueuedPlan (first 150 rows):");
                foreach (var r in QueuedPlan.Take(150))
                {
                    sb.AppendLine($"- Dept={r.DepartmentName}({r.DepartmentId}) L{r.Level} | {r.CourseName}({r.CourseId}) | Sec {r.SectionIndex}/{r.SectionsRequested} | Fac={r.PreferredFacultyId} | Slot={r.PreferredSlotId}");
                }
                sb.AppendLine();

                sb.AppendLine("Generated Results (first 250 rows):");
                foreach (var r in Rows.Take(250))
                {
                    sb.AppendLine($"- {r.Department} | {r.Course} | Sec {r.Section} | {r.Slot} | {r.Faculty} | {r.Room}");
                }

                var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "TrainerScheduler");
                Directory.CreateDirectory(folder);
                var path = Path.Combine(folder, $"diagnostics_{DateTime.Now:yyyyMMdd_HHmmss}.txt");

                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
                return path;
            }
            catch (Exception ex)
            {
                return ex.ToString();
            }
        }
    }
}
