namespace MiniTrainerScheduler.ViewModels
{
    public sealed partial class MainViewModel
    {
        bool CanAddCurrentDept()
            => SelectedDepartment is not null 
               && SelectedLevel is not null 
               && SelectedPlanRow is not null
               && (SelectedPlanRow.SectionsRequested > 0 
                   || SelectedPlanRow.StudentsThisTerm > 0 
                   || SelectedPlanRow.Repeaters > 0);
    }
}
