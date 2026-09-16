using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using MiniTrainerScheduler.Models;
using TrainerScheduler.Security;
using MiniTrainerScheduler.Services;
using TrainerScheduler.Services;

namespace MiniTrainerScheduler.ViewModels;

public sealed partial class MainViewModel
{
    // ======== Rows shown in UI ========

    public sealed class StudentScheduleRow
    {
        public int StudentId { get; init; }
        public string StudentNo { get; init; } = "";
        public string StudentName { get; init; } = "";
        public int DepartmentId { get; init; }
        public string DepartmentName { get; init; } = "";
        public int Level { get; init; }

        public int CourseId { get; init; }
        public string CourseCode { get; init; } = "";
        public string CourseName { get; init; } = "";

        public int SectionIndex { get; init; }
        public string Kind { get; init; } = ""; // THEORY / LAB

        public string FacultyName { get; init; } = "";
        public string SlotLabel { get; init; } = "";
        public string RoomName { get; init; } = "";

        public string CourseDisplay => string.IsNullOrWhiteSpace(CourseCode) ? CourseName : $"{CourseCode} - {CourseName}";

        public string KindAr => AcademicEnglishText.KindLabel(Kind);
    }

    public sealed class StudentUnassignedRow
    {
        public int StudentId { get; init; }
        public string StudentNo { get; init; } = "";
        public string StudentName { get; init; } = "";
        public int DepartmentId { get; init; }
        public string DepartmentName { get; init; } = "";
        public int Level { get; init; }

        public int CourseId { get; init; }
        public string CourseCode { get; init; } = "";
        public string CourseName { get; init; } = "";

        public string Kind { get; init; } = ""; // THEORY / LAB

        public string Reason { get; init; } = "";
        public string Details { get; init; } = "";

        public string CourseDisplay => string.IsNullOrWhiteSpace(CourseCode) ? CourseName : $"{CourseCode} - {CourseName}";

        public string KindAr => AcademicEnglishText.KindLabel(Kind);
    }

    // ======== Collections bound to XAML ========

    public ObservableCollection<StudentScheduleRow> StudentScheduleRows { get; } = new();
    public ObservableCollection<StudentUnassignedRow> StudentUnassignedRows { get; } = new();

    public ObservableCollection<Department> StudentFilterDepartments { get; } = new();
    public ObservableCollection<int> StudentFilterLevels { get; } = new();

    private int _studentFilterDepartmentId;
    public int StudentFilterDepartmentId
    {
        get => _studentFilterDepartmentId;
        set
        {
            if (_studentFilterDepartmentId == value) return;
            _studentFilterDepartmentId = value;
            OnPropertyChanged(nameof(StudentFilterDepartmentId));
            RefreshStudentFilterLevels();
            ApplyStudentFilters();
        }
    }

    private int _studentFilterLevel;
    public int StudentFilterLevel
    {
        get => _studentFilterLevel;
        set
        {
            if (_studentFilterLevel == value) return;
            _studentFilterLevel = value;
            OnPropertyChanged(nameof(StudentFilterLevel));
            ApplyStudentFilters();
        }
    }

    private string _studentFilterText = "";
    public string StudentFilterText
    {
        get => _studentFilterText;
        set
        {
            if (_studentFilterText == value) return;
            _studentFilterText = value ?? "";
            OnPropertyChanged(nameof(StudentFilterText));
            ApplyStudentFilters();
        }
    }

    public bool IsStudentFilterDepartmentEditable => !AuthContext.IsSupervisor;

    // ======== Commands ========

    public ICommand RefreshStudentScheduleCommand { get; private set; } = null!;
    public ICommand ExportStudentScheduleCsvCommand { get; private set; } = null!;
    public ICommand ExportStudentUnassignedCsvCommand { get; private set; } = null!;

    // ======== Internal caches ========

    private readonly List<StudentScheduleRow> _studentScheduleAll = new();
    private readonly List<StudentUnassignedRow> _studentUnassignedAll = new();

    private EnrollmentReport? _lastEnrollmentReport;

    internal void SetLastEnrollmentReport(EnrollmentReport report)
    {
        _lastEnrollmentReport = report;
    }

    public async Task RefreshStudentScheduleAsync(bool showMessageIfEmpty = true)
    {
        if (Store is null) return;

        // Supervisor: lock department filter to their dept
        if (AuthContext.IsSupervisor)
        {
            var deptId = AuthContext.Current?.DepartmentId ?? 0;
            if (deptId > 0)
                StudentFilterDepartmentId = deptId;
        }

        IsBusy = true;
        RaiseAllCanExec();

        try
        {
			// Ensure we have the latest core data from SQLite
			try
			{
				int? scopeDeptId = AuthContext.IsSupervisor ? AuthContext.Current?.DepartmentId : null;
				await _sqlite.TryLoadStudentsAsync(Store, scopeDeptId);
			}
			catch (Exception ex)
            {
                DiagnosticsLog.TryWrite(ex);
            }

            try
            {
                int? scopeDeptId = AuthContext.IsSupervisor ? AuthContext.Current?.DepartmentId : null;
                await _sqlite.TryLoadStudentPlansAsync(Store, scopeDeptId);
                await _sqlite.TryLoadStudentEnrollmentsAsync(Store, scopeDeptId);
            }
            catch (Exception ex)
            {
                DiagnosticsLog.TryWrite(ex);
            }

            // Load latest schedule results (Assignments)
            try
            {
                await TryLoadSavedResultsAsync(showMessageIfEmpty: false);
            }
            catch (Exception ex)
            {
                DiagnosticsLog.TryWrite(ex);
            }

            // Course codes (optional)
            Dictionary<int, string> courseCodeById = new();
            try
            {
                var maps = await _sqlite.GetCourseCodeMapsAsync();
				courseCodeById = maps
					.Where(m => m.CourseId is not null)
					.GroupBy(m => m.CourseId!.Value)
					.ToDictionary(g => g.Key, g => g.Select(x => x.CourseCode).FirstOrDefault() ?? "");
            }
            catch (Exception ex)
            {
                DiagnosticsLog.TryWrite(ex);
            }

            BuildStudentFilterDepartments();

            // Build dictionaries for fast lookup
            var studentsById = Store.Students.ToDictionary(s => s.Id);
            var coursesById = Store.Courses.ToDictionary(c => c.Id);
            var facultyById = Store.Faculties.ToDictionary(f => f.Id);
            var roomsById = Store.Rooms.ToDictionary(r => r.Id);
            var slotsById = Store.Slots.ToDictionary(s => s.Id);
            var deptsById = Store.Departments.ToDictionary(d => d.Id);

            var assignmentsById = Store.Assignments.ToDictionary(a => a.Id);

            _studentScheduleAll.Clear();

            foreach (var enr in Store.StudentEnrollments)
            {
                if (!studentsById.TryGetValue(enr.StudentId, out var st))
                    continue;

                // Supervisor scope guard (in case old enrollments exist in store)
                if (AuthContext.IsSupervisor && AuthContext.Current?.DepartmentId is int sDept && sDept > 0)
                {
                    if (st.DepartmentId != sDept)
                        continue;
                }

                if (!assignmentsById.TryGetValue((int)enr.AssignmentId, out var asg))
                    continue;

                if (!coursesById.TryGetValue(asg.CourseId, out var course))
                    continue;

				var deptId = st.DepartmentId ?? 0;
				var deptName = deptId > 0 && deptsById.TryGetValue(deptId, out var dept)
					? dept.Name
					: (deptId > 0 ? deptId.ToString() : "-");

                var facName = facultyById.TryGetValue(asg.FacultyId, out var fac) ? fac.Name : asg.FacultyId.ToString();

                var slotLabel = "";
                if (slotsById.TryGetValue(asg.SlotId, out var slot))
                    slotLabel = AcademicEnglishText.Slot(slot);

                var roomName = "";
                if (asg.RoomId is int roomId && roomId > 0 && roomsById.TryGetValue(roomId, out var room))
                    roomName = room.Name;

                _studentScheduleAll.Add(new StudentScheduleRow
                {
                    StudentId = st.Id,
                    StudentNo = st.StudentNo ?? "",
					StudentName = st.FullName ?? "",
					DepartmentId = deptId,
                    DepartmentName = deptName,
					Level = st.Level ?? 0,

                    CourseId = course.Id,
                    CourseCode = courseCodeById.TryGetValue(course.Id, out var code) ? code : "",
                    CourseName = course.Name ?? "",

                    SectionIndex = asg.SectionIndex,
                    Kind = NormalizeKind(asg.Kind),

                    FacultyName = facName,
                    SlotLabel = slotLabel,
                    RoomName = roomName
                });
            }

            _studentUnassignedAll.Clear();

            List<UnassignedPlan> unassignedPlans;
            if (_lastEnrollmentReport?.Unassigned is not null)
            {
                unassignedPlans = _lastEnrollmentReport.Unassigned;
            }
            else
            {
                try
                {
                    int? scopeDeptId = AuthContext.IsSupervisor ? AuthContext.Current?.DepartmentId : null;
                    unassignedPlans = await _sqlite.TryLoadStudentUnassignedPlansAsync(scopeDeptId);
                }
                catch
                {
                    unassignedPlans = new List<UnassignedPlan>();
                }
            }

            foreach (var u in unassignedPlans)
            {
                if (!studentsById.TryGetValue(u.StudentId, out var st))
                    continue;

                if (AuthContext.IsSupervisor && AuthContext.Current?.DepartmentId is int sDept && sDept > 0)
                {
                    if (st.DepartmentId != sDept)
                        continue;
                }

                if (!coursesById.TryGetValue(u.CourseId, out var course))
                    continue;

				var deptId = st.DepartmentId ?? 0;
				var deptName = deptId > 0 && deptsById.TryGetValue(deptId, out var dept)
					? dept.Name
					: (deptId > 0 ? deptId.ToString() : "-");

                _studentUnassignedAll.Add(new StudentUnassignedRow
                {
                    StudentId = st.Id,
                    StudentNo = st.StudentNo ?? "",
					StudentName = st.FullName ?? "",
					DepartmentId = deptId,
                    DepartmentName = deptName,
					Level = st.Level ?? 0,

                    CourseId = course.Id,
                    CourseCode = courseCodeById.TryGetValue(course.Id, out var code) ? code : "",
                    CourseName = course.Name ?? "",

                    Kind = NormalizeKind(u.Kind),
                    Reason = u.Reason ?? "",
                    Details = u.Details ?? ""
                });
            }
            // Sort
            _studentScheduleAll.Sort((a, b) =>
            {
                var c = string.Compare(a.StudentName, b.StudentName, StringComparison.CurrentCultureIgnoreCase);
                if (c != 0) return c;
                c = string.Compare(a.StudentNo, b.StudentNo, StringComparison.CurrentCultureIgnoreCase);
                if (c != 0) return c;
                c = string.Compare(a.CourseName, b.CourseName, StringComparison.CurrentCultureIgnoreCase);
                if (c != 0) return c;
                c = a.SectionIndex.CompareTo(b.SectionIndex);
                if (c != 0) return c;
                return string.Compare(a.Kind, b.Kind, StringComparison.OrdinalIgnoreCase);
            });

            _studentUnassignedAll.Sort((a, b) =>
            {
                var c = string.Compare(a.StudentName, b.StudentName, StringComparison.CurrentCultureIgnoreCase);
                if (c != 0) return c;
                c = string.Compare(a.StudentNo, b.StudentNo, StringComparison.CurrentCultureIgnoreCase);
                if (c != 0) return c;
                c = string.Compare(a.CourseName, b.CourseName, StringComparison.CurrentCultureIgnoreCase);
                if (c != 0) return c;
                return string.Compare(a.Kind, b.Kind, StringComparison.OrdinalIgnoreCase);
            });

            RefreshStudentFilterLevels();
            ApplyStudentFilters();

            if (showMessageIfEmpty && StudentScheduleRows.Count == 0)
            {
                MessageBox.Show(
                    "No student timetable data is currently available.\n\n" +
                    "Please make sure that:\n" +
                    "1) student records and study-plan data (StudentPlans) are available.\n" +
                    "2) the student distribution step has been completed and the results have been saved.",
                    "Student Timetable",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
        finally
        {
            IsBusy = false;
            RaiseAllCanExec();
        }
    }

    private void BuildStudentFilterDepartments()
    {
        StudentFilterDepartments.Clear();

		// "All"
		StudentFilterDepartments.Add(new Department(0, "All"));

        foreach (var d in Departments.OrderBy(d => d.Name))
            StudentFilterDepartments.Add(d);

        // Default selection
        if (AuthContext.IsSupervisor)
        {
            var deptId = AuthContext.Current?.DepartmentId ?? 0;
            if (deptId > 0)
                _studentFilterDepartmentId = deptId;
        }
        else
        {
            if (_studentFilterDepartmentId == 0 && Departments.Count == 1)
                _studentFilterDepartmentId = Departments[0].Id;
        }

        OnPropertyChanged(nameof(IsStudentFilterDepartmentEditable));
        OnPropertyChanged(nameof(StudentFilterDepartments));
        OnPropertyChanged(nameof(StudentFilterDepartmentId));
    }

    private void RefreshStudentFilterLevels()
    {
        StudentFilterLevels.Clear();
        StudentFilterLevels.Add(0); // all

        IEnumerable<int> levels;

        if (StudentFilterDepartmentId > 0)
        {
            levels = _studentScheduleAll
                .Where(r => r.DepartmentId == StudentFilterDepartmentId)
                .Select(r => r.Level)
                .Concat(_studentUnassignedAll.Where(r => r.DepartmentId == StudentFilterDepartmentId).Select(r => r.Level));
        }
        else
        {
            levels = _studentScheduleAll.Select(r => r.Level)
                .Concat(_studentUnassignedAll.Select(r => r.Level));
        }

        foreach (var lvl in levels.Where(l => l > 0).Distinct().OrderBy(x => x))
            StudentFilterLevels.Add(lvl);

        // keep selection valid
        if (StudentFilterLevel != 0 && !StudentFilterLevels.Contains(StudentFilterLevel))
            StudentFilterLevel = 0;

        OnPropertyChanged(nameof(StudentFilterLevels));
    }

    private void ApplyStudentFilters()
    {
        StudentScheduleRows.Clear();
        StudentUnassignedRows.Clear();

        var deptId = StudentFilterDepartmentId;
        var level = StudentFilterLevel;
        var q = (StudentFilterText ?? "").Trim();

        IEnumerable<StudentScheduleRow> schedule = _studentScheduleAll;
        IEnumerable<StudentUnassignedRow> unassigned = _studentUnassignedAll;

        if (deptId > 0)
        {
            schedule = schedule.Where(r => r.DepartmentId == deptId);
            unassigned = unassigned.Where(r => r.DepartmentId == deptId);
        }

        if (level > 0)
        {
            schedule = schedule.Where(r => r.Level == level);
            unassigned = unassigned.Where(r => r.Level == level);
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            schedule = schedule.Where(r => MatchesStudentQuery(r.StudentNo, r.StudentName, r.CourseDisplay, q));
            unassigned = unassigned.Where(r => MatchesStudentQuery(r.StudentNo, r.StudentName, r.CourseDisplay, q));
        }

        foreach (var r in schedule)
            StudentScheduleRows.Add(r);

        foreach (var r in unassigned)
            StudentUnassignedRows.Add(r);

        RaiseAllCanExec();
    }

    private static bool MatchesStudentQuery(string studentNo, string studentName, string course, string q)
    {
        return ContainsCI(studentNo, q) || ContainsCI(studentName, q) || ContainsCI(course, q);
    }

    private static bool ContainsCI(string? text, string q)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        return text.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0;
    }

    private static string NormalizeKind(string? kind)
    {
        kind = (kind ?? "").Trim().ToUpperInvariant();
        return kind switch
        {
            "PRACTICAL" => "LAB",
            _ => kind
        };
    }

    private void ExportStudentScheduleCsv()
    {
        if (StudentScheduleRows.Count == 0)
        {
            MessageBox.Show("There is no data available for export.", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dlg = new SaveFileDialog
        {
            Title = "Export Student Timetable (CSV)",
            FileName = $"student_schedule_{DateTime.Now:yyyyMMdd_HHmm}.csv",
            Filter = "CSV Files (*.csv)|*.csv|All Files (*.*)|*.*"
        };

        if (dlg.ShowDialog() != true) return;

        try
        {
            var rows = new List<string[]>();
            rows.Add(new[] { "StudentNo", "StudentName", "Department", "Level", "Course", "SectionIndex", "Kind", "Faculty", "Slot", "Room" });

            foreach (var r in StudentScheduleRows)
            {
                rows.Add(new[]
                {
                    r.StudentNo,
                    r.StudentName,
                    r.DepartmentName,
                    r.Level.ToString(),
                    r.CourseDisplay,
                    r.SectionIndex.ToString(),
                    r.KindAr,
                    r.FacultyName,
                    r.SlotLabel,
                    r.RoomName
                });
            }

            WriteCsv(dlg.FileName, rows);
            MessageBox.Show("The export completed successfully.", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.ToString(), "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExportStudentUnassignedCsv()
    {
        if (StudentUnassignedRows.Count == 0)
        {
            MessageBox.Show("There is no data available for export.", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dlg = new SaveFileDialog
        {
            Title = "Export Unassigned Students (CSV)",
            FileName = $"student_unassigned_{DateTime.Now:yyyyMMdd_HHmm}.csv",
            Filter = "CSV Files (*.csv)|*.csv|All Files (*.*)|*.*"
        };

        if (dlg.ShowDialog() != true) return;

        try
        {
            var rows = new List<string[]>();
            rows.Add(new[] { "StudentNo", "StudentName", "Department", "Level", "Course", "Kind", "Reason", "Details" });

            foreach (var r in StudentUnassignedRows)
            {
                rows.Add(new[]
                {
                    r.StudentNo,
                    r.StudentName,
                    r.DepartmentName,
                    r.Level.ToString(),
                    r.CourseDisplay,
                    r.KindAr,
                    r.Reason,
                    r.Details
                });
            }

            WriteCsv(dlg.FileName, rows);
            MessageBox.Show("The export completed successfully.", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.ToString(), "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static void WriteCsv(string path, List<string[]> rows)
    {
        // UTF-8 BOM for Excel Arabic
        using var sw = new StreamWriter(path, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        foreach (var row in rows)
        {
            sw.WriteLine(string.Join(",", row.Select(EscapeCsv)));
        }
    }

    private static string EscapeCsv(string? s)
    {
        s ??= "";
        if (s.Contains('"') || s.Contains(',') || s.Contains('\n') || s.Contains('\r'))
        {
            s = s.Replace("\"", "\"\"");
            return $"\"{s}\"";
        }
        return s;
    }

    internal void InitStudentScheduleCommands()
    {
        RefreshStudentScheduleCommand = new RelayCommand(async _ => await RefreshStudentScheduleAsync(showMessageIfEmpty: true), _ => !IsBusy);

        ExportStudentScheduleCsvCommand = new RelayCommand(_ => ExportStudentScheduleCsv(), _ => StudentScheduleRows.Count > 0);
        ExportStudentUnassignedCsvCommand = new RelayCommand(_ => ExportStudentUnassignedCsv(), _ => StudentUnassignedRows.Count > 0);
    }
}
