using MiniTrainerScheduler.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using TrainerScheduler.Security;

namespace MiniTrainerScheduler.ViewModels
{
    /// <summary>
    /// Weekly Student view (Pickers)
    ///
    /// </summary>
    public sealed partial class MainViewModel
    {
        // ===== Busy (students list only)
        public bool IsStudentWeeklyBusy { get; private set; }
        private void SetStudentWeeklyBusy(bool busy)
        {
            if (IsStudentWeeklyBusy == busy) return;
            IsStudentWeeklyBusy = busy;
            OnPropertyChanged(nameof(IsStudentWeeklyBusy));
            RaiseAllCanExec();
        }

        // ===== Status text (students list)
        private string _studentWeeklyStudentsStatusText = "Select a department, then type/select a student.";
        public string StudentWeeklyStudentsStatusText
        {
            get => _studentWeeklyStudentsStatusText;
            private set
            {
                if (string.Equals(_studentWeeklyStudentsStatusText, value, StringComparison.Ordinal)) return;
                _studentWeeklyStudentsStatusText = value;
                OnPropertyChanged();
            }
        }

        // ===== Pickers
        public ObservableCollection<Department> StudentWeeklyDepartments { get; } = new();
        public ObservableCollection<StudentLiteRow> StudentWeeklyStudents { get; } = new();

        private int _studentWeeklyDepartmentId;
        public int StudentWeeklyDepartmentId
        {
            get => _studentWeeklyDepartmentId;
            set
            {
                if (_studentWeeklyDepartmentId == value) return;
                _studentWeeklyDepartmentId = value;
                OnPropertyChanged();

                // Reset students + selection when department changes
                StudentWeeklyStudents.Clear();
                SelectedStudentWeeklyStudent = null;
                SetStudentWeeklyStudentTextSilently(null);
                StudentWeeklyStudentsStatusText = _studentWeeklyDepartmentId > 0
                    ? "Loading students..."
                    : "Select a department, then type/select a student.";

                _studentWeeklyStudentsCache.Clear();
                _studentWeeklyStudentsCacheOrder.Clear();

                _ = ReloadStudentWeeklyStudentsAsync();
                RaiseAllCanExec();
            }
        }

        private StudentLiteRow? _selectedStudentWeeklyStudent;
        public StudentLiteRow? SelectedStudentWeeklyStudent
        {
            get => _selectedStudentWeeklyStudent;
            set
            {
                if (_selectedStudentWeeklyStudent?.Id == value?.Id) return;
                _selectedStudentWeeklyStudent = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectedStudentWeeklyStudentId));

                // Keep text in sync without triggering DB reload
                _suppressStudentWeeklyTextReload = true;
                try
                {
                    SetStudentWeeklyStudentTextSilently(value?.FullName);
                }
                finally
                {
                    _suppressStudentWeeklyTextReload = false;
                }

                // Weekly grid rendering lives in WeeklyGrid.cs
                _ = ReloadStudentWeeklyScheduleAsync();
            }
        }

        public int SelectedStudentWeeklyStudentId => SelectedStudentWeeklyStudent?.Id ?? 0;

        // ===== Student Text (editable ComboBox)
        private bool _suppressStudentWeeklyTextReload;
        private string? _studentWeeklyStudentText;
        public string? StudentWeeklyStudentText
        {
            get => _studentWeeklyStudentText;
            set
            {
                if (string.Equals(_studentWeeklyStudentText, value, StringComparison.Ordinal)) return;
                _studentWeeklyStudentText = value;
                OnPropertyChanged();

                if (_suppressStudentWeeklyTextReload) return;

                // If user is typing, clear selection (so schedule won't show stale student)
                if (SelectedStudentWeeklyStudent is not null)
                {
                    // WPF Editable ComboBox may temporarily push text that is not exactly the same
                    // as the selected student's display text (e.g., includes StudentNo or different spacing).
                    // We therefore compare using trimmed culture-aware comparison and allow "contains".
                    var selectedName = (SelectedStudentWeeklyStudent.Value.FullName ?? string.Empty).Trim();
                    var typed = (value ?? string.Empty).Trim();

                    bool isSame = string.Equals(typed, selectedName, StringComparison.CurrentCulture);
                    if (!isSame && !string.IsNullOrWhiteSpace(selectedName))
                    {
                        // Allow cases like "12345 — Student Name ..." where the name is still present.
                        isSame = typed.Contains(selectedName, StringComparison.CurrentCulture);
                    }

                    if (!isSame)
                        SelectedStudentWeeklyStudent = null;
                }

                _ = DebouncedReloadStudentWeeklyStudentsAsync();
            }
        }

        private void SetStudentWeeklyStudentTextSilently(string? text)
        {
            if (string.Equals(_studentWeeklyStudentText, text, StringComparison.Ordinal)) return;
            _studentWeeklyStudentText = text;
            OnPropertyChanged(nameof(StudentWeeklyStudentText));
        }

        // ===== Debounce + cancellation
        private CancellationTokenSource? _studentWeeklyLoadCts;
        private CancellationTokenSource? _studentWeeklyDebounceCts;

        // ===== Small LRU cache for (deptId + search)
        private readonly Dictionary<StudentsCacheKey, StudentsCacheEntry> _studentWeeklyStudentsCache = new();
        private readonly LinkedList<StudentsCacheKey> _studentWeeklyStudentsCacheOrder = new();
        private const int StudentWeeklyStudentsCacheMax = 12;
        private const int StudentWeeklyTake = 400;

        private readonly record struct StudentsCacheKey(int DepartmentId, string? Search);

        private sealed class StudentsCacheEntry
        {
            public List<StudentLiteRow> Items { get; } = new();
            public DateTime LastUsedUtc { get; set; } = DateTime.UtcNow;
        }

        private StudentsCacheKey BuildStudentsCacheKey(int deptId, string? search)
        {
            var s = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
            return new StudentsCacheKey(deptId, s);
        }

        private void TouchStudentsCacheKey(StudentsCacheKey key)
        {
            if (_studentWeeklyStudentsCacheOrder.First?.Value.Equals(key) == true) return;

            var node = _studentWeeklyStudentsCacheOrder.Find(key);
            if (node is not null)
            {
                _studentWeeklyStudentsCacheOrder.Remove(node);
                _studentWeeklyStudentsCacheOrder.AddFirst(node);
            }
            else
            {
                _studentWeeklyStudentsCacheOrder.AddFirst(key);
            }

            while (_studentWeeklyStudentsCacheOrder.Count > StudentWeeklyStudentsCacheMax)
            {
                var last = _studentWeeklyStudentsCacheOrder.Last!.Value;
                _studentWeeklyStudentsCacheOrder.RemoveLast();
                _studentWeeklyStudentsCache.Remove(last);
            }
        }

        private void ApplyStudentsCacheToUi(StudentsCacheKey key, StudentsCacheEntry entry)
        {
            TouchStudentsCacheKey(key);
            StudentWeeklyStudents.Clear();
            foreach (var s in entry.Items)
                StudentWeeklyStudents.Add(s);
            StudentWeeklyStudentsStatusText = BuildStudentsStatusText(StudentWeeklyStudents.Count);
        }

        private static string BuildStudentsStatusText(int shown)
        {
            if (shown <= 0) return "No results found.";
            return shown >= StudentWeeklyTake ? $"Showing: {shown} (type to search)" : $"Showing: {shown}";
        }

        // ===== Commands
        public ICommand RefreshStudentWeeklyStudentsCommand { get; private set; } = null!;

        internal void InitStudentWeeklyViewCommands()
        {
            RefreshStudentWeeklyStudentsCommand = new RelayCommand(async _ => await ReloadStudentWeeklyStudentsAsync(force: true), _ => !IsStudentWeeklyBusy && StudentWeeklyDepartmentId > 0);

            // Build initial departments list (when Departments already synced)
            RefreshStudentWeeklyDepartmentGroups();
        }

        /// <summary>
        /// Keep old method name (called from other places) but now it just populates departments.
        /// </summary>
        public void RefreshStudentWeeklyDepartmentGroups()
        {
            StudentWeeklyDepartments.Clear();
            StudentWeeklyStudents.Clear();
            SelectedStudentWeeklyStudent = null;
            SetStudentWeeklyStudentTextSilently(null);

            _studentWeeklyStudentsCache.Clear();
            _studentWeeklyStudentsCacheOrder.Clear();

            var src = (Departments?.ToList() ?? new List<Department>())
                .Where(d => d is not null && d.Id > 0 && !string.IsNullOrWhiteSpace(d.Name))
                .OrderBy(d => d.Name)
                .ToList();

            if (src.Count == 0)
            {
                _studentWeeklyDepartmentId = 0;
                OnPropertyChanged(nameof(StudentWeeklyDepartmentId));
                StudentWeeklyStudentsStatusText = "No departments available.";
                RaiseAllCanExec();
                return;
            }

            foreach (var d in src)
                StudentWeeklyDepartments.Add(d);

            // Supervisors: force to their department
            if (AuthContext.IsSupervisor)
            {
                var locked = AuthContext.Current?.DepartmentId;
                if (locked is int lockedId && lockedId > 0)
                {
                    StudentWeeklyDepartmentId = lockedId;
                    return;
                }
            }

            // Keep selection if possible
            if (StudentWeeklyDepartmentId <= 0 || !StudentWeeklyDepartments.Any(d => d.Id == StudentWeeklyDepartmentId))
                StudentWeeklyDepartmentId = StudentWeeklyDepartments.First().Id;
            else
                _ = ReloadStudentWeeklyStudentsAsync();

            RaiseAllCanExec();
        }

        private async Task DebouncedReloadStudentWeeklyStudentsAsync()
        {
            _studentWeeklyDebounceCts?.Cancel();
            var cts = new CancellationTokenSource();
            _studentWeeklyDebounceCts = cts;

            try
            {
                await Task.Delay(250, cts.Token);
            }
            catch
            {
                return;
            }

            await ReloadStudentWeeklyStudentsAsync();
        }

        private async Task ReloadStudentWeeklyStudentsAsync(bool force = false)
        {
            var deptId = StudentWeeklyDepartmentId;
            if (deptId <= 0)
            {
                StudentWeeklyStudents.Clear();
                StudentWeeklyStudentsStatusText = "Select a department, then type/select a student.";
                RaiseAllCanExec();
                return;
            }

            // Supervisors should never load outside their department.
            if (AuthContext.IsSupervisor)
            {
                var locked = AuthContext.Current?.DepartmentId;
                if (locked is int lockedId && lockedId > 0 && deptId != lockedId)
                {
                    StudentWeeklyDepartmentId = lockedId;
                    return;
                }
            }

            var prevSelectedId = SelectedStudentWeeklyStudent?.Id ?? 0;

            var search = string.IsNullOrWhiteSpace(StudentWeeklyStudentText) ? null : StudentWeeklyStudentText.Trim();
            // If selected student exists and text matches it exactly, treat as no search
            if (SelectedStudentWeeklyStudent is not null)
            {
                var sel = SelectedStudentWeeklyStudent.Value.FullName;
                if (string.Equals(search, sel, StringComparison.Ordinal))
                    search = null;
            }

            var cacheKey = BuildStudentsCacheKey(deptId, search);
            if (!force && _studentWeeklyStudentsCache.TryGetValue(cacheKey, out var cached) && cached.Items.Count > 0)
            {
                ApplyStudentsCacheToUi(cacheKey, cached);
                RaiseAllCanExec();
                return;
            }

            _studentWeeklyLoadCts?.Cancel();
            var cts = new CancellationTokenSource();
            _studentWeeklyLoadCts = cts;

            SetStudentWeeklyBusy(true);
            StudentWeeklyStudentsStatusText = "Loading students...";

            try
            {
                List<StudentLiteRow> list;

                if (Store.Students.Count > 0)
                {

                    if (AuthContext.IsSupervisor && AuthContext.Current?.DepartmentId is int sDept && sDept > 0 && deptId != sDept)
                    {
                        list = new List<StudentLiteRow>();
                    }
                    else
                    {
                        var assignmentsById = Store.Assignments
                            .GroupBy(a => a.Id)
                            .ToDictionary(g => g.Key, g => g.First());

                        var coursesById = Store.Courses
                            .GroupBy(c => c.Id)
                            .ToDictionary(g => g.Key, g => g.First());

                        var studentIds = new HashSet<int>();
                        foreach (var enr in Store.StudentEnrollments)
                        {
                            if (enr.StudentId <= 0) continue;
                            if (!assignmentsById.TryGetValue((int)enr.AssignmentId, out var asg)) continue;
                            if (!coursesById.TryGetValue(asg.CourseId, out var course)) continue;
                            if (course.DepartmentId != deptId) continue;
                            studentIds.Add(enr.StudentId);
                        }

                        IEnumerable<Student> src = Store.Students.Where(s => studentIds.Contains(s.Id));

                        if (search is not null)
                        {
                            var q = search;
                            src = src.Where(s =>
                                (!string.IsNullOrWhiteSpace(s.FullName) && s.FullName!.Contains(q, StringComparison.CurrentCultureIgnoreCase)) ||
                                (!string.IsNullOrWhiteSpace(s.StudentNo) && s.StudentNo!.Contains(q, StringComparison.CurrentCultureIgnoreCase)));
                        }

                        list = src
                            .OrderBy(s => s.FullName)
                            .Take(StudentWeeklyTake)
                            .Select(s => new StudentLiteRow(s.Id, s.StudentNo, s.FullName ?? string.Empty, s.Level))
                            .Where(s => s.Id > 0 && !string.IsNullOrWhiteSpace(s.FullName))
                            .ToList();
                    }
                }
                else
                {
                    list = await _sqlite.GetStudentsLiteByDepartmentAsync(deptId, searchText: search, skip: 0, take: StudentWeeklyTake);
                }
                if (cts.IsCancellationRequested) return;

                StudentWeeklyStudents.Clear();
                foreach (var s in list)
                    StudentWeeklyStudents.Add(s);

                // Keep selection if possible, otherwise auto-select (faculty-like UX)
                if (StudentWeeklyStudents.Count > 0)
                {
                    // 1) Keep previous selection if it still exists
                    if (prevSelectedId > 0)
                    {
                        var keep = StudentWeeklyStudents.FirstOrDefault(x => x.Id == prevSelectedId);
                        if (keep.Id == prevSelectedId)
                            SelectedStudentWeeklyStudent = keep;
                    }
                    // 2) If user is not searching (empty text) and nothing selected, auto select first
                    if (SelectedStudentWeeklyStudent is null && search is null)
                        SelectedStudentWeeklyStudent = StudentWeeklyStudents[0];

                    // 3) If search yields exactly one match, auto select it
                    if (SelectedStudentWeeklyStudent is null && StudentWeeklyStudents.Count == 1)
                        SelectedStudentWeeklyStudent = StudentWeeklyStudents[0];
                }

                var entry = new StudentsCacheEntry();
                entry.Items.AddRange(StudentWeeklyStudents);
                entry.LastUsedUtc = DateTime.UtcNow;
                _studentWeeklyStudentsCache[cacheKey] = entry;
                TouchStudentsCacheKey(cacheKey);

                StudentWeeklyStudentsStatusText = BuildStudentsStatusText(StudentWeeklyStudents.Count);
            }
            catch
            {
                StudentWeeklyStudents.Clear();
                StudentWeeklyStudentsStatusText = "Unable to load students. Please check the database.";
            }
            finally
            {
                SetStudentWeeklyBusy(false);
                RaiseAllCanExec();
            }
        }
    }
}
