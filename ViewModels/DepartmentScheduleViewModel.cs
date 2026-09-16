using TrainerScheduler.Services;
﻿using MiniTrainerScheduler.Models;
using MiniTrainerScheduler.Services;
using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Printing;
using System.Windows.Media;
using MiniTrainerScheduler.ViewModels.Adapters;
using System.Windows;
using TrainerScheduler.Security;

namespace MiniTrainerScheduler.ViewModels
{
    /// <summary>
    /// </summary>
    public sealed class DepartmentScheduleViewModel : INotifyPropertyChanged
    {
        public DepartmentScheduleViewModel(DataStore store, int? scopeDepartmentId = null)
        {
            Store = store ?? throw new ArgumentNullException(nameof(store));

            // 4B-5c: Supervisor scoping — hide all other departments completely.
            // Priority: explicit scopeDepartmentId from caller, otherwise infer from authenticated session.
            int? effectiveScope = scopeDepartmentId;
            if (effectiveScope is null && AuthContext.IsSupervisor)
                effectiveScope = AuthContext.Current?.DepartmentId;

            var depts = Store.Departments.AsEnumerable();
            if (effectiveScope is int sid && sid > 0)
                depts = depts.Where(d => d.Id == sid);

            foreach (var d in depts.OrderBy(d => d.Name))
                Departments.Add(d);

            SelectedDepartment = Departments.FirstOrDefault();

            if ((effectiveScope is int sid2 && sid2 > 0) && SelectedDepartment is null)
            {
                MessageBox.Show(
                    "The department timetable cannot be opened because the supervisor's department is not available in the current data.\n\n" +
                    "Please add the departments to the database first.",
                    "Notice",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            RefreshLevels();
            RefreshRows();
            RefreshTemplate();
        }

        public bool CanSelectDepartment => !AuthContext.IsSupervisor;

        public DataStore Store { get; }

        public ObservableCollection<Department> Departments { get; } = new();
        public ObservableCollection<int> Levels { get; } = new();

        public ObservableCollection<DeptRow> Rows { get; } = new();

        public ObservableCollection<TimeRowModel> RowsGeneral { get; } = new();
        private RowsProxy? _generalProxy;
        public RowsProxy GeneralProxy => _generalProxy ??= new RowsProxy(RowsGeneral);
        public sealed class RowsProxy
        {
            public ObservableCollection<TimeRowModel> Rows { get; }
            public RowsProxy(ObservableCollection<TimeRowModel> rows) { Rows = rows; }
        }

        public ObservableCollection<DeptTemplateRow> TemplateRows { get; } = new();

        private Department? _selectedDepartment;
        public Department? SelectedDepartment
        {
            get => _selectedDepartment;
            set
            {
                _selectedDepartment = value;
                OnPropertyChanged();
                RefreshLevels();
                RefreshRows();
                RefreshTemplate();
                BuildGeneralRows();
            }
        }

        private int? _selectedLevel;
        public int? SelectedLevel
        {
            get => _selectedLevel;
            set
            {
                _selectedLevel = value;
                OnPropertyChanged();
                RefreshRows();
                RefreshTemplate();
                BuildGeneralRows();
            }
        }

        private int _departmentTotalHours;
        public int DepartmentTotalHours
        {
            get => _departmentTotalHours;
            private set { _departmentTotalHours = value; OnPropertyChanged(); }
        }

        public sealed class DeptRow
        {
            public string Course { get; init; } = "";
            public int Section { get; init; }
            public string Slot { get; init; } = "";
            public int Hours { get; init; }
            public string Faculty { get; init; } = "";
            public string Room { get; init; } = "";
            public string Kind { get; init; } = "";
            public string Status { get; init; } = "";
        }

        void RefreshLevels()
        {
            Levels.Clear();
            if (SelectedDepartment is null) { SelectedLevel = null; return; }

            foreach (var lv in Store.LevelsForDepartment(SelectedDepartment.Id))
                Levels.Add(lv);

            if (Levels.Count == 0)
            {
                SelectedLevel = null;
            }
            else if (!SelectedLevel.HasValue || !Levels.Contains(SelectedLevel.Value))
            {
                SelectedLevel = Levels[0];
            }
        }

        public void RefreshRows()
        {
            Rows.Clear();
            DepartmentTotalHours = 0;
            if (SelectedDepartment is null) return;

            var query = Store.Assignments.Where(a => a.DepartmentId == SelectedDepartment.Id);

            if (SelectedLevel is int lvl)
                query = query.Where(a =>
                {
                    var c = Store.Courses.FirstOrDefault(x => x.Id == a.CourseId);
                    return c != null && c.Level == lvl;
                });

            foreach (var a in query)
            {
                var course = Store.Courses.FirstOrDefault(c => c.Id == a.CourseId);
                var slot = Store.Slots.FirstOrDefault(s => s.Id == a.SlotId);
                var room = a.RoomId is null ? null : Store.Rooms.FirstOrDefault(r => r.Id == a.RoomId);
                var facultyName = Store.Faculties.FirstOrDefault(f => f.Id == a.FacultyId)?.Name ?? "—";

                string kindArabic = AcademicEnglishText.KindLabel(a.Kind);

                Rows.Add(new DeptRow
                {
                    Course = course?.Name ?? "—",
                    Section = a.SectionIndex,
                    Slot = (slot is null || a.SlotId <= 0) ? "—" : AcademicEnglishText.Slot(slot),
                    Hours = (int)Math.Round(Convert.ToDouble(course?.HoursPerWeek ?? 0)),
                    Faculty = facultyName,
                    Room = room?.Name ?? (a.RoomId?.ToString() ?? ""),
                    Kind = kindArabic,
                    Status = a.Status
                });
            }

            var courseHours = Store.Courses.ToDictionary(c => c.Id, c => (int)Math.Round(Convert.ToDouble(c.HoursPerWeek)));

            var distinctSections = query
                .Select(a => new { a.CourseId, a.SectionIndex })
                .Distinct();

            DepartmentTotalHours = distinctSections
                .Sum(k => courseHours.TryGetValue(k.CourseId, out var h) ? h : 0);
        }

        public void RefreshTemplate()
        {
            TemplateRows.Clear();
            if (SelectedDepartment is null) return;

            var assignments = Store.Assignments.Where(a => a.DepartmentId == SelectedDepartment.Id);

            if (SelectedLevel is int lvl)
            {
                var idsAtLevel = new HashSet<int>(
                    Store.Courses.Where(c => c.Level == lvl).Select(c => c.Id)
                );
                assignments = assignments.Where(a => idsAtLevel.Contains(a.CourseId));
            }

            var slotById = Store.Slots.ToDictionary(s => s.Id);

            var map = new Dictionary<string, List<string>[]>();

            foreach (var a in assignments)
            {
                if (a.SlotId <= 0 || !slotById.TryGetValue(a.SlotId, out var s))
                    continue;

                var label = AcademicEnglishText.Slot(s);
                if (string.IsNullOrWhiteSpace(label))
                    continue;

                int day = GetDayIndexFromLabel(label);
                if (day < 0) continue;

                string timeLabel = GetTimeLabel(label);

                if (!map.TryGetValue(timeLabel, out var perDay))
                {
                    perDay = new[]
                    {
                        new List<string>(), new List<string>(),
                        new List<string>(), new List<string>(),
                        new List<string>()
                    };
                    map[timeLabel] = perDay;
                }

                var courseName = Store.Courses.FirstOrDefault(c => c.Id == a.CourseId)?.Name ?? "—";
                var teacher = Store.Faculties.FirstOrDefault(f => f.Id == a.FacultyId)?.Name ?? "—";
                var roomName = a.RoomId is null
                    ? ""
                    : (Store.Rooms.FirstOrDefault(r => r.Id == a.RoomId)?.Name ?? a.RoomId.Value.ToString());

                string kindSuffix = AcademicEnglishText.KindSuffix(a.Kind);

                string courseWithKind = string.IsNullOrWhiteSpace(kindSuffix)
                    ? courseName
                    : courseName + kindSuffix;

                string section = $"Sec. {a.SectionIndex}";

                string secondLine = teacher;
                if (!string.IsNullOrWhiteSpace(roomName))
                    secondLine = $"{teacher} — {roomName}";

                string cellText = $"{courseWithKind} — {section}\n{secondLine}";

                perDay[day].Add(cellText);
            }

            var ordered = map.Keys
                .Select(k => new { Key = k, Start = GetStartMinutes(k) })
                .OrderBy(x => x.Start)
                .ThenBy(x => x.Key)
                .ToList();

            foreach (var rowKey in ordered)
            {
                var row = new DeptTemplateRow { Time = rowKey.Key };

                var perDay = map[rowKey.Key];
                for (int d = 0; d < 5; d++)
                    row.SetDayTracks(d, perDay[d].Take(3).ToArray());

                TemplateRows.Add(row);
            }
        }


        private static int GetDayIndexFromLabel(string slotLabel)
        {
            if (slotLabel.Contains("Sunday")) return 0;
            if (slotLabel.Contains("Monday")) return 1;
            if (slotLabel.Contains("Tuesday")) return 2;
            if (slotLabel.Contains("Wednesday")) return 3;
            if (slotLabel.Contains("Thursday")) return 4;
            return -1;
        }

        private static string GetTimeLabel(string slotLabel)
        {
            string s = slotLabel;
            s = s.Replace("Sunday", "").Replace("Monday", "")
                 .Replace("Tuesday", "").Replace("Wednesday", "")
                 .Replace("Thursday", "");
            return s.Trim().Trim('\u060C').Trim();
        }

        private static int GetStartMinutes(string timeLabel)
        {
            try
            {
                var parts = timeLabel.Split('-', '–');
                var start = parts[0].Trim();
                if (TimeSpan.TryParse(start, out var ts))
                    return (int)ts.TotalMinutes;
            }
            catch (Exception ex)
            {
                DiagnosticsLog.TryWrite(ex);
            }
            return int.MaxValue;
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
            if (SelectedDepartment is null)
            {
                RowsGeneral.Clear();
                return;
            }

            string Fmt(object t)
            {
                if (t is TimeSpan ts) return ts.ToString(@"hh\:mm");
                if (t is TimeOnly to) return to.ToString("HH:mm");
                return t?.ToString() ?? "";
            }
            string Key(object s1, object s2) => $"{Fmt(s1)}-{Fmt(s2)}";

            var times = Store.Slots.Select(s => new { s.Start, s.End })
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

            var query = Store.Assignments.AsEnumerable();
            query = query.Where(a => a.DepartmentId == SelectedDepartment!.Id);
            if (SelectedLevel is int lvl)
            {
                query = query.Where(a =>
                {
                    var c = Store.Courses.FirstOrDefault(x => x.Id == a.CourseId);
                    return c != null && c.Level == lvl;
                });
            }

            foreach (var a in query)
            {
                var slot = Store.Slots.FirstOrDefault(s => s.Id == a.SlotId);
                if (slot is null) continue;

                if (!indexByKey.TryGetValue(Key(slot.Start, slot.End), out var r)) continue;
                int c = DayIndex(slot.Day);
                if (c < 0 || c >= 5) continue;

                var courseName = Store.Courses.FirstOrDefault(x => x.Id == a.CourseId)?.Name ?? "—";
                var teacher = Store.Faculties.FirstOrDefault(f => f.Id == a.FacultyId)?.Name ?? "—";
                var roomName = a.RoomId is null
                    ? ""
                    : (Store.Rooms.FirstOrDefault(rr => rr.Id == a.RoomId)?.Name ?? a.RoomId.Value.ToString());

                string kindSuffix = AcademicEnglishText.KindSuffix(a.Kind);

                string courseWithKind = string.IsNullOrWhiteSpace(kindSuffix)
                    ? courseName
                    : courseName + kindSuffix;

                string line = string.IsNullOrWhiteSpace(roomName)
                    ? $"{courseWithKind}\n{teacher}"
                    : $"{courseWithKind}\n{teacher}\nRoom: {roomName}";

                RowsGeneral[r].Cells[c].Add(line);
            }
        }

        // INotifyPropertyChanged
        public event PropertyChangedEventHandler? PropertyChanged;
        void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public sealed class DeptTemplateRow
        {
            public string Time { get; set; } = "";
            public DayCell Sun { get; } = new();
            public DayCell Mon { get; } = new();
            public DayCell Tue { get; } = new();
            public DayCell Wed { get; } = new();
            public DayCell Thu { get; } = new();

            public void SetDayTracks(int dayIndex, string[] tracks)
            {
                DayCell cell = dayIndex switch
                {
                    0 => Sun,
                    1 => Mon,
                    2 => Tue,
                    3 => Wed,
                    4 => Thu,
                    _ => Sun
                };
                cell.Track1 = tracks.ElementAtOrDefault(0) ?? "";
                cell.Track2 = tracks.ElementAtOrDefault(1) ?? "";
                cell.Track3 = tracks.ElementAtOrDefault(2) ?? "";
            }
        }

        public sealed class DayCell : INotifyPropertyChanged
        {
            string _t1 = "", _t2 = "", _t3 = "";
            public string Track1 { get => _t1; set { _t1 = value; OnPropertyChanged(nameof(Track1)); } }
            public string Track2 { get => _t2; set { _t2 = value; OnPropertyChanged(nameof(Track2)); } }
            public string Track3 { get => _t3; set { _t3 = value; OnPropertyChanged(nameof(Track3)); } }

            public event PropertyChangedEventHandler? PropertyChanged;
            void OnPropertyChanged(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
        }
    }
}
