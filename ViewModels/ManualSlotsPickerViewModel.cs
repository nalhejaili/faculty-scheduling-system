using MiniTrainerScheduler.Models;
using MiniTrainerScheduler.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace MiniTrainerScheduler.ViewModels
{
    public sealed class ManualSlotsPickerViewModel : INotifyPropertyChanged
    {
        public sealed class Cell : INotifyPropertyChanged
        {
            public Slot? Slot { get; }

            bool _isSelected;
            readonly ManualSlotsPickerViewModel _owner;

            public bool IsEnabled => Slot != null;

            public bool IsSelected
            {
                get => _isSelected;
                set
                {
                    if (!_isSelected && value)
                    {
                        if (!_owner.CanSelectMore)
                            return;
                    }

                    if (_isSelected == value) return;

                    _isSelected = value;
                    OnPropertyChanged();
                    _owner.RecalculateSelectedCount();
                }
            }

            public Cell(ManualSlotsPickerViewModel owner, Slot? slot)
            {
                _owner = owner;
                Slot = slot;
            }

            public event PropertyChangedEventHandler? PropertyChanged;
            void OnPropertyChanged([CallerMemberName] string? propertyName = null)
                => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public sealed class Row
        {
            public string TimeText { get; }
            public Cell[] Cells { get; }

            public Row(string timeText, Cell[] cells)
            {
                TimeText = timeText;
                Cells = cells;
            }
        }

        public ObservableCollection<Row> Rows { get; } = new();

        public int MaxSlots { get; }
        public bool RequireExact { get; }
        public string HeaderText { get; }
        public string RequirementLabel { get; }

        internal bool CanSelectMore => SelectedCount < MaxSlots;

        readonly Dictionary<int, Cell> _cellBySlotId = new();

        int _selectedCount;
        public int SelectedCount
        {
            get => _selectedCount;
            private set
            {
                if (_selectedCount == value) return;
                _selectedCount = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanAccept));
            }
        }

        public bool CanAccept => RequireExact ? SelectedCount == MaxSlots : SelectedCount > 0;

        public ManualSlotsPickerViewModel(
            DataStore store,
            int maxSlots,
            IEnumerable<int>? preselectedSlotIds = null,
            string? headerText = null,
            bool requireExact = false)
        {
            if (store is null)
                throw new ArgumentNullException(nameof(store));

            MaxSlots = maxSlots <= 0 ? 1 : maxSlots;
            RequireExact = requireExact;

            HeaderText = string.IsNullOrWhiteSpace(headerText)
                ? "Select preferred weekly class meeting times"
                : headerText!.Trim();

            RequirementLabel = RequireExact ? "Required weekly meetings: " : "Maximum weekly meetings: ";

            var allSlots = store.Slots ?? new List<Slot>();

            var groups = allSlots
                .OrderBy(s => s.Start)
                .GroupBy(s => new { s.Start, s.End })
                .OrderBy(g => g.Key.Start);

            foreach (var g in groups)
            {
                string timeText = $"{FormatTime(g.Key.Start)}-{FormatTime(g.Key.End)}";

                var cells = new Cell[5];
                for (int col = 0; col < 5; col++)
                {
                    Slot? slotForCol = null;

                    foreach (var s in g)
                    {
                        int idx = NormalizeDayIndex(s.Day);
                        if (idx == col)
                        {
                            slotForCol = s;
                            break;
                        }
                    }

                    var cell = new Cell(this, slotForCol);
                    cells[col] = cell;

                    if (slotForCol != null)
                        _cellBySlotId[slotForCol.Id] = cell;
                }

                Rows.Add(new Row(timeText, cells));
            }

            if (preselectedSlotIds != null)
                ApplyPreselection(preselectedSlotIds);

            RecalculateSelectedCount();
        }

        void ApplyPreselection(IEnumerable<int> preselectedSlotIds)
        {
            var seen = new HashSet<int>();
            foreach (var id in preselectedSlotIds)
            {
                if (id <= 0) continue;
                if (!seen.Add(id)) continue;

                if (_cellBySlotId.TryGetValue(id, out var cell))
                    cell.IsSelected = true;
            }
        }

        static string FormatTime(object value)
        {
            if (value is TimeSpan ts)
                return ts.ToString(@"hh\\:mm");

            if (value is TimeOnly to)
                return to.ToString("HH:mm");

            return value?.ToString() ?? string.Empty;
        }

        static int NormalizeDayIndex(object? dayValue)
        {
            if (dayValue is null)
                return -1;

            int raw;
            switch (dayValue)
            {
                case int i:
                    raw = i;
                    break;
                case byte b:
                    raw = b;
                    break;
                case sbyte sb:
                    raw = sb;
                    break;
                case DayOfWeek dow:
                    return dow switch
                    {
                        DayOfWeek.Sunday => 0,
                        DayOfWeek.Monday => 1,
                        DayOfWeek.Tuesday => 2,
                        DayOfWeek.Wednesday => 3,
                        DayOfWeek.Thursday => 4,
                        _ => -1
                    };
                default:
                    return -1;
            }

            if (raw >= 0 && raw <= 4) return raw;
            if (raw >= 1 && raw <= 5) return raw - 1;

            return -1;
        }

        void RecalculateSelectedCount()
        {
            SelectedCount = Rows
                .SelectMany(r => r.Cells)
                .Count(c => c.Slot != null && c.IsSelected);
        }

        public Slot[] GetSelectedSlotsLimited()
        {
            return Rows
                .SelectMany(r => r.Cells)
                .Where(c => c.Slot != null && c.IsSelected)
                .Select(c => c.Slot!)
                .Take(MaxSlots)
                .ToArray();
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
