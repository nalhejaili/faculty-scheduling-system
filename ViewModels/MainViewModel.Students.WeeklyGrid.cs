using MiniTrainerScheduler.Models;
using MiniTrainerScheduler.ViewModels.Adapters;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MiniTrainerScheduler.ViewModels
{
    /// <summary>
    /// Weekly Student view (weekly grid rows)
    /// - Builds TimeRowModel rows compatible with GeneralScheduleGrid.
    /// - Loads only the selected student's assignments (lazy) from SQLite.
    /// - Adds a small in-memory LRU cache for faster toggling between students.
    /// </summary>
    public sealed partial class MainViewModel
    {
        // ===== Weekly grid rows (GeneralScheduleGrid)
        public ObservableCollection<TimeRowModel> StudentWeeklyRowsGeneral { get; } = new();

        private RowsProxy? _studentWeeklyGeneralProxy;
        public RowsProxy StudentWeeklyGeneralProxy => _studentWeeklyGeneralProxy ??= new RowsProxy(StudentWeeklyRowsGeneral);

        public sealed class RowsProxy
        {
            public ObservableCollection<TimeRowModel> Rows { get; }
            public RowsProxy(ObservableCollection<TimeRowModel> rows) { Rows = rows; }
        }

        private bool _isStudentWeeklyScheduleBusy;
        public bool IsStudentWeeklyScheduleBusy
        {
            get => _isStudentWeeklyScheduleBusy;
            private set
            {
                if (_isStudentWeeklyScheduleBusy == value) return;
                _isStudentWeeklyScheduleBusy = value;
                OnPropertyChanged();
            }
        }

        private string _studentWeeklyScheduleStatusText = "";
        public string StudentWeeklyScheduleStatusText
        {
            get => _studentWeeklyScheduleStatusText;
            private set
            {
                if (string.Equals(_studentWeeklyScheduleStatusText, value, StringComparison.Ordinal)) return;
                _studentWeeklyScheduleStatusText = value;
                OnPropertyChanged();
            }
        }

        private CancellationTokenSource? _studentWeeklyScheduleCts;

        // ===== Schedule cache (LRU)
        private readonly Dictionary<int, ScheduleCacheEntry> _studentWeeklyScheduleCache = new();
        private readonly LinkedList<int> _studentWeeklyScheduleCacheOrder = new();
        private const int StudentWeeklyScheduleCacheMax = 20;

        private sealed class ScheduleCacheEntry
        {
            public List<StudentWeeklyAssignmentRow> Items { get; } = new();
            public DateTime LastUsedUtc { get; set; } = DateTime.UtcNow;
        }

        private void TouchScheduleCacheKey(int studentId)
        {
            if (_studentWeeklyScheduleCacheOrder.First?.Value == studentId) return;

            var node = _studentWeeklyScheduleCacheOrder.Find(studentId);
            if (node is not null)
            {
                _studentWeeklyScheduleCacheOrder.Remove(node);
                _studentWeeklyScheduleCacheOrder.AddFirst(node);
            }
            else
            {
                _studentWeeklyScheduleCacheOrder.AddFirst(studentId);
            }

            while (_studentWeeklyScheduleCacheOrder.Count > StudentWeeklyScheduleCacheMax)
            {
                var last = _studentWeeklyScheduleCacheOrder.Last!.Value;
                _studentWeeklyScheduleCacheOrder.RemoveLast();
                _studentWeeklyScheduleCache.Remove(last);
            }
        }

        private async Task ReloadStudentWeeklyScheduleAsync()
        {
            _studentWeeklyScheduleCts?.Cancel();
            var cts = new CancellationTokenSource();
            _studentWeeklyScheduleCts = cts;

            var studentId = SelectedStudentWeeklyStudentId;
            if (studentId <= 0)
            {
                StudentWeeklyRowsGeneral.Clear();
                StudentWeeklyScheduleStatusText = "Select a department, then a student, to view the weekly timetable.";
                return;
            }

            // Cache hit
            if (_studentWeeklyScheduleCache.TryGetValue(studentId, out var cached))
            {
                TouchScheduleCacheKey(studentId);
                BuildStudentWeeklyGeneralRows(cached.Items);

                var studentName = SelectedStudentWeeklyStudent?.FullName ?? "";
                StudentWeeklyScheduleStatusText = $"{studentName} — Total classes: {cached.Items.Count}";
                return;
            }

            IsStudentWeeklyScheduleBusy = true;
            StudentWeeklyScheduleStatusText = "Loading student timetable...";

            try
            {
                var items = GetStudentWeeklyAssignmentsFromStore(studentId);

                if (items.Count == 0)
                    items = (await _sqlite.GetStudentWeeklyAssignmentsAsync(studentId)).ToList();
                if (cts.IsCancellationRequested) return;

                // Cache update
                var entry = new ScheduleCacheEntry();
                entry.Items.AddRange(items);
                entry.LastUsedUtc = DateTime.UtcNow;
                _studentWeeklyScheduleCache[studentId] = entry;
                TouchScheduleCacheKey(studentId);

                BuildStudentWeeklyGeneralRows(items);

                var studentName = SelectedStudentWeeklyStudent?.FullName ?? "";
                StudentWeeklyScheduleStatusText = items.Count == 0
                    ? $"{studentName} — No scheduled classes."
                    : $"{studentName} — Total classes: {items.Count}";
            }
            catch
            {
                StudentWeeklyRowsGeneral.Clear();
                StudentWeeklyScheduleStatusText = "Unable to load the student timetable. Please check the database.";
            }
            finally
            {
                IsStudentWeeklyScheduleBusy = false;
            }
        }

        private List<StudentWeeklyAssignmentRow> GetStudentWeeklyAssignmentsFromStore(int studentId)
        {
            var res = new List<StudentWeeklyAssignmentRow>(64);

            // Fast lookups
            var assignmentsById = new Dictionary<int, Assignment>(capacity: Math.Max(32, Store.Assignments.Count));
            foreach (var a in Store.Assignments)
            {
                // Avoid duplicate keys just in case
                if (a.Id > 0 && !assignmentsById.ContainsKey(a.Id))
                    assignmentsById[a.Id] = a;
            }

            var slotsById = Store.Slots.ToDictionary(s => s.Id);
            var coursesById = Store.Courses.ToDictionary(c => c.Id);

            foreach (var enr in Store.StudentEnrollments)
            {
                if (enr.StudentId != studentId) continue;

                var asgId = (int)enr.AssignmentId;
                if (asgId <= 0) continue;

                if (!assignmentsById.TryGetValue(asgId, out var asg)) continue;
                if (!slotsById.TryGetValue(asg.SlotId, out var slot)) continue;

                // Resolve kind (THEORY/LAB). Prefer Assignment.Kind if present.
                var kind = asg.Kind;
                if (string.IsNullOrWhiteSpace(kind))
                {
                    // Fallback for older rows
                    kind = Store.PracticalCourseIds.Contains(asg.CourseId) ? "LAB" : "THEORY";
                }

                var sm = (slot.Start.Hour * 60) + slot.Start.Minute;
                var em = (slot.End.Hour * 60) + slot.End.Minute;

                // slot.Day is DayOfWeek in our Store model
                var slotDay = (int)slot.Day;

                res.Add(new StudentWeeklyAssignmentRow(
                    AssignmentId: asg.Id,
                    DepartmentId: asg.DepartmentId,
                    CourseId: asg.CourseId,
                    SectionIndex: asg.SectionIndex,
                    SlotId: asg.SlotId,
                    FacultyId: asg.FacultyId,
                    RoomId: asg.RoomId,
                    Status: asg.Status,
                    Kind: kind,
                    SlotDay: slotDay,
                    StartMinutes: sm,
                    EndMinutes: em
                ));
            }

            // stable ordering for a nicer UI
            res.Sort((a, b) =>
            {
                var d = a.SlotDay.CompareTo(b.SlotDay);
                if (d != 0) return d;
                d = a.StartMinutes.CompareTo(b.StartMinutes);
                if (d != 0) return d;
                return a.CourseId.CompareTo(b.CourseId);
            });

            return res;
        }

        private void BuildStudentWeeklyGeneralRows(IReadOnlyList<StudentWeeklyAssignmentRow> items)
        {
            StudentWeeklyRowsGeneral.Clear();

            // Prebuild lookup dictionaries for speed
            var slotById = Store.Slots.ToDictionary(s => s.Id);
            var courseNameById = Store.Courses.ToDictionary(c => c.Id, c => c.Name ?? "—");
            var facultyNameById = Store.Faculties.ToDictionary(f => f.Id, f => f.Name ?? "—");
            var roomNameById = Store.Rooms.ToDictionary(r => r.Id, r => r.Name ?? r.Id.ToString());

            // Build time pairs from store slots + student's assignments (to avoid missing rows)
            var pairs = new HashSet<(int start, int end)>();

            foreach (var s in Store.Slots)
            {
                var sm = (s.Start.Hour * 60) + s.Start.Minute;
                var em = (s.End.Hour * 60) + s.End.Minute;
                if (sm >= 0 && em > sm)
                    pairs.Add((sm, em));
            }

            foreach (var a in items)
            {
                if (a.StartMinutes >= 0 && a.EndMinutes > a.StartMinutes)
                {
                    pairs.Add((a.StartMinutes, a.EndMinutes));
                    continue;
                }

                if (slotById.TryGetValue(a.SlotId, out var slot))
                {
                    var sm = (slot.Start.Hour * 60) + slot.Start.Minute;
                    var em = (slot.End.Hour * 60) + slot.End.Minute;
                    if (sm >= 0 && em > sm)
                        pairs.Add((sm, em));
                }
            }

            var times = pairs.OrderBy(p => p.start).ThenBy(p => p.end).ToList();
            var rowIndex = new Dictionary<(int start, int end), int>();

            for (int i = 0; i < times.Count; i++)
            {
                var t = times[i];
                StudentWeeklyRowsGeneral.Add(new TimeRowModel
                {
                    Time = $"{FmtMinutes(t.start)} - {FmtMinutes(t.end)}"
                });
                rowIndex[t] = i;
            }

            foreach (var a in items)
            {
                // Day column
                int dayCol = DayIndexFromSlotDay(a.SlotDay);
                if (dayCol < 0)
                {
                    if (!slotById.TryGetValue(a.SlotId, out var slot)) continue;
                    dayCol = DayIndexFromDayOfWeek(slot.Day);
                }
                if (dayCol < 0 || dayCol >= 5) continue;

                // Time row
                (int start, int end) pair;
                if (a.StartMinutes >= 0 && a.EndMinutes > a.StartMinutes)
                {
                    pair = (a.StartMinutes, a.EndMinutes);
                }
                else
                {
                    if (!slotById.TryGetValue(a.SlotId, out var slot)) continue;
                    pair = ((slot.Start.Hour * 60) + slot.Start.Minute, (slot.End.Hour * 60) + slot.End.Minute);
                }

                if (!rowIndex.TryGetValue(pair, out var r))
                    continue;

                var courseName = courseNameById.TryGetValue(a.CourseId, out var cn) ? cn : "—";
                var facultyName = facultyNameById.TryGetValue(a.FacultyId, out var fn) ? fn : "—";
                var roomName = a.RoomId is null ? "" : (roomNameById.TryGetValue(a.RoomId.Value, out var rn) ? rn : a.RoomId.Value.ToString());

                string kindSuffix = AcademicEnglishText.KindSuffix(a.Kind);

                var sectionLabel = a.SectionIndex > 0 ? $" (Section {a.SectionIndex})" : "";

                var firstLine = (courseName + kindSuffix) + sectionLabel;
                string line = string.IsNullOrWhiteSpace(roomName)
                    ? $"{firstLine}\n{facultyName}"
                    : $"{firstLine}\n{facultyName}\nRoom: {roomName}";

                StudentWeeklyRowsGeneral[r].Cells[dayCol].Add(line);
            }
        }

        private static int DayIndexFromSlotDay(int slotDay)
        {
            // SlotEntity stores DayOfWeek as int (Sunday=0 ... Saturday=6)
            return slotDay switch
            {
                0 => 0, // Sunday
                1 => 1, // Monday
                2 => 2, // Tuesday
                3 => 3, // Wednesday
                4 => 4, // Thursday
                _ => -1
            };
        }

        private static int DayIndexFromDayOfWeek(DayOfWeek d) => d switch
        {
            DayOfWeek.Sunday => 0,
            DayOfWeek.Monday => 1,
            DayOfWeek.Tuesday => 2,
            DayOfWeek.Wednesday => 3,
            DayOfWeek.Thursday => 4,
            _ => -1
        };

        private static string FmtMinutes(int minutes)
        {
            if (minutes < 0) return "—";
            try
            {
                var t = TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(minutes));
                return t.ToString("HH:mm");
            }
            catch
            {
                return "—";
            }
        }
    }
}
