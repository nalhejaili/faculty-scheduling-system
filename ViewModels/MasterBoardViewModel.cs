using MiniTrainerScheduler.Models;
using MiniTrainerScheduler.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;

namespace MiniTrainerScheduler.ViewModels
{
    /// <summary>
    /// </summary>
    public sealed class MasterBoardViewModel : INotifyPropertyChanged
    {
        public ObservableCollection<TemplateRow> TemplateRows { get; } = new();

        public MasterBoardViewModel(DataStore store)
        {
            if (store is null) throw new ArgumentNullException(nameof(store));
            BuildTemplate(store);
        }
        private static string RtlLabel(string text)
            => "\u202B" + text + "\u202C";   // RLE ... PDF

        private void BuildTemplate(DataStore store)
        {
            TemplateRows.Clear();

            var courses = (store.Courses ?? new List<Course>())
                .GroupBy(c => c.Id)
                .ToDictionary(g => g.Key, g => g.First());

            var slots = (store.Slots ?? new List<Slot>())
                .GroupBy(s => s.Id)
                .ToDictionary(g => g.Key, g => g.OrderBy(x => x.Start).ThenBy(x => x.End).First());

            var faculties = (store.Faculties ?? new List<Faculty>())
                .GroupBy(f => f.Id)
                .ToDictionary(g => g.Key, g => g.First());

            var rooms = (store.Rooms ?? new List<Room>())
                .GroupBy(r => r.Id)
                .ToDictionary(g => g.Key, g => g.First());

            var assignments = store.Assignments ?? new List<Assignment>();

            var asgBySlot = assignments
                .GroupBy(a => a.SlotId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var timeKeys = (store.Slots ?? new List<Slot>())
                .Select(s => (s.Start, s.End))
                .Distinct()
                .OrderBy(k => k.Start)
                .ThenBy(k => k.End)
                .ToList();

            var days = new[]
            {
                DayOfWeek.Sunday,
                DayOfWeek.Monday,
                DayOfWeek.Tuesday,
                DayOfWeek.Wednesday,
                DayOfWeek.Thursday
            };

            foreach (var (start, end) in timeKeys)
            {
                var row = new TemplateRow($"{start:HH:mm} - {end:HH:mm}");

                foreach (var day in days)
                {
                    var slotIdsForThisCell = (store.Slots ?? new List<Slot>())
                        .Where(s => s.Day == day && s.Start == start && s.End == end)
                        .Select(s => s.Id)
                        .ToHashSet();

                    var cellAssignments = slotIdsForThisCell
                        .SelectMany(id => asgBySlot.TryGetValue(id, out var list) ? list : Enumerable.Empty<Assignment>())
                        .ToList();

                    var lines = new List<string>();

                    foreach (var a in cellAssignments)
                    {
                        string courseName = courses.TryGetValue(a.CourseId, out var c)
                            ? c.Name
                            : $"Course#{a.CourseId}";

                        string facName = faculties.TryGetValue(a.FacultyId, out var f)
                            ? f.Name
                            : $"F#{a.FacultyId}";

                        string roomName = "";
                        if (a.RoomId.HasValue && rooms.TryGetValue(a.RoomId.Value, out var r))
                            roomName = r.Name;

                        string section = $"Sec. {a.SectionIndex}";

                        string kindSuffix = AcademicEnglishText.KindSuffix(a.Kind);

                        string courseWithKind = string.IsNullOrWhiteSpace(kindSuffix)
                            ? courseName
                            : courseName + kindSuffix;

                        string secondLine = facName;
                        if (!string.IsNullOrWhiteSpace(roomName))
                            secondLine = $"{facName} — {roomName}";

                        string label = $"{courseWithKind} — {section}\n{secondLine}";
                        lines.Add(label);
                    }

                    if (store.FacultySlotBlocks != null)
                    {
                        var blocksForThisCell = store.FacultySlotBlocks
                            .Where(b => slotIdsForThisCell.Contains(b.SlotId))
                            .ToList();

                        foreach (var blk in blocksForThisCell)
                        {
                            if (!faculties.TryGetValue(blk.FacultyId, out var f))
                                continue;

                            string reason = AcademicEnglishText.FacultyDutyLabel(blk.Reason);

                            string label = $"{reason}\n{f.Name}";
                            lines.Add(label);
                        }
                    }

                    string t1 = lines.Count > 0 ? lines[0] : "";
                    string t2 = lines.Count > 1 ? lines[1] : "";
                    string t3 = lines.Count > 2 ? string.Join("\n", lines.Skip(2)) : "";

                    row.SetDayTracks(day, new[] { t1, t2, t3 });
                }

                TemplateRows.Add(row);
            }

        }

        public sealed class DayCell : INotifyPropertyChanged
        {
            string _track1 = "", _track2 = "", _track3 = "";

            public string Track1
            {
                get => _track1;
                set { if (_track1 != value) { _track1 = value; OnPropertyChanged(nameof(Track1)); } }
            }

            public string Track2
            {
                get => _track2;
                set { if (_track2 != value) { _track2 = value; OnPropertyChanged(nameof(Track2)); } }
            }

            public string Track3
            {
                get => _track3;
                set { if (_track3 != value) { _track3 = value; OnPropertyChanged(nameof(Track3)); } }
            }

            public event PropertyChangedEventHandler? PropertyChanged;
            void OnPropertyChanged(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public sealed class TemplateRow
        {
            public string Time { get; }

            public DayCell Sun { get; } = new();
            public DayCell Mon { get; } = new();
            public DayCell Tue { get; } = new();
            public DayCell Wed { get; } = new();
            public DayCell Thu { get; } = new();

            public TemplateRow(string time)
            {
                Time = time;
            }

            public void SetDayTracks(DayOfWeek day, string[] tracks)
            {
                var cell = day switch
                {
                    DayOfWeek.Sunday => Sun,
                    DayOfWeek.Monday => Mon,
                    DayOfWeek.Tuesday => Tue,
                    DayOfWeek.Wednesday => Wed,
                    DayOfWeek.Thursday => Thu,
                    _ => null
                };

                if (cell is null) return;

                string t1 = tracks.Length > 0 ? tracks[0] : "";
                string t2 = tracks.Length > 1 ? tracks[1] : "";
                string t3 = tracks.Length > 2 ? tracks[2] : "";

                cell.Track1 = t1;
                cell.Track2 = t2;
                cell.Track3 = t3;
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        void OnPropertyChanged(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
