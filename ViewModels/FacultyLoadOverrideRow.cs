using System.ComponentModel;

namespace MiniTrainerScheduler.ViewModels
{
    public sealed class FacultyLoadOverrideRow : INotifyPropertyChanged
    {
        public int FacultyId { get; set; }
        public string FacultyName { get; set; } = "";

        private int _maxHours;
        public int MaxHours
        {
            get => _maxHours;
            set { _maxHours = value; PropertyChanged?.Invoke(this, new(nameof(MaxHours))); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
