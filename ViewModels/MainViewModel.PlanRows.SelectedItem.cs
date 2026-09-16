using System.ComponentModel;

namespace MiniTrainerScheduler.ViewModels
{
    public sealed partial class MainViewModel : INotifyPropertyChanged
    {
        private CoursePlanRow? _selectedPlanRow;
        public CoursePlanRow? SelectedPlanRow
        {
            get => _selectedPlanRow;
            set { _selectedPlanRow = value; OnPropertyChanged(); RaiseAllCanExec(); }
        }
    }
}
