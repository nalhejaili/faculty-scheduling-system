using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace MiniTrainerScheduler.ViewModels
{
    /// <summary>
    /// </summary>
    public sealed class QueuedPlanRow : INotifyPropertyChanged
    {
        public int DepartmentId { get; set; }
        public string DepartmentName { get; set; } = "";
        public int Level { get; set; }

        public int CourseId { get; set; }
        public string CourseName { get; set; } = "";

        private int? _preferredFacultyId;
        public int? PreferredFacultyId
        {
            get => _preferredFacultyId;
            set
            {
                if (_preferredFacultyId == value) return;
                _preferredFacultyId = value;
                OnPropertyChanged(nameof(PreferredFacultyId));
            }
        }

        private int? _preferredSlotId;
        public int? PreferredSlotId
        {
            get => _preferredSlotId;
            set
            {
                if (_preferredSlotId == value) return;
                _preferredSlotId = value;
                OnPropertyChanged(nameof(PreferredSlotId));
            }
        }

        private int? _preferredRoomId;
        public int? PreferredRoomId
        {
            get => _preferredRoomId;
            set
            {
                if (_preferredRoomId == value) return;
                _preferredRoomId = value;
                OnPropertyChanged(nameof(PreferredRoomId));
            }
        }


        double _theoryHoursRequired;
        public double TheoryHoursRequired
        {
            get => _theoryHoursRequired;
            set
            {
                if (Math.Abs(_theoryHoursRequired - value) < 0.0001) return;
                _theoryHoursRequired = value < 0 ? 0 : value;
                OnPropertyChanged(nameof(TheoryHoursRequired));
                NotifySlotsDerivedChanged();
            }
        }

        double _practicalHoursRequired;
        public double PracticalHoursRequired
        {
            get => _practicalHoursRequired;
            set
            {
                if (Math.Abs(_practicalHoursRequired - value) < 0.0001) return;
                _practicalHoursRequired = value < 0 ? 0 : value;
                OnPropertyChanged(nameof(PracticalHoursRequired));
                NotifySlotsDerivedChanged();
            }
        }

        List<int> _theorySlotIds = new();
        public List<int> TheorySlotIds
        {
            get => _theorySlotIds;
            set
            {
                _theorySlotIds = value ?? new List<int>();
                OnPropertyChanged(nameof(TheorySlotIds));
                NotifySlotsDerivedChanged();
            }
        }

        List<int> _practicalSlotIds = new();
        public List<int> PracticalSlotIds
        {
            get => _practicalSlotIds;
            set
            {
                _practicalSlotIds = value ?? new List<int>();
                OnPropertyChanged(nameof(PracticalSlotIds));
                NotifySlotsDerivedChanged();
            }
        }

        public int TheorySlotsNeeded => TheoryHoursRequired <= 0 ? 0 : (int)Math.Ceiling(TheoryHoursRequired);
        public int PracticalSlotsNeeded => PracticalHoursRequired <= 0 ? 0 : (int)Math.Ceiling(PracticalHoursRequired);

        public bool HasTheoryPart => TheoryHoursRequired > 0;
        public bool HasPracticalPart => PracticalHoursRequired > 0;

        public int TheorySlotsSelectedCount => TheorySlotIds?.Count ?? 0;
        public int PracticalSlotsSelectedCount => PracticalSlotIds?.Count ?? 0;

        public string SlotsSummary
        {
            get
            {
                var thNeed = TheorySlotsNeeded;
                var prNeed = PracticalSlotsNeeded;
                if (thNeed <= 0 && prNeed <= 0) return "—";

                if (thNeed > 0 && prNeed > 0)
                    return $"Lecture {TheorySlotsSelectedCount}/{thNeed} | Lab {PracticalSlotsSelectedCount}/{prNeed}";
                if (thNeed > 0)
                    return $"Lecture {TheorySlotsSelectedCount}/{thNeed}";
                return $"Lab {PracticalSlotsSelectedCount}/{prNeed}";
            }
        }

        void NotifySlotsDerivedChanged()
        {
            OnPropertyChanged(nameof(TheorySlotsNeeded));
            OnPropertyChanged(nameof(PracticalSlotsNeeded));
            OnPropertyChanged(nameof(HasTheoryPart));
            OnPropertyChanged(nameof(HasPracticalPart));
            OnPropertyChanged(nameof(TheorySlotsSelectedCount));
            OnPropertyChanged(nameof(PracticalSlotsSelectedCount));
            OnPropertyChanged(nameof(SlotsSummary));
        }


        private int _studentsThisTerm;
        public int StudentsThisTerm
        {
            get => _studentsThisTerm;
            set
            {
                var v = value < 0 ? 0 : value;
                if (_studentsThisTerm == v) return;
                _studentsThisTerm = v;
                OnPropertyChanged(nameof(StudentsThisTerm));
            }
        }

        private int _repeaters;
        public int Repeaters
        {
            get => _repeaters;
            set
            {
                var v = value < 0 ? 0 : value;
                if (_repeaters == v) return;
                _repeaters = v;
                OnPropertyChanged(nameof(Repeaters));
            }
        }

        public int SectionIndex { get; set; } = 1;

        private bool _isPractical;
        public bool IsPractical
        {
            get => _isPractical;
            set
            {
                if (_isPractical == value) return;
                _isPractical = value;
                OnPropertyChanged(nameof(IsPractical));
            }
        }

        private int _sections = 1;
        public int SectionsRequested
        {
            get => _sections;
            set
            {
                var v = value < 0 ? 0 : value;
                if (_sections == v) return;
                _sections = v;
                OnPropertyChanged(nameof(SectionsRequested));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged(string propertyName)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
