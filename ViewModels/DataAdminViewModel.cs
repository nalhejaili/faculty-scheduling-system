using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Data;
using Microsoft.Win32;
using TrainerScheduler.Data.Entities;
using TrainerScheduler.Services;

namespace MiniTrainerScheduler.ViewModels
{
    public sealed class DataAdminViewModel : INotifyPropertyChanged
    {
        private readonly MainViewModel _main;
        private readonly SqliteStoreService _sqlite = new();
        private readonly System.Collections.Generic.Dictionary<int, string> _courseNameById = new();


public event PropertyChangedEventHandler? PropertyChanged;
private void OnPropertyChanged([CallerMemberName] string? name = null)
    => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

private string _legacyMaintenanceReport = string.Empty;
public string LegacyMaintenanceReport
{
    get => _legacyMaintenanceReport;
    private set
    {
        if (_legacyMaintenanceReport == value) return;
        _legacyMaintenanceReport = value;
        OnPropertyChanged();
    }
}

public ICommand DiagnoseLegacyCommand { get; }
public ICommand RunLegacyMaintenanceCommand { get; }
public ICommand CopyLegacyReportCommand { get; }



        public ObservableCollection<DepartmentEntity> Departments { get; } = new();
        public ObservableCollection<FacultyEntity> Faculties { get; } = new();
        public ObservableCollection<CourseEntity> Courses { get; } = new();
        public ObservableCollection<RoomEntity> Rooms { get; } = new();

public ObservableCollection<StudentEntity> Students { get; } = new();
public ObservableCollection<StudentPlanPreviewRow> SelectedStudentPlans { get; } = new();
public ObservableCollection<CourseEntity> StudentPlanCourseOptions { get; } = new();

private StudentPlanPreviewRow? _selectedStudentPlan;
public StudentPlanPreviewRow? SelectedStudentPlan
{
    get => _selectedStudentPlan;
    set
    {
        if (ReferenceEquals(_selectedStudentPlan, value)) return;
        _selectedStudentPlan = value;
        OnPropertyChanged();
    }
}

private string _selectedStudentRegistrationSummary = "Select a student to review smart academic registration rules.";
public string SelectedStudentRegistrationSummary
{
    get => _selectedStudentRegistrationSummary;
    private set
    {
        if (_selectedStudentRegistrationSummary == value) return;
        _selectedStudentRegistrationSummary = value;
        OnPropertyChanged();
    }
}


        public ObservableCollection<CourseCodeMapEntity> CourseCodeMaps { get; } = new();

        private const string AllOption = "(All)";

        public ObservableCollection<string> DepartmentBaseOptions { get; } = new();
        public ObservableCollection<string> SpecialtyOptions { get; } = new();
        public ObservableCollection<string> LevelOptions { get; } = new();

        private string _selectedDepartmentBase = AllOption;
        public string SelectedDepartmentBase
        {
            get => _selectedDepartmentBase;
            set
            {
                if (_selectedDepartmentBase == value) return;
                _selectedDepartmentBase = string.IsNullOrWhiteSpace(value) ? AllOption : value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsSpecialtyEnabled));
                RebuildSpecialtyOptions();
                RebuildLevelOptions();
				RebuildFilteredCourseOptions();
            }
        }

        private string _selectedSpecialty = AllOption;
        public string SelectedSpecialty
        {
            get => _selectedSpecialty;
            set
            {
                if (_selectedSpecialty == value) return;
                _selectedSpecialty = string.IsNullOrWhiteSpace(value) ? AllOption : value;
                OnPropertyChanged();
                RebuildLevelOptions();
				RebuildFilteredCourseOptions();
            }
        }

        private string _selectedLevelOption = AllOption;
        public string SelectedLevelOption
        {
            get => _selectedLevelOption;
            set
            {
                if (_selectedLevelOption == value) return;
                _selectedLevelOption = string.IsNullOrWhiteSpace(value) ? AllOption : value;
                OnPropertyChanged();
				RebuildFilteredCourseOptions();
            }
        }

        public bool IsSpecialtyEnabled => SelectedDepartmentBase != AllOption;

        // A plain filtered list for course pickers in the mapping grid.
        // IMPORTANT: Do NOT expose a shared ICollectionView as ItemsSource for the
        // DataGrid ComboBoxes. A shared ICollectionView has a shared CurrentItem, and
        // multiple ComboBoxes may appear to "lock" on a single course (e.g., showing
        // the same course for all rows) even when data is correct.
        public ObservableCollection<CourseEntity> FilteredCourseOptions { get; } = new();

        public ICommand ClearCourseFiltersCommand { get; }

        public DepartmentEntity? SelectedDepartment { get; set; }
        public FacultyEntity? SelectedFaculty { get; set; }
        public CourseEntity? SelectedCourse { get; set; }
        public RoomEntity? SelectedRoom { get; set; }

private StudentEntity? _selectedStudent;
public StudentEntity? SelectedStudent
{
    get => _selectedStudent;
    set
    {
        if (ReferenceEquals(_selectedStudent, value)) return;
        _selectedStudent = value;
        OnPropertyChanged();
        RebuildStudentPlanCourseOptions();
        RefreshSelectedStudentRegistrationSummary();
        _ = ReloadSelectedStudentPlansAsync();
    }
}



        public CourseCodeMapEntity? SelectedCourseCodeMap { get; set; }

        public string DbPath => _sqlite.DatabasePath;

        public ICommand ReloadCommand { get; }
        public ICommand AddDepartmentCommand { get; }
        public ICommand DeleteDepartmentCommand { get; }
        public ICommand SaveDepartmentsCommand { get; }

        public ICommand AddFacultyCommand { get; }
        public ICommand DeleteFacultyCommand { get; }
        public ICommand SaveFacultiesCommand { get; }

        public ICommand AddCourseCommand { get; }
        public ICommand DeleteCourseCommand { get; }
        public ICommand SaveCoursesCommand { get; }

        public ICommand AddRoomCommand { get; }
        public ICommand DeleteRoomCommand { get; }
        public ICommand SaveRoomsCommand { get; }

        public ICommand AddStudentCommand { get; }
        public ICommand DeleteStudentCommand { get; }
        public ICommand SaveStudentsCommand { get; }
        public ICommand AddStudentPlanCommand { get; }
        public ICommand DeleteStudentPlanCommand { get; }
        public ICommand ValidateStudentRegistrationCommand { get; }
        public ICommand SaveStudentPlansCommand { get; }
        public ICommand ImportStudentsCsvCommand { get; }
        public ICommand ExportStudentImportTemplateCommand { get; }

        public ICommand SaveCourseCodeMapsCommand { get; }
        public ICommand DeleteCourseCodeMapCommand { get; }
        public ICommand ClearCourseCodeMapCourseCommand { get; }

        // Backup / Restore
        public ICommand BackupDatabaseCommand { get; }
        public ICommand RestoreDatabaseCommand { get; }
        public ICommand OpenDataFolderCommand { get; }

        public DataAdminViewModel(MainViewModel main)
        {
            _main = main;

			// Build a filtered *list* of Courses so the mapping ComboBoxes can be narrowed
            ClearCourseFiltersCommand = new RelayCommand(_ => ClearCourseFilters());

            ReloadCommand = new RelayCommand(async _ => await ReloadAsync());
            AddDepartmentCommand = new RelayCommand(_ => AddDepartment());
            DeleteDepartmentCommand = new RelayCommand(_ => DeleteDepartment());
            SaveDepartmentsCommand = new RelayCommand(async _ => await SaveDepartmentsAsync());

            AddFacultyCommand = new RelayCommand(_ => AddFaculty());
            DeleteFacultyCommand = new RelayCommand(_ => DeleteFaculty());
            SaveFacultiesCommand = new RelayCommand(async _ => await SaveFacultiesAsync());

            AddCourseCommand = new RelayCommand(_ => AddCourse());
            DeleteCourseCommand = new RelayCommand(_ => DeleteCourse());
            SaveCoursesCommand = new RelayCommand(async _ => await SaveCoursesAsync());

            AddRoomCommand = new RelayCommand(_ => AddRoom());
            DeleteRoomCommand = new RelayCommand(_ => DeleteRoom());
            SaveRoomsCommand = new RelayCommand(async _ => await SaveRoomsAsync());

            AddStudentCommand = new RelayCommand(_ => AddStudent());
            DeleteStudentCommand = new RelayCommand(async _ => await DeleteStudentAsync());
            SaveStudentsCommand = new RelayCommand(async _ => await SaveStudentsAsync());
            AddStudentPlanCommand = new RelayCommand(_ => AddStudentPlan());
            DeleteStudentPlanCommand = new RelayCommand(_ => DeleteStudentPlan());
            ValidateStudentRegistrationCommand = new RelayCommand(_ => ValidateSelectedStudentRegistration(showDialog: true));
            SaveStudentPlansCommand = new RelayCommand(async _ => await SaveStudentPlansAsync());
            ImportStudentsCsvCommand = new RelayCommand(async _ => await ImportStudentsCsvAsync());
            ExportStudentImportTemplateCommand = new RelayCommand(_ => SaveStudentImportTemplate());

            SaveCourseCodeMapsCommand = new RelayCommand(async _ => await SaveCourseCodeMapsAsync());
            DeleteCourseCodeMapCommand = new RelayCommand(_ => DeleteCourseCodeMap());
            ClearCourseCodeMapCourseCommand = new RelayCommand(_ => ClearCourseCodeMapCourse());

            BackupDatabaseCommand = new RelayCommand(_ => BackupDatabase());
            RestoreDatabaseCommand = new RelayCommand(_ => RestoreDatabase());
            OpenDataFolderCommand = new RelayCommand(_ => OpenDataFolder());

            DiagnoseLegacyCommand = new RelayCommand(async _ => await DiagnoseLegacyAsync());
            RunLegacyMaintenanceCommand = new RelayCommand(async _ => await RunLegacyMaintenanceAsync());
            CopyLegacyReportCommand = new RelayCommand(_ => CopyLegacyReport());

        }

        public async Task ReloadAsync()
        {
            try
            {
                // Loading lookups from SQLite can be slow on some machines / large DBs.
                // Run DB queries on a background thread so the UI doesn't freeze when
                // opening the window.
                var payload = await Task.Run(async () =>
                {
                    var depts = await _sqlite.GetDepartmentsAsync();
                    var facs = await _sqlite.GetFacultiesAsync();
                    var courses = await _sqlite.GetCoursesAsync();
                    var rooms = await _sqlite.GetRoomsAsync();
                    var maps = await _sqlite.GetCourseCodeMapsAsync();
                    var students = await _sqlite.GetStudentsAsync();
                    return (depts: depts, facs: facs, courses: courses, rooms: rooms, maps: maps, students: students);
                });

                Departments.Clear();
                Faculties.Clear();
                Courses.Clear();
                Rooms.Clear();
                CourseCodeMaps.Clear();
                Students.Clear();

                foreach (var d in payload.depts) Departments.Add(d);
                foreach (var f in payload.facs) Faculties.Add(f);
                foreach (var c in payload.courses) Courses.Add(c);

                _courseNameById.Clear();
                foreach (var c in payload.courses)
                    _courseNameById[c.Id] = c.Name ?? string.Empty;

                foreach (var r in payload.rooms) Rooms.Add(r);
                foreach (var m in payload.maps) CourseCodeMaps.Add(m);
                foreach (var s in payload.students) Students.Add(s);

                RebuildStudentPlanCourseOptions();
                SelectedStudentPlan = null;

                // Rebuild filter pickers used by the course-code mapping tab.
                RebuildDepartmentBaseOptions();
                RebuildSpecialtyOptions();
                RebuildLevelOptions();
				RebuildFilteredCourseOptions();

                SelectedStudent = null;
                RefreshSelectedStudentRegistrationSummary();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Unable to load data from SQLite.\n\n{ex.Message}", "Data Management",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }



        private async Task ReloadSelectedStudentPlansAsync()
        {
            try
            {
                SelectedStudentPlans.Clear();
                SelectedStudentPlan = null;

                if (SelectedStudent is null || SelectedStudent.Id <= 0)
                {
                    RefreshSelectedStudentRegistrationSummary();
                    return;
                }

                // Load from DB (read-only preview).
                var plans = await Task.Run(async () => await _sqlite.GetStudentPlansByStudentIdAsync(SelectedStudent.Id));

                foreach (var p in plans)
                {
                    var name = _courseNameById.TryGetValue(p.CourseId, out var n) ? n : $"#{p.CourseId}";
                    SelectedStudentPlans.Add(new StudentPlanPreviewRow
                    {
                        Id = p.Id,
                        CourseId = p.CourseId,
                        CourseName = name,
                        Priority = p.Priority,
                        IsRepeat = p.IsRepeat,
                        TermKey = AcademicTermKeyService.NormalizeForDisplay(p.TermKey),
                        CreatedUtc = p.CreatedUtc
                    });
                }

                RefreshSelectedStudentRegistrationSummary();
            }
            catch (Exception ex)
            {
                // Don't crash the UI - show a friendly message
				MessageBox.Show($"Unable to load the student's study plan.\n\n{ex.Message}", "Data Management",
					MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // ------------------------------
        // ------------------------------

        private void RebuildStudentPlanCourseOptions()
        {
            StudentPlanCourseOptions.Clear();

            if (SelectedStudent is null)
                return;

            var currentPlanCourseIds = SelectedStudentPlans
                .Where(p => p.CourseId > 0)
                .Select(p => p.CourseId)
                .ToHashSet();

            IEnumerable<CourseEntity> query = Courses.Where(c =>
                currentPlanCourseIds.Contains(c.Id) ||
                (StudentAcademicRegistrationService.IsCourseAllowedForStudent(SelectedStudent, c, Departments)
                 && StudentAcademicRegistrationService.IsCourseWithinLevelCeiling(SelectedStudent.Level, c)));

            query = query
                .OrderBy(c => GetStudentPlanCourseSortRank(c))
                .ThenBy(c => c.Level)
                .ThenBy(c => c.Name);

            foreach (var course in query)
                StudentPlanCourseOptions.Add(course);
        }

        private void AddStudent()
        {
            if (Departments.Count == 0)
            {
                MessageBox.Show("Add at least one department first.", "Data Management",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var student = new StudentEntity
            {
                Id = NextId(Students, x => x.Id),
                StudentNo = string.Empty,
                FullName = "New Student",
                DepartmentId = Departments.First().Id,
                Level = 1,
                CreatedUtc = DateTime.UtcNow
            };

            Students.Add(student);
            SelectedStudent = student;
        }

        private async Task DeleteStudentAsync()
        {
            if (SelectedStudent is null) return;

            var res = MessageBox.Show(
                "Do you want to delete the selected student and all related study-plan records?",
                "Confirmation",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (res != MessageBoxResult.Yes) return;

            try
            {
                await _sqlite.DeleteStudentAdminAsync(SelectedStudent.Id);
                Students.Remove(SelectedStudent);
                SelectedStudent = null;
                SelectedStudentPlans.Clear();
                SelectedStudentPlan = null;
                await _sqlite.TryLoadStudentsAsync(_main.Store, scopeDepartmentId: null);
                await _sqlite.TryLoadStudentPlansAsync(_main.Store, scopeDepartmentId: null);

                MessageBox.Show("The student record was deleted successfully.", "Data Management",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Unable to delete the student record.\n\n{ex.Message}", "Data Management",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task SaveStudentsAsync()
        {
            foreach (var student in Students)
            {
                student.StudentNo = string.IsNullOrWhiteSpace(student.StudentNo) ? null : student.StudentNo.Trim();
                student.FullName = (student.FullName ?? string.Empty).Trim();

                if (string.IsNullOrWhiteSpace(student.FullName))
                {
                    MessageBox.Show("There is a student record without a name. Edit the name before saving.", "Notice",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                if (student.DepartmentId is null || !Departments.Any(d => d.Id == student.DepartmentId.Value))
                {
                    MessageBox.Show($"Student '{student.FullName}' is linked to a department that does not exist. Edit the department before saving.", "Notice",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                if (student.Level is not null && student.Level <= 0)
                {
                    MessageBox.Show($"Student '{student.FullName}' has an invalid academic level. Set the level to 1 or higher.", "Notice",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
            }

            var duplicateStudentNo = Students
                .Where(s => !string.IsNullOrWhiteSpace(s.StudentNo))
                .GroupBy(s => s.StudentNo!, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(g => g.Count() > 1);

            if (duplicateStudentNo is not null)
            {
                MessageBox.Show($"A duplicate student number was found: {duplicateStudentNo.Key}. Resolve the duplicate and try again.",
                    "Notice", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                await _sqlite.SaveStudentsAdminAsync(Students);
                await ReloadAsync();
                await _sqlite.TryLoadStudentsAsync(_main.Store, scopeDepartmentId: null);
                await _sqlite.TryLoadStudentPlansAsync(_main.Store, scopeDepartmentId: null);
                await _main.ReloadCoreFromDatabaseAsync();
                RefreshSelectedStudentRegistrationSummary();
                MessageBox.Show("Student records saved successfully.", "Data Management",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Unable to save student records.\n\n{ex.Message}", "Data Management",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AddStudentPlan()
        {
            RebuildStudentPlanCourseOptions();

            if (SelectedStudent is null)
            {
                MessageBox.Show("Select a student first.", "Data Management",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (StudentPlanCourseOptions.Count == 0)
            {
                MessageBox.Show("No courses are available for the selected student's department.", "Data Management",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var defaultCourse = StudentPlanCourseOptions.First();
            SelectedStudentPlans.Add(new StudentPlanPreviewRow
            {
                CourseId = defaultCourse.Id,
                CourseName = defaultCourse.Name ?? string.Empty,
                Priority = 0,
                IsRepeat = false,
                TermKey = AcademicTermKeyService.CreateSuggestedGregorianTermKey(),
                CreatedUtc = DateTime.UtcNow
            });

            SelectedStudentPlan = SelectedStudentPlans.LastOrDefault();
            RefreshSelectedStudentRegistrationSummary();
        }

        private void DeleteStudentPlan()
        {
            if (SelectedStudentPlan is null) return;

            SelectedStudentPlans.Remove(SelectedStudentPlan);
            SelectedStudentPlan = SelectedStudentPlans.LastOrDefault();
            RefreshSelectedStudentRegistrationSummary();
        }

        private async Task SaveStudentPlansAsync()
        {
            RebuildStudentPlanCourseOptions();

            if (SelectedStudent is null)
            {
                MessageBox.Show("Select a student first.", "Data Management",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (string.IsNullOrWhiteSpace(SelectedStudent.FullName) || SelectedStudent.DepartmentId is null)
            {
                MessageBox.Show("Save the student profile first, then save the study plan.", "Data Management",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            foreach (var row in SelectedStudentPlans)
                row.CourseName = _courseNameById.TryGetValue(row.CourseId, out var name) ? name : row.CourseName;

            var validation = GetSelectedStudentRegistrationValidation();
            RefreshSelectedStudentRegistrationSummary(validation);

            if (validation.Errors.Count > 0)
            {
                MessageBox.Show(
                    "The study plan cannot be saved until the following issues are resolved:\n\n" +
                    string.Join("\n", validation.Errors.Distinct(StringComparer.OrdinalIgnoreCase).Take(12)),
                    "Smart Academic Registration",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (validation.Warnings.Count > 0)
            {
                var proceed = MessageBox.Show(
                    "The study plan passed the required checks, but there are notes to review:\n\n" +
                    string.Join("\n", validation.Warnings.Distinct(StringComparer.OrdinalIgnoreCase).Take(8)) +
                    "\n\nDo you want to save the study plan anyway?",
                    "Smart Academic Registration",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (proceed != MessageBoxResult.Yes)
                    return;
            }

            try
            {
                await _sqlite.SaveStudentsAdminAsync(Students);

                var rows = SelectedStudentPlans.Select(p => new StudentPlanEntity
                {
                    Id = p.Id,
                    StudentId = SelectedStudent.Id,
                    CourseId = p.CourseId,
                    Priority = p.Priority,
                    IsRepeat = p.IsRepeat,
                    TermKey = AcademicTermKeyService.NormalizeForStorage(p.TermKey),
                    CreatedUtc = p.CreatedUtc == default ? DateTime.UtcNow : p.CreatedUtc
                }).ToList();

                await _sqlite.SaveStudentPlansForStudentAsync(SelectedStudent.Id, rows);
                await ReloadSelectedStudentPlansAsync();
                await _sqlite.TryLoadStudentsAsync(_main.Store, scopeDepartmentId: null);
                await _sqlite.TryLoadStudentPlansAsync(_main.Store, scopeDepartmentId: null);

                MessageBox.Show("The student's study plan was saved successfully.", "Data Management",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Unable to save the student's study plan.\n\n{ex.Message}", "Data Management",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task ImportStudentsCsvAsync()
        {
            try
            {
                var ofd = new OpenFileDialog
                {
                    Title = "Import Students and Study Plans",
                    Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*"
                };

                if (ofd.ShowDialog() != true)
                    return;

                var build = StudentCsvImportService.ParseAndResolve(ofd.FileName, Departments, Courses);
                if (build.Errors.Count > 0)
                {
                    MessageBox.Show(
                        "The import file could not be processed.\n\n" + string.Join("\n", build.Errors),
                        "Student Import",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                if (build.Items.Count == 0)
                {
                    var details = build.Warnings.Count > 0 ? "\n\n" + string.Join("\n", build.Warnings.Take(12)) : string.Empty;
                    MessageBox.Show(
                        "No valid student records were found in the selected file." + details,
                        "Student Import",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                var confirm = MessageBox.Show(
                    $"Students prepared: {build.Items.Count}\n" +
                    $"Study-plan rows prepared: {build.PlanRowsPrepared}\n" +
                    $"Rows skipped: {build.RowsSkipped}\n\n" +
                    "Smart registration rules were applied during import: department alignment, General Studies allowance, level ceiling, and duplicate merge.\n\n" +
                    "Continue and merge these records into the database? Existing study plans will only be replaced for imported students who have course rows in this file.",
                    "Student Import",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (confirm != MessageBoxResult.Yes)
                    return;

                var saveReport = await _sqlite.ImportStudentsAndPlansAsync(build.Items);

                await ReloadAsync();
                await _sqlite.TryLoadStudentsAsync(_main.Store, scopeDepartmentId: null);
                await _sqlite.TryLoadStudentPlansAsync(_main.Store, scopeDepartmentId: null);
                await _main.ReloadCoreFromDatabaseAsync();

                var lines = new System.Collections.Generic.List<string>
                {
                    $"Students created: {saveReport.StudentsCreated}",
                    $"Students updated: {saveReport.StudentsUpdated}",
                    $"Study-plan rows imported: {saveReport.PlansInserted}",
                    $"Existing study-plan rows replaced: {saveReport.PlansReplaced}",
                    $"Rows skipped during parsing: {build.RowsSkipped}"
                };

                var warnings = build.Warnings
                    .Concat(saveReport.Warnings)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(12)
                    .ToList();

                if (warnings.Count > 0)
                {
                    lines.Add(string.Empty);
                    lines.Add("Notes:");
                    lines.AddRange(warnings);
                    if (build.Warnings.Count + saveReport.Warnings.Count > warnings.Count)
                        lines.Add("...");
                }

                MessageBox.Show(string.Join("\n", lines), "Student Import",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Unable to import the student file.\n\n{ex.Message}", "Student Import",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SaveStudentImportTemplate()
        {
            try
            {
                var sfd = new SaveFileDialog
                {
                    Title = "Save Student Import Template",
                    Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                    FileName = "student_import_template.csv"
                };

                if (sfd.ShowDialog() != true)
                    return;

                var lines = new[]
                {
                    "Student Number,Student Name,Department,Academic Level,Course Code,Course Name,Priority,Repeat,Term Key",
                    "2026001,Ahmed Ali,Computer Science,3,CS301,Operating Systems,0,No,First Term 2026/2027",
                    "2026001,Ahmed Ali,Computer Science,3,CS315,Database Systems,1,No,First Term 2026/2027",
                    "2026002,Sara Khalid,Information Systems,2,IS220,System Analysis,0,Yes,First Term 2026/2027",
                    "2026003,Lina Omar,General Studies,1,,,,,First Term 2026/2027"
                };

                File.WriteAllLines(sfd.FileName, lines);

                MessageBox.Show(
                    "The CSV template was created successfully.\n\nYou can open it in Excel, complete the rows, then import it back into the application.",
                    "Student Import Template",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Unable to create the student import template.\n\n{ex.Message}", "Student Import Template",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        private StudentRegistrationValidationResult GetSelectedStudentRegistrationValidation()
        {
            if (SelectedStudent is null)
            {
                var empty = new StudentRegistrationValidationResult();
                empty.Errors.Add("Select a student before validating academic registration.");
                return empty;
            }

            return StudentAcademicRegistrationService.ValidatePlan(
                SelectedStudent,
                SelectedStudentPlans.Select(p => new StudentAcademicPlanInput(p.CourseId, p.Priority, p.IsRepeat, p.TermKey)),
                Courses,
                Departments);
        }

        private void ValidateSelectedStudentRegistration(bool showDialog)
        {
            var validation = GetSelectedStudentRegistrationValidation();
            RefreshSelectedStudentRegistrationSummary(validation);

            if (!showDialog)
                return;

            var lines = new System.Collections.Generic.List<string>
            {
                $"Registered courses: {validation.RegisteredCourseCount}",
                $"Weekly contact hours: {validation.TotalWeeklyHours}",
                $"Distinct term keys: {validation.DistinctTermCount}"
            };

            if (validation.Errors.Count > 0)
            {
                lines.Add(string.Empty);
                lines.Add("Blocking issues:");
                lines.AddRange(validation.Errors.Distinct(StringComparer.OrdinalIgnoreCase).Take(12));
            }
            else
            {
                lines.Add(string.Empty);
                lines.Add("Required checks passed.");
            }

            if (validation.Warnings.Count > 0)
            {
                lines.Add(string.Empty);
                lines.Add("Notes:");
                lines.AddRange(validation.Warnings.Distinct(StringComparer.OrdinalIgnoreCase).Take(12));
            }

            MessageBox.Show(
                string.Join("\n", lines),
                "Smart Academic Registration",
                MessageBoxButton.OK,
                validation.Errors.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
        }

        private void RefreshSelectedStudentRegistrationSummary(StudentRegistrationValidationResult? validation = null)
        {
            if (SelectedStudent is null)
            {
                SelectedStudentRegistrationSummary = "Select a student to review smart academic registration rules. Eligible courses will be limited to the student's department and General Studies courses.";
                return;
            }

            validation ??= GetSelectedStudentRegistrationValidation();
            var status = validation.Errors.Count > 0
                ? $"Blocking issues: {validation.Errors.Count}."
                : validation.Warnings.Count > 0
                    ? $"Review notes: {validation.Warnings.Count}."
                    : "Ready to save.";

            var firstDetail = validation.Errors.FirstOrDefault()
                              ?? validation.Warnings.FirstOrDefault()
                              ?? "Registration rules applied: department alignment, General Studies allowance, level ceiling, and duplicate prevention.";

            SelectedStudentRegistrationSummary =
                $"Current plan: {validation.RegisteredCourseCount} course(s), {validation.TotalWeeklyHours} weekly contact hour(s). {status} " +
                $"Eligible course options are filtered to the student's department and General Studies courses up to the student's current academic level. {firstDetail}";
        }

        private int GetStudentPlanCourseSortRank(CourseEntity course)
        {
            if (SelectedStudent?.Level is not int level || level <= 0)
                return 0;

            if (course.Level == level)
                return 0;
            if (course.Level > 0 && course.Level < level)
                return 1;
            return 2;
        }

        private void ClearCourseFilters()
        {
            SelectedDepartmentBase = AllOption;
            SelectedSpecialty = AllOption;
            SelectedLevelOption = AllOption;
        }

		private void RebuildFilteredCourseOptions()
		{
			FilteredCourseOptions.Clear();

			var allowedDeptIds = GetAllowedDepartmentIdsForFilter();
			var lvl = GetSelectedLevelValue();

			IEnumerable<CourseEntity> query = Courses;
			if (allowedDeptIds is not null)
				query = query.Where(c => allowedDeptIds.Contains(c.DepartmentId));
			if (lvl.HasValue)
				query = query.Where(c => c.Level == lvl.Value);

			foreach (var c in query.OrderBy(c => c.Name))
				FilteredCourseOptions.Add(c);
		}

        private int? GetSelectedLevelValue()
        {
            if (string.IsNullOrWhiteSpace(SelectedLevelOption) || SelectedLevelOption == AllOption)
                return null;
            return int.TryParse(SelectedLevelOption, out var v) ? v : null;
        }

        private HashSet<int>? GetAllowedDepartmentIdsForFilter()
        {
            if (string.IsNullOrWhiteSpace(SelectedDepartmentBase) || SelectedDepartmentBase == AllOption)
                return null;

            var baseName = SelectedDepartmentBase.Trim();
            var specialty = (SelectedSpecialty ?? AllOption).Trim();

            var set = new HashSet<int>();
            foreach (var d in Departments)
            {
                var split = SplitDepartmentName(d.Name ?? string.Empty);
                if (!string.Equals(split.Base, baseName, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (specialty != AllOption && !string.Equals(split.Specialty, specialty, StringComparison.OrdinalIgnoreCase))
                    continue;

                set.Add(d.Id);
            }

            return set.Count == 0 ? null : set;
        }

        private static (string Base, string Specialty) SplitDepartmentName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return (string.Empty, string.Empty);

            var normalized = name.Replace('–', '-');
            var idx = normalized.IndexOf('-');
            if (idx < 0)
                return (name.Trim(), string.Empty);

            var left = normalized.Substring(0, idx).Trim();
            var right = normalized.Substring(idx + 1).Trim();
            return (left, right);
        }

        private void RebuildDepartmentBaseOptions()
        {
            DepartmentBaseOptions.Clear();
            DepartmentBaseOptions.Add(AllOption);

            var bases = Departments
                .Select(d => SplitDepartmentName(d.Name ?? string.Empty).Base)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct()
                .OrderBy(x => x)
                .ToList();

            foreach (var b in bases)
                DepartmentBaseOptions.Add(b);

            if (!DepartmentBaseOptions.Contains(_selectedDepartmentBase))
            {
                _selectedDepartmentBase = AllOption;
                OnPropertyChanged(nameof(SelectedDepartmentBase));
                OnPropertyChanged(nameof(IsSpecialtyEnabled));
            }
        }

        private void RebuildSpecialtyOptions()
        {
            SpecialtyOptions.Clear();
            SpecialtyOptions.Add(AllOption);

            IEnumerable<string> specs;
            if (SelectedDepartmentBase == AllOption)
            {
                specs = Departments
                    .Select(d => SplitDepartmentName(d.Name ?? string.Empty).Specialty)
                    .Where(x => !string.IsNullOrWhiteSpace(x));
            }
            else
            {
                specs = Departments
                    .Where(d => string.Equals(SplitDepartmentName(d.Name ?? string.Empty).Base, SelectedDepartmentBase, StringComparison.OrdinalIgnoreCase))
                    .Select(d => SplitDepartmentName(d.Name ?? string.Empty).Specialty)
                    .Where(x => !string.IsNullOrWhiteSpace(x));
            }

            foreach (var s in specs.Distinct().OrderBy(x => x))
                SpecialtyOptions.Add(s);

            if (!SpecialtyOptions.Contains(_selectedSpecialty))
            {
                _selectedSpecialty = AllOption;
                OnPropertyChanged(nameof(SelectedSpecialty));
            }
        }

        private void RebuildLevelOptions()
        {
            LevelOptions.Clear();
            LevelOptions.Add(AllOption);

            IEnumerable<CourseEntity> src = Courses;
            var allowedDeptIds = GetAllowedDepartmentIdsForFilter();
            if (allowedDeptIds is not null)
                src = src.Where(c => allowedDeptIds.Contains(c.DepartmentId));

            foreach (var lvl in src.Select(c => c.Level).Distinct().OrderBy(x => x))
                LevelOptions.Add(lvl.ToString());

            if (!LevelOptions.Contains(_selectedLevelOption))
            {
                _selectedLevelOption = AllOption;
                OnPropertyChanged(nameof(SelectedLevelOption));
            }
        }

        private int NextId<T>(ObservableCollection<T> list, Func<T, int> idSelector)
            => list.Count == 0 ? 1 : (list.Max(idSelector) + 1);

        private void AddDepartment()
        {
            Departments.Add(new DepartmentEntity
            {
                Id = NextId(Departments, x => x.Id),
                Name = "New Department"
            });
        }

        private void DeleteDepartment()
        {
            if (SelectedDepartment is null) return;
            var res = MessageBox.Show("Do you want to delete the selected department?", "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (res != MessageBoxResult.Yes) return;

            // Also remove dependent rows in-memory (user can adjust before saving).
            var deptId = SelectedDepartment.Id;
            Departments.Remove(SelectedDepartment);

            var facToRemove = Faculties.Where(x => x.DepartmentId == deptId).ToList();
            foreach (var f in facToRemove) Faculties.Remove(f);

            var courseToRemove = Courses.Where(x => x.DepartmentId == deptId).ToList();
            foreach (var c in courseToRemove) Courses.Remove(c);
        }

        private async Task SaveDepartmentsAsync()
        {
            if (Departments.Any(d => string.IsNullOrWhiteSpace(d.Name)))
            {
                MessageBox.Show("There is a department with an empty name. Edit the name before saving.", "Notice",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                await _sqlite.SaveDepartmentsAsync(Departments);
                await _main.ReloadCoreFromDatabaseAsync();
                MessageBox.Show("Departments saved successfully.", "Data Management",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Unable to save departments.\n\n{ex.Message}", "Data Management",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AddFaculty()
        {
            if (Departments.Count == 0)
            {
                MessageBox.Show("Add a department first.", "Data Management", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var defaultDeptId = Departments.First().Id;

            Faculties.Add(new FacultyEntity
            {
                Id = NextId(Faculties, x => x.Id),
                Name = "New Faculty Member",
                DepartmentId = defaultDeptId,
                IsGeneralStudies = false
            });
        }

        private void DeleteFaculty()
        {
            if (SelectedFaculty is null) return;
            var res = MessageBox.Show("Do you want to delete the selected faculty member?", "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (res != MessageBoxResult.Yes) return;
            Faculties.Remove(SelectedFaculty);
        }

        private async Task SaveFacultiesAsync()
        {
            if (Faculties.Any(f => string.IsNullOrWhiteSpace(f.Name)))
            {
                MessageBox.Show("There is a faculty member with an empty name. Edit the name before saving.", "Notice",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (Faculties.Any(f => !Departments.Any(d => d.Id == f.DepartmentId)))
            {
                MessageBox.Show("There is a faculty member linked to a department that does not exist. Edit the department before saving.", "Notice",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                await _sqlite.SaveFacultiesAsync(Faculties);
                await _main.ReloadCoreFromDatabaseAsync();
                MessageBox.Show("Faculty members saved successfully.", "Data Management",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Unable to save faculty members.\n\n{ex.Message}", "Data Management",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AddCourse()
        {
            if (Departments.Count == 0)
            {
                MessageBox.Show("Add a department first.", "Data Management", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var defaultDeptId = Departments.First().Id;

            Courses.Add(new CourseEntity
            {
                Id = NextId(Courses, x => x.Id),
                Name = "New Course",
                DepartmentId = defaultDeptId,
                Level = 1,
                HoursPerWeek = 4,
                IsGeneralCourse = false
            });
        }

        private void DeleteCourse()
        {
            if (SelectedCourse is null) return;
            var res = MessageBox.Show("Do you want to delete the selected course?", "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (res != MessageBoxResult.Yes) return;
            Courses.Remove(SelectedCourse);
        }

        private async Task SaveCoursesAsync()
        {
            if (Courses.Any(c => string.IsNullOrWhiteSpace(c.Name)))
            {
                MessageBox.Show("There is a course with an empty name. Edit the name before saving.", "Notice",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (Courses.Any(c => !Departments.Any(d => d.Id == c.DepartmentId)))
            {
                MessageBox.Show("There is a course linked to a department that does not exist. Edit the department before saving.", "Notice",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (Courses.Any(c => c.Level <= 0))
            {
                MessageBox.Show("There is a course with an invalid level. Set the level to 1 or higher.", "Notice",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (Courses.Any(c => c.HoursPerWeek < 0))
            {
                MessageBox.Show("There is a course with invalid hours per week. Set the value to 0 or higher.", "Notice",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                await _sqlite.SaveCoursesAsync(Courses);
                await _main.ReloadCoreFromDatabaseAsync();
                MessageBox.Show("Courses saved successfully.", "Data Management",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Unable to save courses.\n\n{ex.Message}", "Data Management",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        private void AddRoom()
        {
            Rooms.Add(new RoomEntity
            {
                Id = NextId(Rooms, x => x.Id),
                Name = "New Room",
                DepartmentId = SelectedDepartment?.Id ?? Departments.FirstOrDefault()?.Id
            });
        }

        private void DeleteRoom()
        {
            if (SelectedRoom is null) return;
            var res = MessageBox.Show("Do you want to delete the selected room?", "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (res != MessageBoxResult.Yes) return;

            Rooms.Remove(SelectedRoom);
        }

        private async Task SaveRoomsAsync()
        {
            try
            {
                await _sqlite.SaveRoomsAsync(Rooms);
                await ReloadAsync();
                MessageBox.Show("Rooms saved successfully.", "Data Management", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Unable to save rooms.\n\n{ex.Message}", "Data Management",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // ------------------------------
        // ------------------------------
        private void DeleteCourseCodeMap()
        {
            if (SelectedCourseCodeMap is null) return;
            var res = MessageBox.Show("Do you want to delete the selected mapping row?", "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (res != MessageBoxResult.Yes) return;
            CourseCodeMaps.Remove(SelectedCourseCodeMap);
            SelectedCourseCodeMap = null;
        }

        private void ClearCourseCodeMapCourse()
        {
            if (SelectedCourseCodeMap is null) return;
            SelectedCourseCodeMap.CourseId = null;
        }

        private async Task SaveCourseCodeMapsAsync()
        {
            // Basic validation
            foreach (var m in CourseCodeMaps)
            {
                m.CourseCode = (m.CourseCode ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(m.CourseCode))
                {
                    MessageBox.Show("There is a mapping row without a course code. Edit the code before saving.", "Notice",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
            }

            var dup = CourseCodeMaps
                .GroupBy(x => x.CourseCode, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(g => g.Count() > 1);
            if (dup is not null)
            {
                MessageBox.Show($"A duplicate course code was found: {dup.Key}\n\nDelete the duplicate and try again.", "Notice",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // auto-fill it so the code becomes the stable master identifier inside the program.
            var filledCourseCodes = 0;
            foreach (var m in CourseCodeMaps)
            {
                if (m.CourseId is null || m.CourseId <= 0) continue;
                var course = Courses.FirstOrDefault(c => c.Id == m.CourseId.Value);
                if (course is null) continue;
                if (string.IsNullOrWhiteSpace(course.CourseCode))
                {
                    course.CourseCode = m.CourseCode;
                    filledCourseCodes++;
                }
            }

            try
            {
                if (filledCourseCodes > 0)
                    await _sqlite.SaveCoursesAsync(Courses);

                await _sqlite.SaveCourseCodeMapsAsync(CourseCodeMaps, deleteMissing: true);

                if (filledCourseCodes > 0)
                {
                    MessageBox.Show($"Course code mappings saved successfully.\n\nThe course code field was also filled for {filledCourseCodes} course records that previously had no code.",
                        "Data Management", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("Course code mappings saved successfully.", "Data Management", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Unable to save course code mappings.\n\n{ex.Message}", "Data Management",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task DiagnoseLegacyAsync()
{
    try
    {
        var report = await _sqlite.DiagnoseLegacyAsync(scopeDepartmentId: null);
        LegacyMaintenanceReport = report.ToHumanText();
        MessageBox.Show("Diagnosis completed. Review the report in the Maintenance tab.", "Data Management",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }
    catch (Exception ex)
    {
        MessageBox.Show($"Unable to run the diagnosis.\n\n{ex.Message}", "Data Management",
            MessageBoxButton.OK, MessageBoxImage.Error);
    }
}

private async Task RunLegacyMaintenanceAsync()
{
    var res = MessageBox.Show(
        "Legacy data maintenance will repair records and remove unrecoverable entries, which may affect saved results.\n\nDo you want to continue?",
        "Confirmation",
        MessageBoxButton.YesNo,
        MessageBoxImage.Question);

    if (res != MessageBoxResult.Yes) return;

    try
    {
        var (ok, err, report) = await _sqlite.RunLegacyMaintenanceAsync(scopeDepartmentId: null, applyFix: true);
        if (!ok)
        {
            MessageBox.Show($"Maintenance failed.\n\n{err}", "Data Management",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        LegacyMaintenanceReport = report.ToHumanText();

        // Refresh lookups in the running app (helps after repairs that change dept mapping).
        await _main.ReloadCoreFromDatabaseAsync();

        MessageBox.Show("Maintenance completed. Review the report in the Maintenance tab.\n\nNote: the Revision value was updated so client devices refresh automatically.", "Data Management",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }
    catch (Exception ex)
    {
        MessageBox.Show($"Unable to run maintenance.\n\n{ex.Message}", "Data Management",
            MessageBoxButton.OK, MessageBoxImage.Error);
    }
}

private void CopyLegacyReport()
{
    try
    {
        if (string.IsNullOrWhiteSpace(LegacyMaintenanceReport))
        {
            MessageBox.Show("There is no report to copy.", "Data Management",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Clipboard.SetText(LegacyMaintenanceReport);
        MessageBox.Show("The report has been copied.", "Data Management",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }
    catch (Exception ex)
    {
        MessageBox.Show($"Unable to copy the report.\n\n{ex.Message}", "Data Management",
            MessageBoxButton.OK, MessageBoxImage.Error);
    }
}

// ------------------------------
// Backup / Restore (SQLite file)
// ------------------------------

private void BackupDatabase()
{
    try
    {
        var dbPath = DbPath;
        if (string.IsNullOrWhiteSpace(dbPath) || !File.Exists(dbPath))
        {
            MessageBox.Show("The database file was not found.", "Backup",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dbDir = Path.GetDirectoryName(dbPath) ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var backupsDir = Path.Combine(dbDir, "Backups");
        Directory.CreateDirectory(backupsDir);

        var suggestedName = $"TrainerScheduler_{DateTime.Now:yyyyMMdd_HHmmss}.db";

        var sfd = new SaveFileDialog
        {
            Title = "Save Backup As",
            Filter = "SQLite DB (*.db)|*.db|Backup (*.bak)|*.bak|All files (*.*)|*.*",
            FileName = suggestedName,
            InitialDirectory = backupsDir
        };

        if (sfd.ShowDialog() != true) return;

        File.Copy(dbPath, sfd.FileName, overwrite: true);
        MessageBox.Show($"Backup created successfully.\n\n{sfd.FileName}", "Backup",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }
    catch (Exception ex)
    {
        MessageBox.Show($"Unable to create the backup.\n\n{ex.Message}", "Backup",
            MessageBoxButton.OK, MessageBoxImage.Error);
    }
}

private void RestoreDatabase()
{
    try
    {
        var ofd = new OpenFileDialog
        {
            Title = "Choose Backup File",
            Filter = "SQLite DB (*.db;*.bak)|*.db;*.bak|All files (*.*)|*.*"
        };

        if (ofd.ShowDialog() != true) return;

        var dbPath = DbPath;
        if (string.IsNullOrWhiteSpace(dbPath))
        {
            MessageBox.Show("Unable to determine the database path.", "Restore",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var confirm = MessageBox.Show(
            "The application will close to apply the restore on the next launch. Do you want to continue?",
            "Restore",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        var stagedRestore = dbPath + ".restore.db";
        var pendingFlag = dbPath + ".restore.pending";

        File.Copy(ofd.FileName, stagedRestore, overwrite: true);
        File.WriteAllText(pendingFlag, DateTime.Now.ToString("O"));

        MessageBox.Show("The restore package is ready. The application will now close and apply it on the next launch.", "Restore",
            MessageBoxButton.OK, MessageBoxImage.Information);

        Application.Current.Shutdown();
    }
    catch (Exception ex)
    {
        MessageBox.Show($"Unable to complete the restore.\n\n{ex.Message}", "Restore",
            MessageBoxButton.OK, MessageBoxImage.Error);
    }
}

private void OpenDataFolder()
{
    try
    {
        var dbPath = DbPath;
        if (string.IsNullOrWhiteSpace(dbPath)) return;

        var dir = Path.GetDirectoryName(dbPath);
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return;

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = dir,
            UseShellExecute = true
        });
    }
    catch (Exception ex)
    {
        MessageBox.Show($"Unable to open the data folder.\n\n{ex.Message}", "Data Folder",
            MessageBoxButton.OK, MessageBoxImage.Error);
    }
}


    }
}
