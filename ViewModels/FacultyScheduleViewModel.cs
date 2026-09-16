using MiniTrainerScheduler.Models;
using MiniTrainerScheduler.Services;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using MiniTrainerScheduler.ViewModels.Adapters;
using System.Collections.Generic;
using System.Windows.Input;
using System.Windows;
using System.Threading.Tasks;
using TrainerScheduler.Services;
using TrainerScheduler.Network;
using TrainerScheduler.Data.Entities;

namespace MiniTrainerScheduler.ViewModels
{
    public sealed class FacultyScheduleViewModel : INotifyPropertyChanged
    {
        private readonly SqliteStoreService _sqlite = new SqliteStoreService();
        private bool _isAutoSaving;
        private bool _autoSavePending;

        public FacultyScheduleViewModel(DataStore store, int? scopeDepartmentId = null)
        {
            Store = store ?? throw new ArgumentNullException(nameof(store));

            ScopeDepartmentId = scopeDepartmentId;

            IEnumerable<Faculty> facultiesQuery = Store.Faculties;
            if (ScopeDepartmentId is int scopeDeptId1)
                facultiesQuery = facultiesQuery.Where(f => f.DepartmentId == scopeDeptId1);

            foreach (var f in facultiesQuery.OrderBy(f => f.Name))
                Faculties.Add(f);

            IEnumerable<Department> departmentsQuery = Store.Departments;
            if (ScopeDepartmentId is int scopeDeptId2)
                departmentsQuery = departmentsQuery.Where(d => d.Id == scopeDeptId2);

            foreach (var d in departmentsQuery.OrderBy(d => d.Name))
                Departments.Add(d);

            foreach (var s in Store.Slots.OrderBy(s => s.Day).ThenBy(s => s.Start))
                Slots.Add(s);

            Table.Clear();
            Table.Add(new DaySlots { Day = "Sunday" });
            Table.Add(new DaySlots { Day = "Monday" });
            Table.Add(new DaySlots { Day = "Tuesday" });
            Table.Add(new DaySlots { Day = "Wednesday" });
            Table.Add(new DaySlots { Day = "Thursday" });

            AddMeetingCommand = new RelayCommand(_ => AddMeeting());
            AddBlockCommand = new RelayCommand(_ => AddBlock());
            DeleteRowCommand = new RelayCommand(row => DeleteRow(row));

            SelectedFaculty = Faculties.FirstOrDefault();
            SelectedDepartment = Departments.FirstOrDefault();

            if (ScopeDepartmentId is int scopeDeptId3 && SelectedDepartment is null)
            {
                MessageBox.Show(
                    "The faculty timetable cannot be opened because the supervisor's department is not available in the current data.\n\n" +
                    "Please add the departments to the database first.",
                    "Notice",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            RefreshRows();
            BuildGeneralRows();
        }

        /// <summary>
        /// Auto-save (silent) after any manual edit inside "Faculty Timetable".
        /// This fixes the issue where manual edits weren't visible to other accounts/devices
        /// because they were only kept in memory.
        /// </summary>
        public void RequestAutoSave()
        {
            _ = AutoSaveInternalAsync();
        }

        /// <summary>
        /// Reload the persisted scheduled results (Assignments + FacultySlotBlocks) from SQLite.
        /// This helps when another user/account edits schedules and you need to refresh without restarting.
        /// </summary>
        public async Task ReloadFromDatabaseAsync()
        {
            try
            {
                var scopeDeptId = ScopeDepartmentId;

                var lanSettings = LanSettingsStore.LoadOrDefault();
                if (lanSettings.Mode == LanMode.Client && LanClientContext.IsAuthenticated)
                {
                    var any = await LanDataClient.TryPullAssignmentsAndBlocksIntoStoreAsync(Store);
                    if (!any)
                    {
                        // Avoid keeping stale results on screen.
                        Store.Assignments.Clear();
                        Store.FacultySlotBlocks.Clear();
                    }

                    RefreshRows();
                    BuildGeneralRows();
                    return;
                }

                // Load Assignments for scope (admin = all). If none, clear to avoid keeping stale in-memory results.
                var loadedAssign = await _sqlite.TryLoadAssignmentsAsync(Store, scopeDeptId);
                if (!loadedAssign)
                    Store.Assignments.Clear();

                // Load blocks (then trim for supervisor scope so he doesn't carry irrelevant blocks in memory).
                var loadedBlocks = await _sqlite.TryLoadFacultySlotBlocksAsync(Store);
                if (!loadedBlocks)
                    Store.FacultySlotBlocks.Clear();

                if (scopeDeptId is not null)
                {
                    var allowedFacultyIds = Store.Faculties
                        .Where(f => f.DepartmentId == scopeDeptId.Value)
                        .Select(f => f.Id)
                        .ToHashSet();

                    Store.FacultySlotBlocks.RemoveAll(b => !allowedFacultyIds.Contains(b.FacultyId));
                }

                RefreshRows();
                BuildGeneralRows();
            }
            catch
            {
                // Silent: Refresh button should not crash the UI.
            }
        }

        private async Task AutoSaveInternalAsync()
        {
            if (_isAutoSaving)
            {
                _autoSavePending = true;
                return;
            }

            _isAutoSaving = true;

            try
            {
                while (true)
                {
                    _autoSavePending = false;

                    var lanSettings = LanSettingsStore.LoadOrDefault();
                    if (lanSettings.Mode == LanMode.Client && LanClientContext.IsAuthenticated)
                    {
                        // Push both assignments + blocks (safe even if unchanged).
                        var rowsA = Store.Assignments.Select(a => new AssignmentEntity
                        {
                            DepartmentId = a.DepartmentId,
                            CourseId = a.CourseId,
                            SectionIndex = a.SectionIndex,
                            SlotId = a.SlotId,
                            FacultyId = a.FacultyId,
                            RoomId = a.RoomId,
                            Status = a.Status ?? string.Empty,
                            Kind = a.Kind ?? string.Empty,
                            SavedUtc = DateTime.UtcNow
                        }).ToList();

                        var rowsB = Store.FacultySlotBlocks.Select(b => new FacultySlotBlockEntity
                        {
                            FacultyId = b.FacultyId,
                            SlotId = b.SlotId,
                            Reason = b.Reason,
                            SavedUtc = DateTime.UtcNow
                        }).ToList();

                        // Allow blocks-only updates even if assignments are empty.
                        if (rowsA.Count > 0)
                        {
                            var (okA, errA) = await LanDataClient.PushAssignmentsAsync(rowsA);
                            if (!okA) throw new Exception(errA);
                        }

                        // IMPORTANT: push blocks even when empty.
                        // Empty list means "clear blocks in scope" on the server.
                        var (okB, errB) = await LanDataClient.PushBlocksAsync(rowsB);
                        if (!okB) throw new Exception(errB);
                    }
                    else
                    {
                        // Save assignments (may be empty - we ignore the message)
                        var (okA, _) = await _sqlite.SaveAssignmentsAsync(Store, ScopeDepartmentId);

                        var (okB, _) = await _sqlite.SaveFacultySlotBlocksAsync(Store, ScopeDepartmentId);

                        try
                        {
                            if ((okA || okB) && lanSettings.Mode == LanMode.Server && LanServerManager.IsRunning)
                                await _sqlite.BumpServerRevisionAsync();
                        }
                        catch
                        {
                            // Never crash auto-save due to revision bump.
                        }
                    }

                    if (!_autoSavePending)
                        break;
                }
            }
            catch (Exception ex)
            {
				UiError.Show(
					"Faculty Timetable",
					"The faculty timetable changes could not be saved automatically.",
					ex);
            }
            finally
            {
                _isAutoSaving = false;
            }
        }

        /// <summary>
        /// When not null, this ViewModel is scoped to a single department (Supervisor mode).
        /// Used to filter which trainers are visible and to restrict destructive edits.
        /// </summary>
        public int? ScopeDepartmentId { get; }

        public bool IsDepartmentSelectionEnabled => ScopeDepartmentId is null;

        public DataStore Store { get; }

        public ObservableCollection<TimeRowModel> RowsGeneral { get; } = new();

        private RowsProxy? _generalProxy;
        public RowsProxy GeneralProxy => _generalProxy ??= new RowsProxy(RowsGeneral);

        public sealed class RowsProxy
        {
            public ObservableCollection<TimeRowModel> Rows { get; }
            public RowsProxy(ObservableCollection<TimeRowModel> rows) { Rows = rows; }
        }

        public ObservableCollection<Faculty> Faculties { get; } = new();
        public ObservableCollection<FacultyRow> Rows { get; } = new();

        public ObservableCollection<Department> Departments { get; } = new();
        public ObservableCollection<int> Levels { get; } = new();

        public ObservableCollection<Course> Courses { get; } = new();
        public ObservableCollection<Slot> Slots { get; } = new();

        private bool _hasTheoryPart;
        public bool HasTheoryPart
        {
            get => _hasTheoryPart;
            private set
            {
                if (_hasTheoryPart == value) return;
                _hasTheoryPart = value;
                OnPropertyChanged();
            }
        }

        private bool _hasPracticalPart;
        public bool HasPracticalPart
        {
            get => _hasPracticalPart;
            private set
            {
                if (_hasPracticalPart == value) return;
                _hasPracticalPart = value;
                OnPropertyChanged();
            }
        }


        private Department? _selectedDepartment;
        public Department? SelectedDepartment
        {
            get => _selectedDepartment;
            set
            {
                if (_selectedDepartment == value) return;
                _selectedDepartment = value;
                OnPropertyChanged();
                RefreshLevels();
                RefreshCourses();
            }
        }

        private int? _selectedLevel;
        public int? SelectedLevel
        {
            get => _selectedLevel;
            set
            {
                if (_selectedLevel == value) return;
                _selectedLevel = value;
                OnPropertyChanged();
                RefreshCourses();
            }
        }

        private Course? _selectedCourse;
        public Course? SelectedCourse
        {
            get => _selectedCourse;
            set
            {
                if (_selectedCourse == value) return;
                _selectedCourse = value;
                OnPropertyChanged();
                UpdateKindAvailability();
            }
        }

        private Slot? _selectedSlot;
        public Slot? SelectedSlot
        {
            get => _selectedSlot;
            set { _selectedSlot = value; OnPropertyChanged(); }
        }

        private int _selectedSectionIndex = 1;
        public int SelectedSectionIndex
        {
            get => _selectedSectionIndex;
            set
            {
                _selectedSectionIndex = value <= 0 ? 1 : value;
                OnPropertyChanged();
            }
        }

        private string _selectedKind = "THEORY";
        public string SelectedKind
        {
            get => _selectedKind;
            set
            {
                if (_selectedKind == value) return;
                _selectedKind = value;
                OnPropertyChanged();
            }
        }

        private void UpdateKindAvailability()
        {
            if (SelectedCourse is null)
            {
                HasTheoryPart = false;
                HasPracticalPart = false;
                return;
            }

            var (theoryHours, labHours, _) = SectionPlanner.GetHoursSplit(Store, SelectedCourse);
            HasTheoryPart = theoryHours > 0;
            HasPracticalPart = labHours > 0;

            if (SelectedKind == "THEORY" && !HasTheoryPart && HasPracticalPart)
                SelectedKind = "LAB";
            else if (SelectedKind == "LAB" && !HasPracticalPart && HasTheoryPart)
                SelectedKind = "THEORY";
            else if (!HasTheoryPart && !HasPracticalPart)
                SelectedKind = "THEORY";
        }

        private string? _blockReason;
        public string? BlockReason
        {
            get => _blockReason;
            set { _blockReason = value; OnPropertyChanged(); }
        }

        public ObservableCollection<DaySlots> Table { get; } = new();

        public sealed class DaySlots
        {
            public string Day { get; init; } = "";
            public string C1 { get; set; } = ""; // 8:00–9:40
            public string C2 { get; set; } = ""; // 10:00–11:40
            public string C3 { get; set; } = ""; // 12:00–1:40
            public string C4 { get; set; } = ""; // 2:00–3:40
        }

        public ICommand AddMeetingCommand { get; }
        public ICommand AddBlockCommand { get; }
        public ICommand DeleteRowCommand { get; }

        private string _footerDepartment = "";
        public string FooterDepartment
        {
            get => _footerDepartment;
            set { _footerDepartment = value; OnPropertyChanged(); }
        }

        private string _footerSpecialty = "";
        public string FooterSpecialty
        {
            get => _footerSpecialty;
            set { _footerSpecialty = value; OnPropertyChanged(); }
        }

        private Faculty? _selectedFaculty;
        public Faculty? SelectedFaculty
        {
            get => _selectedFaculty;
            set { _selectedFaculty = value; OnPropertyChanged(); RefreshRows(); BuildGeneralRows(); }
        }

        private int _facultyTotalHours;
        public int FacultyTotalHours
        {
            get => _facultyTotalHours;
            set { _facultyTotalHours = value; OnPropertyChanged(); }
        }

        public sealed class FacultyRow
        {
            public string Department { get; init; } = "";
            public string Course { get; init; } = "";
            public int Section { get; init; }
            public string Slot { get; init; } = "";
            public int Hours { get; init; }
            public string Room { get; init; } = "";
            public string Kind { get; init; } = "";
            public string Status { get; init; } = "";

            public Assignment? Assignment { get; init; }
            public FacultySlotBlock? Block { get; init; }
            public bool IsBlock => Block != null;
        }


        private void RefreshLevels()
        {
            Levels.Clear();

            if (SelectedDepartment is null)
            {
                SelectedLevel = null;
                return;
            }

            var levels = Store.Courses
                .Where(c => c.DepartmentId == SelectedDepartment.Id)
                .Select(c => c.Level)
                .Distinct()
                .OrderBy(l => l);

            foreach (var lvl in levels)
                Levels.Add(lvl);

            if (Levels.Count == 0)
            {
                SelectedLevel = null;
            }
            else if (!SelectedLevel.HasValue || !Levels.Contains(SelectedLevel.Value))
            {
                SelectedLevel = Levels.First();
            }
        }

        private void RefreshCourses()
        {
            Courses.Clear();

            IEnumerable<Course> query = Store.Courses;

            if (SelectedDepartment is not null)
                query = query.Where(c => c.DepartmentId == SelectedDepartment.Id);

            if (SelectedLevel is int lvl)
                query = query.Where(c => c.Level == lvl);

            var list = query
                .OrderBy(c => c.Level)
                .ThenBy(c => c.Name)
                .ToList();

            foreach (var c in list)
                Courses.Add(c);

            if (SelectedCourse is null || !Courses.Contains(SelectedCourse))
                SelectedCourse = Courses.FirstOrDefault();
}


        public void RefreshRows()
        {
            Rows.Clear();
            FacultyTotalHours = 0;

            foreach (var r in Table)
                r.C1 = r.C2 = r.C3 = r.C4 = "";
            OnPropertyChanged(nameof(Table));

            if (SelectedFaculty is null)
            {
                RowsGeneral.Clear();
                return;
            }

            var blockedSlots = (Store.FacultySlotBlocks ?? new List<FacultySlotBlock>())
                .Where(b => b.FacultyId == SelectedFaculty.Id)
                .Select(b => b.SlotId)
                .ToHashSet();

            var query = Store.Assignments
                .Where(a => a.FacultyId == SelectedFaculty.Id &&
                            !blockedSlots.Contains(a.SlotId))
                .ToList();


            var deptNames = query
                .Select(a => Store.Departments.FirstOrDefault(d => d.Id == a.DepartmentId)?.Name)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct()
                .ToArray();
            FooterDepartment = string.Join(", ", deptNames);

            foreach (var a in query)
            {
                var dept = Store.Departments.FirstOrDefault(d => d.Id == a.DepartmentId);
                var course = Store.Courses.FirstOrDefault(c => c.Id == a.CourseId);
                var slot = Store.Slots.FirstOrDefault(s => s.Id == a.SlotId);
                var room = a.RoomId is null ? null : Store.Rooms.FirstOrDefault(r => r.Id == a.RoomId);

                string kindArabic = AcademicEnglishText.KindLabel(a.Kind);

                var slotText = (slot is null || a.SlotId <= 0) ? "—" : AcademicEnglishText.Slot(slot);

                Rows.Add(new FacultyRow
                {
                    Department = dept?.Name ?? "—",
                    Course = course?.Name ?? "—",
                    Section = a.SectionIndex,
                    Slot = slotText,
                    Hours = (int)Math.Round(Convert.ToDouble(course?.HoursPerWeek ?? 0)),
                    Room = room?.Name ?? (a.RoomId?.ToString() ?? ""),
                    Kind = kindArabic,
                    Status = a.Status,
                    Assignment = a
                });

                if (slot is not null && a.SlotId > 0)
                {
                    var dayName = slotText.Split(' ').FirstOrDefault() ?? "";
                    int col = ColumnIndexFromTime(slotText);
                    int row = DayRowIndex(dayName);

                    if (row >= 0 && col > 0)
                    {
                        string courseName = course?.Name ?? "—";
                        string roomName = room?.Name ?? (a.RoomId?.ToString() ?? "");

                        string kindSuffix = AcademicEnglishText.KindSuffix(a.Kind);

                        string courseWithKind = string.IsNullOrWhiteSpace(kindSuffix)
                            ? courseName
                            : courseName + kindSuffix;

                        string cellText = string.IsNullOrWhiteSpace(roomName)
                            ? courseWithKind
                            : $"{courseWithKind}\nRoom: {roomName}";

                        var rowObj = Table[row];
                        switch (col)
                        {
                            case 1: rowObj.C1 = Append(rowObj.C1, cellText); break;
                            case 2: rowObj.C2 = Append(rowObj.C2, cellText); break;
                            case 3: rowObj.C3 = Append(rowObj.C3, cellText); break;
                            case 4: rowObj.C4 = Append(rowObj.C4, cellText); break;
                        }
                    }
                }
            }

            if (Store.FacultySlotBlocks != null && SelectedFaculty is not null)
            {
                var blocks = Store.FacultySlotBlocks
                    .Where(b => b.FacultyId == SelectedFaculty.Id)
                    .ToList();

                foreach (var b in blocks)
                {
                    var slot = Store.Slots.FirstOrDefault(s => s.Id == b.SlotId);
                    string slotTextBlock = slot is null ? "—" : AcademicEnglishText.Slot(slot);

                    const string blockLabel = "Faculty Release / Official Duty";

                    Rows.Add(new FacultyRow
                    {
                        Department = "—",
                        Course = blockLabel,
                        Section = 0,
                        Slot = slotTextBlock,
                        Hours = 0,
                        Room = "",
                        Kind = "",
                        Status = string.IsNullOrWhiteSpace(b.Reason) ? blockLabel : b.Reason!,
                        Block = b
                    });
                }

            }

            var courseHours = Store.Courses.ToDictionary(
                c => c.Id,
                c => (int)Math.Round(Convert.ToDouble(c.HoursPerWeek)));

            var distinctSections = query
                .Select(a => new { a.CourseId, a.SectionIndex })
                .Distinct();

            FacultyTotalHours = distinctSections.Sum(k =>
                courseHours.TryGetValue(k.CourseId, out var h) ? h : 0);

            OnPropertyChanged(nameof(Table));
            BuildGeneralRows();
        }

        // ===== Helpers =====
        private static string Append(string current, string add)
            => string.IsNullOrWhiteSpace(current) ? add : (current + Environment.NewLine + add);

        private static int DayRowIndex(string day)
            => day switch
            {
                "Sunday" => 0,
                "Monday" => 1,
                "Tuesday" => 2,
                "Wednesday" => 3,
                "Thursday" => 4,
                _ => -1
            };

        private static int ColumnIndexFromTime(string slotText)
        {
            var m = Regex.Match(slotText, @"(\d{2}):\d{2}");
            if (!m.Success) return 0;
            if (!int.TryParse(m.Groups[1].Value, out var h)) return 0;
            return h switch
            {
                8 => 1,
                10 => 2,
                12 => 3,
                14 => 4,
                _ => 0
            };
        }

        private static int DayIndex(DayOfWeek d) => d switch
        {
            DayOfWeek.Sunday => 0,
            DayOfWeek.Monday => 1,
            DayOfWeek.Tuesday => 2,
            DayOfWeek.Wednesday => 3,
            DayOfWeek.Thursday => 4,
            _ => -1
        };

        public void BuildGeneralRows()
        {
            RowsGeneral.Clear();
            if (Store is null || Store.Slots is null || SelectedFaculty is null)
                return;

            string Fmt(object t)
            {
                if (t is TimeSpan ts) return ts.ToString(@"hh\\:mm");
                if (t is TimeOnly to) return to.ToString("HH:mm");
                return t?.ToString() ?? "";
            }

            string Key(object s1, object s2) => $"{Fmt(s1)}-{Fmt(s2)}";

            var times = Store.Slots
                .Select(s => new { s.Start, s.End })
                .Distinct()
                .OrderBy(x => x.Start)
                .ToList();

            var indexByKey = new Dictionary<string, int>();
            for (int i = 0; i < times.Count; i++)
            {
                RowsGeneral.Add(new TimeRowModel
                {
                    Time = $"{Fmt(times[i].Start)} - {Fmt(times[i].End)}"
                });
                indexByKey[Key(times[i].Start, times[i].End)] = i;
            }

            var blockedSlots = (Store.FacultySlotBlocks ?? new List<FacultySlotBlock>())
                .Where(b => b.FacultyId == SelectedFaculty.Id)
                .Select(b => b.SlotId)
                .ToHashSet();

            var query = Store.Assignments
                .Where(a => a.FacultyId == SelectedFaculty.Id &&
                            !blockedSlots.Contains(a.SlotId));

            foreach (var a in query)
            {
                var slot = Store.Slots.FirstOrDefault(s => s.Id == a.SlotId);
                if (slot is null) continue;

                if (!indexByKey.TryGetValue(Key(slot.Start, slot.End), out var r)) continue;
                int c = DayIndex(slot.Day);
                if (c < 0 || c >= 5) continue;

                var course = Store.Courses.FirstOrDefault(x => x.Id == a.CourseId)?.Name ?? "—";
                var room = a.RoomId is null
                    ? ""
                    : (Store.Rooms.FirstOrDefault(rr => rr.Id == a.RoomId)?.Name ?? a.RoomId.Value.ToString());

                string kindSuffix = AcademicEnglishText.KindSuffix(a.Kind);

                string courseWithKind = string.IsNullOrWhiteSpace(kindSuffix)
                    ? course
                    : course + kindSuffix;

                string line = string.IsNullOrWhiteSpace(room)
                    ? courseWithKind
                    : $"{courseWithKind}\nRoom: {room}";

                RowsGeneral[r].Cells[c].Add(line);
            }

            if (Store.FacultySlotBlocks != null)
            {
                var blocks = Store.FacultySlotBlocks
                    .Where(b => b.FacultyId == SelectedFaculty.Id);

                foreach (var b in blocks)
                {
                    var slot = Store.Slots.FirstOrDefault(s => s.Id == b.SlotId);
                    if (slot is null) continue;

                    if (!indexByKey.TryGetValue(Key(slot.Start, slot.End), out var r)) continue;
                    int c = DayIndex(slot.Day);
                    if (c < 0 || c >= 5) continue;

                    const string blockLabel = "Faculty Release / Official Duty";

                    string line = string.IsNullOrWhiteSpace(b.Reason)
                        ? blockLabel
                        : $"{blockLabel}\n{b.Reason}";

                    RowsGeneral[r].Cells[c].Add(line);
                }
            }

        }


        private bool CanAddMeeting()
            => SelectedFaculty is not null
               && SelectedCourse is not null
               && SelectedSlot is not null
               && SelectedSectionIndex > 0;

        private void AddMeeting()
        {
            if (Store is null)
            {
                MessageBox.Show("The data store is not initialized.", "Notice",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (SelectedFaculty is null)
            {
                MessageBox.Show("Select a faculty member first.", "Notice",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (SelectedCourse is null)
            {
                MessageBox.Show("Select a course first.", "Notice",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (SelectedSlot is null)
            {
                MessageBox.Show("Select a time slot first.", "Notice",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (SelectedSectionIndex <= 0)
                SelectedSectionIndex = 1;

            bool alreadyExists = Store.Assignments.Any(x =>
                x.CourseId == SelectedCourse.Id &&
                x.SectionIndex == SelectedSectionIndex &&
                x.FacultyId == SelectedFaculty.Id &&
                x.SlotId == SelectedSlot.Id);

            if (alreadyExists)
            {
                MessageBox.Show("This class entry already exists for the same faculty member, course, section, and time slot.",
                    "Notice", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            bool facultyBusy = Store.Assignments.Any(x =>
                x.FacultyId == SelectedFaculty.Id &&
                x.SlotId == SelectedSlot.Id);

            if (facultyBusy)
            {
                MessageBox.Show("The faculty member already has another class in the same time slot (conflict).",
                    "Notice", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string kind = SelectedKind;
            if (string.IsNullOrWhiteSpace(kind))
                kind = "THEORY";

            var assignment = new Assignment(
                SelectedCourse.DepartmentId,
                SelectedCourse.Id,
                SelectedSectionIndex,
                SelectedSlot.Id,
                SelectedFaculty.Id,
                null,        // RoomId
                "MANUAL",    // Status
                kind
            );

            ManualPlanBridge.RegisterManualAssignment(Store, assignment);

            RefreshRows();
            BuildGeneralRows();

            RequestAutoSave();
        }

        private bool CanAddBlock()
            => SelectedFaculty is not null && SelectedSlot is not null;

        private void AddBlock()
        {
            if (SelectedFaculty is null)
            {
                MessageBox.Show("Select a faculty member first.", "Notice", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (SelectedSlot is null)
            {
                MessageBox.Show("Select a time slot first.", "Notice", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (Store.FacultySlotBlocks == null)
                Store.FacultySlotBlocks = new List<FacultySlotBlock>();

            var block = new FacultySlotBlock
            {
                FacultyId = SelectedFaculty.Id,
                SlotId = SelectedSlot.Id,
                Reason = BlockReason
            };

            Store.FacultySlotBlocks.Add(block);

            RefreshRows();
            BuildGeneralRows();

            RequestAutoSave();
        }

                private async void DeleteRow(object? parameter)
        {
            if (parameter is not FacultyRow row) return;

            var lanSettings = LanSettingsStore.LoadOrDefault();
            var isLanClient = lanSettings.Mode == LanMode.Client && LanClientContext.IsAuthenticated;

            if (row.Assignment != null)
            {
                var a = row.Assignment;

                // If user is scoped to a specific department, don't allow deleting outside that scope.
                if (ScopeDepartmentId is int scopedDeptId && a.DepartmentId != scopedDeptId)
                {
                    UiError.Show("Notice", "You do not have permission to delete items outside the assigned department.", new Exception("Forbidden"));
                    return;
                }

                if (isLanClient)
                {
                    var resp = await LanDataClient.DeleteAssignmentRowAsync(
                        a.DepartmentId,
                        a.CourseId,
                        a.SectionIndex,
                        a.SlotId,
                        a.FacultyId);

                    if (!resp.ok)
                    {
                        UiError.Show("Error", resp.errorMessage, new Exception(resp.errorMessage));
                        return;
                    }
                }
                else
                {
                    // Local delete (SQLite): signature is (departmentId, courseId, sectionIndex, slotId, facultyId, scopeDepartmentId)
                    var local = await _sqlite.DeleteAssignmentRowAsync(
                        a.DepartmentId,
                        a.CourseId,
                        a.SectionIndex,
                        a.SlotId,
                        a.FacultyId,
                        ScopeDepartmentId);

                    if (!local.ok)
                    {
                        UiError.Show("Error", local.errorMessage, new Exception(local.errorMessage));
                        return;
                    }
                }

                Store.Assignments.Remove(a);
            }

            if (row.Block != null && Store.FacultySlotBlocks != null)
            {
                var b = row.Block;

                if (isLanClient)
                {
                    var resp = await LanDataClient.DeleteBlockAsync(b.FacultyId, b.SlotId);
                    if (!resp.ok)
                    {
                    UiError.Show("Error", resp.errorMessage, new Exception(resp.errorMessage));
                        return;
                    }
                }
                else
                {
                    var local = await _sqlite.DeleteFacultySlotBlockAsync(
                        b.FacultyId,
                        b.SlotId,
                        ScopeDepartmentId);

                    if (!local.ok)
                    {
                        UiError.Show("Error", local.errorMessage, new Exception(local.errorMessage));
                        return;
                    }
                }

                Store.FacultySlotBlocks.Remove(b);
            }

            RefreshRows();
            BuildGeneralRows();
            RequestAutoSave();
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}
