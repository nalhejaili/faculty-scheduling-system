using MiniTrainerScheduler.Models;
using MiniTrainerScheduler.ViewModels;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using TrainerScheduler.Security;

namespace MiniTrainerScheduler.Views
{
    public partial class CourseFacultyOverridesWindow : Window
    {
        private ICollectionView? _coursesView;
        private ICollectionView? _facultiesView;
        private ICollectionView? _cfoRowsView;

        private int? _scopeDepartmentId; // supervisors are locked to a single department

        private const string AllValue = "(All)";
        private readonly Dictionary<string, Dictionary<string, List<int>>> _deptIdsByMajorSpecialty
            = new(StringComparer.OrdinalIgnoreCase);

        private HashSet<int>? _selectedDeptIds;
        private int? _selectedLevel; // null => all

        public CourseFacultyOverridesWindow(MainViewModel vm)
        {
            InitializeComponent();
            DataContext = vm;
            Loaded += CourseFacultyOverridesWindow_Loaded;

            CourseFilterBox.TextChanged += (_, __) => _coursesView?.Refresh();
            FacultyFilterBox.TextChanged += (_, __) => _facultiesView?.Refresh();
        }

        private void CourseFacultyOverridesWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MainViewModel vm)
                return;

            _scopeDepartmentId = vm.IsSupervisor ? AuthContext.Current?.DepartmentId : null;

            if (vm.IsSupervisor)
            {
                // Make the labels accurate for supervisors.
                ApplyAllSameNameChk.Content = "Apply to all courses with the same name (within the department)";
                UseCourseCodeChk.Content = "Match by course code (within the department)";
                CodeScopeAllDeptsChk.Visibility = Visibility.Collapsed;
                CodeScopeAllDeptsChk.IsChecked = false;
            }

            // When using CourseCode mode, 'apply all same name' is irrelevant.
            void RefreshModeToggles()
            {
                var useCode = UseCourseCodeChk.IsChecked == true;
                ApplyAllSameNameChk.IsEnabled = !useCode;
                if (!useCode) CodeScopeAllDeptsChk.IsChecked = false;
                CodeScopeAllDeptsChk.IsEnabled = useCode && !vm.IsSupervisor;
            }
            UseCourseCodeChk.Checked += (_, __) => RefreshModeToggles();
            UseCourseCodeChk.Unchecked += (_, __) => RefreshModeToggles();
            RefreshModeToggles();

            vm.EnsureOverridesUiSynced();

            SetupDepartmentSpecialtyLevelSelectors(vm);

            _coursesView = CollectionViewSource.GetDefaultView(vm.Courses);
            _coursesView.Filter = it =>
            {
                if (it is not Course c) return false;

                // Supervisors: only courses from their locked department.
                if (_scopeDepartmentId is int deptId && c.DepartmentId != deptId)
                    return false;

                // UI selections (department/specialty/level)
                if (_selectedDeptIds is { Count: > 0 } ids && !ids.Contains(c.DepartmentId))
                    return false;

                if (_selectedLevel is int lvl && c.Level != lvl)
                    return false;

                var q = (CourseFilterBox.Text ?? "").Trim();
                if (q.Length == 0) return true;
                return (c.DisplayName ?? "").Contains(q, StringComparison.OrdinalIgnoreCase);
            };

            _facultiesView = CollectionViewSource.GetDefaultView(vm.Faculties);
            _facultiesView.Filter = it =>
            {
                if (it is not Faculty f) return false;

                // Supervisors: only faculty from their locked department.
                if (_scopeDepartmentId is int deptId && f.DepartmentId != deptId)
                    return false;

                var q = (FacultyFilterBox.Text ?? "").Trim();
                if (q.Length == 0) return true;
                return (f.Name ?? "").Contains(q, StringComparison.OrdinalIgnoreCase);
            };

            // Supervisors: show only their department overrides in the grid.
            _cfoRowsView = CollectionViewSource.GetDefaultView(vm.CfoRows);
            _cfoRowsView.Filter = it =>
            {
                if (_scopeDepartmentId is not int deptId)
                    return true;

                if (it is not CourseFacultyOverrideRow row)
                    return false;

                // Code-level overrides are already scoped.
                if (row.IsCodeMode || (row.CourseId <= 0 && !string.IsNullOrWhiteSpace(row.CourseCode)))
                    return row.ScopeDepartmentId == deptId;

                // CourseId-level overrides: scope by course department.
                if (row.ScopeDepartmentId != 0)
                    return row.ScopeDepartmentId == deptId;

                var course = vm.Store.Courses.FirstOrDefault(x => x.Id == row.CourseId);
                return course is not null && course.DepartmentId == deptId;
            };

            _coursesView.Refresh();
            _facultiesView.Refresh();
            _cfoRowsView.Refresh();
        }

        private void SetupDepartmentSpecialtyLevelSelectors(MainViewModel vm)
        {
            // Build department -> (major, specialty) mapping.
            _deptIdsByMajorSpecialty.Clear();

            var departments = vm.Store.Departments.AsEnumerable();
            if (_scopeDepartmentId is int deptScope)
                departments = departments.Where(d => d.Id == deptScope);

            foreach (var d in departments)
            {
                var name = (d.Name ?? "").Trim();
                if (name.Length == 0) continue;

                SplitMajorSpecialty(name, out var major, out var specialty);
                major = major.Trim();
                specialty = specialty.Trim();

                if (major.Length == 0)
                    major = name;
                if (specialty.Length == 0)
                    specialty = "General";

                if (!_deptIdsByMajorSpecialty.TryGetValue(major, out var specs))
                {
                    specs = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
                    _deptIdsByMajorSpecialty[major] = specs;
                }

                if (!specs.TryGetValue(specialty, out var ids))
                {
                    ids = new List<int>();
                    specs[specialty] = ids;
                }

                if (!ids.Contains(d.Id))
                    ids.Add(d.Id);
            }

            var majors = _deptIdsByMajorSpecialty.Keys.OrderBy(x => x).ToList();
            var majorItems = new List<string>();
            if (_scopeDepartmentId is null)
                majorItems.Add(AllValue);
            majorItems.AddRange(majors);

            DeptMajorCombo.ItemsSource = majorItems;

            if (_scopeDepartmentId is null)
            {
                // Default to first real major if available (reduces list sizes).
                DeptMajorCombo.SelectedIndex = majorItems.Count > 1 ? 1 : 0;
            }
            else
            {
                DeptMajorCombo.SelectedIndex = 0;
                DeptMajorCombo.IsEnabled = false;
            }

            // Trigger refresh of dependent combos.
            RefreshSpecialtyAndLevel();
        }

        private static void SplitMajorSpecialty(string fullName, out string major, out string specialty)
        {
            major = fullName;
            specialty = "";

            if (string.IsNullOrWhiteSpace(fullName))
                return;

            var s = fullName.Trim();

            // Prefer splitting by common tokens " - " / " – " / "/".
            string[] tokens = { " - ", " – ", " — ", " / ", "/", "-", "–", "—" };
            foreach (var t in tokens)
            {
                var idx = s.IndexOf(t, StringComparison.Ordinal);
                if (idx <= 0) continue;

                major = s.Substring(0, idx).Trim();
                specialty = s.Substring(idx + t.Length).Trim();
                return;
            }
        }

        private void DeptMajorCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            RefreshSpecialtyAndLevel();
        }

        private void DeptSpecialtyCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            RefreshLevelOnly();
            _coursesView?.Refresh();
            CourseCombo.SelectedItem = null;
        }

        private void CourseLevelCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            var sel = CourseLevelCombo.SelectedItem;
            _selectedLevel = sel is int i ? i : null;
            _coursesView?.Refresh();
            CourseCombo.SelectedItem = null;
        }

        private void RefreshSpecialtyAndLevel()
        {
            var selectedMajor = DeptMajorCombo.SelectedItem as string;

            // If invalid selection, fall back to first item.
            if (string.IsNullOrWhiteSpace(selectedMajor) && DeptMajorCombo.Items.Count > 0)
                selectedMajor = DeptMajorCombo.Items[0] as string;

            if (string.IsNullOrWhiteSpace(selectedMajor))
                selectedMajor = AllValue;

            // Specialty list
            if (_scopeDepartmentId is not null)
            {
                // Locked: only the single matching specialty.
                var major = _deptIdsByMajorSpecialty.Keys.FirstOrDefault() ?? AllValue;
                selectedMajor = major;
                var specs = _deptIdsByMajorSpecialty.TryGetValue(major, out var m) ? m.Keys.OrderBy(x => x).ToList() : new List<string>();
                DeptSpecialtyCombo.ItemsSource = specs;
                DeptSpecialtyCombo.SelectedIndex = specs.Count > 0 ? 0 : -1;
                DeptSpecialtyCombo.IsEnabled = false;
            }
            else if (selectedMajor == AllValue)
            {
                DeptSpecialtyCombo.ItemsSource = new List<string> { AllValue };
                DeptSpecialtyCombo.SelectedIndex = 0;
                DeptSpecialtyCombo.IsEnabled = false;
            }
            else
            {
                var specs = _deptIdsByMajorSpecialty.TryGetValue(selectedMajor, out var m)
                    ? m.Keys.OrderBy(x => x).ToList()
                    : new List<string>();

                var items = new List<string> { AllValue };
                items.AddRange(specs);
                DeptSpecialtyCombo.ItemsSource = items;
                DeptSpecialtyCombo.SelectedIndex = 0;
                DeptSpecialtyCombo.IsEnabled = true;
            }

            RefreshLevelOnly();
            _coursesView?.Refresh();
            CourseCombo.SelectedItem = null;
        }

        private void RefreshLevelOnly()
        {
            _selectedDeptIds = ComputeSelectedDepartmentIds();

            if (DataContext is not MainViewModel vm)
                return;

            var deptIds = _selectedDeptIds;

            var levels = vm.Store.Courses
                .Where(c => deptIds is null || deptIds.Count == 0 || deptIds.Contains(c.DepartmentId))
                .Select(c => c.Level)
                .Where(l => l > 0)
                .Distinct()
                .OrderBy(l => l)
                .Cast<object>()
                .ToList();

            var levelItems = new List<object> { AllValue };
            levelItems.AddRange(levels);
            CourseLevelCombo.ItemsSource = levelItems;
            CourseLevelCombo.SelectedIndex = 0;
            _selectedLevel = null;
        }

        private HashSet<int> ComputeSelectedDepartmentIds()
        {
            if (_scopeDepartmentId is int deptScope)
                return new HashSet<int> { deptScope };

            var major = DeptMajorCombo.SelectedItem as string;
            var spec = DeptSpecialtyCombo.SelectedItem as string;

            if (string.IsNullOrWhiteSpace(major) || major == AllValue)
            {
                // All majors
                return new HashSet<int>(_deptIdsByMajorSpecialty.Values.SelectMany(v => v.Values).SelectMany(x => x));
            }

            if (!_deptIdsByMajorSpecialty.TryGetValue(major, out var specs))
                return new HashSet<int>();

            if (string.IsNullOrWhiteSpace(spec) || spec == AllValue)
            {
                return new HashSet<int>(specs.Values.SelectMany(x => x));
            }

            return specs.TryGetValue(spec, out var ids)
                ? new HashSet<int>(ids)
                : new HashSet<int>();
        }

        private static string NormalizeName(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            var parts = s.Trim().Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            return string.Join(" ", parts);
        }


        private static string NormalizeCourseCode(string? code)
        {
            if (string.IsNullOrWhiteSpace(code)) return "";
            // Normalization: Trim + Upper + remove internal spaces
            return new string(code.Trim().ToUpperInvariant().Where(ch => !char.IsWhiteSpace(ch)).ToArray());
        }

        private void AddOverrides_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MainViewModel vm)
                return;

            if (CourseCombo.SelectedItem is not Course selectedCourse)
            {
                MessageBox.Show("Select a course first.", "Notice", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Supervisors: must be within their department.
            if (_scopeDepartmentId is int deptIdScope && selectedCourse.DepartmentId != deptIdScope)
            {
                MessageBox.Show("As a department supervisor, you can assign courses only within your department.", "Permissions", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var selectedFaculties = FacultyList.SelectedItems.Cast<object>()
                .OfType<Faculty>()
                .ToList();

            // Supervisors: safety filter (UI already filters, but we enforce anyway).
            if (_scopeDepartmentId is int deptIdFac)
                selectedFaculties = selectedFaculties.Where(f => f.DepartmentId == deptIdFac).ToList();

            if (selectedFaculties.Count == 0)
            {
                MessageBox.Show("Select at least one faculty member.", "Notice", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }


            var useCourseCode = UseCourseCodeChk.IsChecked == true;

            // Code-level override: (ScopeDepartmentId, CourseCode) -> allowed FacultyIds
            if (useCourseCode)
            {
                var code = NormalizeCourseCode(selectedCourse.CourseCode);
                if (string.IsNullOrWhiteSpace(code))
                {
                    MessageBox.Show("This course does not have a CourseCode value.\n\nAdd a course code and try again.", "Notice",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // Scope:
                // - Supervisors: always locked to their department.
                // - Admin: if 'all departments' checked => 0, else scope = department of the selected course.
                var scopeDepartmentId = _scopeDepartmentId ?? (CodeScopeAllDeptsChk.IsChecked == true ? 0 : selectedCourse.DepartmentId);

                // If scoped to a department, enforce faculty is from that department (requested behavior for supervisors and scoped admins).
                if (scopeDepartmentId != 0 && selectedFaculties.Any(f => f.DepartmentId != scopeDepartmentId))
                {
                    MessageBox.Show("When matching by course code within a specific department, all selected faculty members must belong to the same department.", "Notice",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var existingCode = new HashSet<(int ScopeId, string Code, int FacultyId)>(
                    vm.Store.CourseCodeFacultyOverrides.Select(o => (o.ScopeDepartmentId, NormalizeCourseCode(o.CourseCode), o.FacultyId)));

                var addedCode = 0;
                foreach (var f in selectedFaculties)
                {
                    var key = (scopeDepartmentId, code, f.Id);
                    if (existingCode.Contains(key))
                        continue;

                    vm.Store.CourseCodeFacultyOverrides.Add(new CourseCodeFacultyOverride(scopeDepartmentId, code, f.Id));
                    existingCode.Add(key);
                    addedCode++;
                }

                vm.EnsureOverridesUiSynced(cfoOnly: true);
                vm.SaveCourseCodeFacultyOverridesToDatabase();

                if (addedCode == 0)
                {
                    MessageBox.Show("There is nothing new to add; all selected assignments already exist.", "Completed",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                MessageBox.Show($"{addedCode} code-based assignment(s) were added successfully.", "Completed",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var applyAllSameName = ApplyAllSameNameChk.IsChecked == true;
            var courseIds = new List<int>();

            if (applyAllSameName)
            {
                var key = NormalizeName(selectedCourse.Name);
                courseIds = vm.Store.Courses
                    .Where(c => NormalizeName(c.Name) == key)
                    .Where(c => _scopeDepartmentId is not int d || c.DepartmentId == d)
                    .Select(c => c.Id)
                    .Distinct()
                    .ToList();

                if (courseIds.Count == 0)
                    courseIds.Add(selectedCourse.Id);
            }
            else
            {
                courseIds.Add(selectedCourse.Id);
            }

            var existing = new HashSet<(int CourseId, int FacultyId)>(
                vm.Store.CourseFacultyOverrides.Select(o => (o.CourseId, o.FacultyId)));

            var added = 0;
            foreach (var cid in courseIds)
            {
                foreach (var f in selectedFaculties)
                {
                    var key = (cid, f.Id);
                    if (existing.Contains(key))
                        continue;

                    vm.Store.CourseFacultyOverrides.Add(new CourseFacultyOverride(cid, f.Id));
                    existing.Add(key);
                    added++;
                }
            }

            vm.EnsureOverridesUiSynced(cfoOnly: true);

            vm.SaveCourseFacultyOverridesToDatabase();

            if (added == 0)
            {
                MessageBox.Show("There is nothing new to add; all selected assignments already exist.", "Completed", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            MessageBox.Show($"{added} assignment(s) were added successfully.", "Completed", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
