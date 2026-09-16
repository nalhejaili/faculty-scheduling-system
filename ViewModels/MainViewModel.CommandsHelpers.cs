using MiniTrainerScheduler.Services;

namespace MiniTrainerScheduler.ViewModels
{
    public sealed partial class MainViewModel
    {
        void RaiseAllCanExec()
        {
            (GenerateCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ExportCsvCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ExportCsvByDeptCommand as RelayCommand)?.RaiseCanExecuteChanged();

            (SaveQueuedPlanJsonCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (LoadQueuedPlanJsonCommand as RelayCommand)?.RaiseCanExecuteChanged();

            (ImportMasterCsvCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ImportPlanCsvCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ImportJsonCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (AddFdoCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (RemoveFdoCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (AddCfoCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (RemoveCfoCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (AddFloCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (RemoveFloCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (AddCurrentDeptToPlanCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (AddManualToQueuedPlanCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (AddSectionOverrideCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (RemoveSectionOverrideCommand as RelayCommand)?.RaiseCanExecuteChanged();

            (BuildQueuedPlanFromExpectedRegistrationCommand as RelayCommand)?.RaiseCanExecuteChanged();

            (ImportStudentsJsonCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (DistributeStudentsCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (RefreshStudentScheduleCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ExportStudentScheduleCsvCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ExportStudentUnassignedCsvCommand as RelayCommand)?.RaiseCanExecuteChanged();

            (RefreshStudentWeeklyStudentsCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }
}
